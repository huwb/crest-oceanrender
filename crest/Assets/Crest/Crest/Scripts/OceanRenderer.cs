// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using System.Collections.Generic;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using Crest.Internal;
#if UNITY_EDITOR
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.Experimental.SceneManagement;
#endif

#if !UNITY_2020_3_OR_NEWER
#error This version of Crest requires Unity 2020.3 or later.
#endif

namespace Crest
{
    /// <summary>
    /// The main script for the ocean system. Attach this to a GameObject to create an ocean. This script initializes the various data types and systems
    /// and moves/scales the ocean based on the viewpoint. It also hosts a number of global settings that can be tweaked here.
    /// </summary>
    [ExecuteAlways, SelectionBase]
    [AddComponentMenu(Internal.Constants.MENU_PREFIX_SCRIPTS + "Ocean Renderer")]
    [HelpURL(Constants.HELP_URL_GENERAL)]
    public partial class OceanRenderer : MonoBehaviour
    {
        /// <summary>
        /// The version of this asset. Can be used to migrate across versions. This value should
        /// only be changed when the editor upgrades the version.
        /// </summary>
        [SerializeField, HideInInspector]
#pragma warning disable 414
        int _version = 0;
#pragma warning restore 414

        [Tooltip("Base wind speed in km/h. Controls wave conditions. Can be overridden on ShapeGerstner components."), Range(0, 150f, power: 2f)]
        public float _globalWindSpeed = 10f;

        [Tooltip("The viewpoint which drives the ocean detail. Defaults to the camera."), SerializeField]
        Transform _viewpoint;
        public Transform Viewpoint
        {
            get
            {
#if UNITY_EDITOR
                if (_followSceneCamera)
                {
                    var sceneViewCamera = EditorHelpers.EditorHelpers.GetActiveSceneViewCamera();
                    if (sceneViewCamera != null)
                    {
                        return sceneViewCamera.transform;
                    }
                }
#endif
                if (_viewpoint != null)
                {
                    return _viewpoint;
                }

                // Even with performance improvements, it is still good to cache whenever possible.
                var camera = ViewCamera;

                if (camera != null)
                {
                    return camera.transform;
                }

                return null;
            }
            set
            {
                _viewpoint = value;
            }
        }

        [Tooltip("The camera which drives the ocean data. Defaults to main camera."), SerializeField]
        Camera _camera;
        public Camera ViewCamera
        {
            get
            {
#if UNITY_EDITOR
                if (_followSceneCamera)
                {
                    var sceneViewCamera = EditorHelpers.EditorHelpers.GetActiveSceneViewCamera();
                    if (sceneViewCamera != null)
                    {
                        return sceneViewCamera;
                    }
                }
#endif

                if (_camera != null)
                {
                    return _camera;
                }

                // Unity has greatly improved performance of this operation in 2019.4.9.
                return Camera.main;
            }
            set
            {
                _camera = value;
            }
        }

        public Transform Root { get; private set; }

        // does not respond to _timeProvider changing in inspector

        // Loosely a stack for time providers. The last TP in the list is the active one. When a TP gets
        // added to the stack, it is bumped to the top of the list. When a TP is removed, all instances
        // of it are removed from the stack. This is less rigid than a real stack which would be harder
        // to use as users have to keep a close eye on the order that things are pushed/popped.
        public List<ITimeProvider> _timeProviderStack = new List<ITimeProvider>();

        [Tooltip("Optional provider for time, can be used to hard-code time for automation, or provide server time. Defaults to local Unity time."), SerializeField]
        TimeProviderBase _timeProvider = null;
        public ITimeProvider TimeProvider
        {
            get => _timeProviderStack[_timeProviderStack.Count - 1];
        }

        // Put a time provider at the top of the stack
        public void PushTimeProvider(ITimeProvider tp)
        {
            Debug.Assert(tp != null, "Null time provider pushed");

            // Remove any instances of it already in the stack
            PopTimeProvider(tp);

            // Add it to the top
            _timeProviderStack.Add(tp);
        }

        // Remove a time provider from the stack
        public void PopTimeProvider(ITimeProvider tp)
        {
            Debug.Assert(tp != null, "Null time provider popped");

            _timeProviderStack.RemoveAll(candidate => candidate == tp);
        }

        public float CurrentTime => TimeProvider.CurrentTime;
        public float DeltaTime => TimeProvider.DeltaTime;
        public float DeltaTimeDynamics => TimeProvider.DeltaTimeDynamics;

        [Tooltip("The primary directional light. Required if shadowing is enabled.")]
        public Light _primaryLight;
        [Tooltip("If Primary Light is not set, search the scene for all directional lights and pick the brightest to use as the sun light.")]
        [SerializeField, Predicated("_primaryLight", true), DecoratedField]
        bool _searchForPrimaryLightOnStartup = true;

        [Header("Ocean Params")]

        [SerializeField, Tooltip("Material to use for the ocean surface")]
        internal Material _material = null;
        public Material OceanMaterial { get { return _material; } set { _material = value; } }

        [System.Obsolete("Use the _layer field instead."), HideInInspector, SerializeField]
        string _layerName = "";
        [System.Obsolete("Use the Layer property instead.")]
        public string LayerName { get { return _layerName; } }

        [HelpBox("The <i>Layer</i> property needs to migrate the deprecated <i>Layer Name</i> property before it can be used. Please see the bottom of this component for a fix button.", HelpBoxAttribute.MessageType.Warning, HelpBoxAttribute.Visibility.PropertyDisabled, order = 1)]
        [Tooltip("The ocean tile renderers will have this layer.")]
        [SerializeField, Predicated("_layerName", inverted: true), Layer]
        int _layer = 4; // Water
        public int Layer => _layer;

        [SerializeField, Delayed, Tooltip("Multiplier for physics gravity."), Range(0f, 10f)]
        float _gravityMultiplier = 1f;
        public float Gravity { get { return _gravityMultiplier * Physics.gravity.magnitude; } }


        [Header("Detail Params")]

        [Range(2, 16)]
        [Tooltip("Min number of verts / shape texels per wave."), SerializeField]
        float _minTexelsPerWave = 3f;
        public float MinTexelsPerWave => _minTexelsPerWave;

        [Delayed, Tooltip("The smallest scale the ocean can be."), SerializeField]
        float _minScale = 8f;

        [Delayed, Tooltip("The largest scale the ocean can be (-1 for unlimited)."), SerializeField]
        float _maxScale = 256f;

        [Tooltip("Drops the height for maximum ocean detail based on waves. This means if there are big waves, max detail level is reached at a lower height, which can help visual range when there are very large waves and camera is at sea level."), SerializeField, Range(0f, 1f)]
        float _dropDetailHeightBasedOnWaves = 0.2f;

        [SerializeField, Delayed, Tooltip("Resolution of ocean LOD data. Use even numbers like 256 or 384. This is 4x the old 'Base Vert Density' param, so if you used 64 for this param, set this to 256.")]
        int _lodDataResolution = 256;
        public int LodDataResolution { get { return _lodDataResolution; } }

        [SerializeField, Delayed, Tooltip("How much of the water shape gets tessellated by geometry. If set to e.g. 4, every geometry quad will span 4x4 LOD data texels. Use power of 2 values like 1, 2, 4...")]
        int _geometryDownSampleFactor = 2;

        [SerializeField, Tooltip("Number of ocean tile scales/LODs to generate."), Range(2, LodDataMgr.MAX_LOD_COUNT)]
        int _lodCount = 7;

        [SerializeField, Range(UNDERWATER_CULL_LIMIT_MINIMUM, UNDERWATER_CULL_LIMIT_MAXIMUM)]
        [Tooltip("Proportion of visibility below which ocean will be culled underwater. The larger the number, the closer to the camera the ocean tiles will be culled.")]
        public float _underwaterCullLimit = 0.001f;
        internal const float UNDERWATER_CULL_LIMIT_MINIMUM = 0.000001f;
        internal const float UNDERWATER_CULL_LIMIT_MAXIMUM = 0.01f;


        [Header("Simulation Params")]

        [Embedded]
        public SimSettingsAnimatedWaves _simSettingsAnimatedWaves;

        [Tooltip("Water depth information used for shallow water, shoreline foam, wave attenuation, among others."), SerializeField]
        bool _createSeaFloorDepthData = true;
        public bool CreateSeaFloorDepthData { get { return _createSeaFloorDepthData; } }

        [Tooltip("Simulation of foam created in choppy water and dissipating over time."), SerializeField]
        bool _createFoamSim = true;
        public bool CreateFoamSim { get { return _createFoamSim; } }
        [Predicated("_createFoamSim"), Embedded]
        public SimSettingsFoam _simSettingsFoam;

        [Tooltip("Dynamic waves generated from interactions with objects such as boats."), SerializeField]
        bool _createDynamicWaveSim = false;
        public bool CreateDynamicWaveSim { get { return _createDynamicWaveSim; } }
        [Predicated("_createDynamicWaveSim"), Embedded]
        public SimSettingsWave _simSettingsDynamicWaves;

        [Tooltip("Horizontal motion of water body, akin to water currents."), SerializeField]
        bool _createFlowSim = false;
        public bool CreateFlowSim { get { return _createFlowSim; } }
        [Predicated("_createFlowSim"), Embedded]
        public SimSettingsFlow _simSettingsFlow;

        [Tooltip("Shadow information used for lighting water."), SerializeField]
        bool _createShadowData = false;
        public bool CreateShadowData { get { return _createShadowData; } }
        [Predicated("_createShadowData"), Embedded]
        public SimSettingsShadow _simSettingsShadow;

        [Tooltip("Clip surface information for clipping the ocean surface."), SerializeField]
        bool _createClipSurfaceData = false;
        public bool CreateClipSurfaceData { get { return _createClipSurfaceData; } }

        [Predicated("_createClipSurfaceData"), Embedded]
        public SimSettingsClipSurface _simSettingsClipSurface;

        public enum DefaultClippingState
        {
            NothingClipped,
            EverythingClipped,
        }
        [Tooltip("Whether to clip nothing by default (and clip inputs remove patches of surface), or to clip everything by default (and clip inputs add patches of surface).")]
        [Predicated("_createClipSurfaceData"), DecoratedField]
        public DefaultClippingState _defaultClippingState = DefaultClippingState.NothingClipped;

        [Header("Edit Mode Params")]

        [SerializeField]
#pragma warning disable 414
        bool _showOceanProxyPlane = false;
#pragma warning restore 414
#if UNITY_EDITOR
        GameObject _proxyPlane;
        const string kProxyShader = "Hidden/Crest/OceanProxy";
#endif

        [Tooltip("Sets the update rate of the ocean system when in edit mode. Can be reduced to save power."), Range(0f, 60f), SerializeField]
#pragma warning disable 414
        float _editModeFPS = 30f;
#pragma warning restore 414

        [Tooltip("Move ocean with Scene view camera if Scene window is focused."), SerializeField, Predicated("_showOceanProxyPlane", true), DecoratedField]
#pragma warning disable 414
        bool _followSceneCamera = true;
#pragma warning restore 414

        [Header("Server Settings")]
        [Tooltip("Emulate batch mode which models running without a display (but with a GPU available). Equivalent to running standalone build with -batchmode argument."), SerializeField]
        bool _forceBatchMode = false;
        [Tooltip("Emulate running on a client without a GPU. Equivalent to running standalone with -nographics argument."), SerializeField]
        bool _forceNoGPU = false;

        [Header("Debug Params")]

        [Tooltip("Attach debug gui that adds some controls and allows to visualise the ocean data."), SerializeField]
        bool _attachDebugGUI = false;
        [Tooltip("Move ocean with viewpoint.")]
        bool _followViewpoint = true;
        [Tooltip("Set the ocean surface tiles hidden by default to clean up the hierarchy.")]
        public bool _hideOceanTileGameObjects = true;
        [HideInInspector, Tooltip("Whether to generate ocean geometry tiles uniformly (with overlaps).")]
        public bool _uniformTiles = false;
        [HideInInspector, Tooltip("Disable generating a wide strip of triangles at the outer edge to extend ocean to edge of view frustum.")]
        public bool _disableSkirt = false;

        /// <summary>
        /// Current ocean scale (changes with viewer altitude).
        /// </summary>
        public float Scale { get; private set; }
        public float CalcLodScale(float lodIndex) { return Scale * Mathf.Pow(2f, lodIndex); }
        public float CalcGridSize(int lodIndex) { return CalcLodScale(lodIndex) / LodDataResolution; }

        /// <summary>
        /// The ocean changes scale when viewer changes altitude, this gives the interpolation param between scales.
        /// </summary>
        public float ViewerAltitudeLevelAlpha { get; private set; }

        /// <summary>
        /// Sea level is given by y coordinate of GameObject with OceanRenderer script.
        /// </summary>
        public float SeaLevel { get { return Root.position.y; } }

        [HideInInspector] public LodTransform _lodTransform;
        [HideInInspector] public LodDataMgrAnimWaves _lodDataAnimWaves;
        [HideInInspector] public LodDataMgrSeaFloorDepth _lodDataSeaDepths;
        [HideInInspector] public LodDataMgrClipSurface _lodDataClipSurface;
        [HideInInspector] public LodDataMgrDynWaves _lodDataDynWaves;
        [HideInInspector] public LodDataMgrFlow _lodDataFlow;
        [HideInInspector] public LodDataMgrFoam _lodDataFoam;
        [HideInInspector] public LodDataMgrShadow _lodDataShadow;

        /// <summary>
        /// The number of LODs/scales that the ocean is currently using.
        /// </summary>
        public int CurrentLodCount { get { return _lodTransform != null ? _lodTransform.LodCount : 0; } }

        /// <summary>
        /// Vertical offset of camera vs water surface.
        /// </summary>
        public float ViewerHeightAboveWater { get; private set; }

        List<LodDataMgr> _lodDatas = new List<LodDataMgr>();

        List<OceanChunkRenderer> _oceanChunkRenderers = new List<OceanChunkRenderer>();
        public List<OceanChunkRenderer> Tiles => _oceanChunkRenderers;

        SampleHeightHelper _sampleHeightHelper = new SampleHeightHelper();

        public static OceanRenderer Instance { get; private set; }

        // A hash of the settings used to generate the ocean, used to regenerate when necessary
        int _generatedSettingsHash = 0;

        /// <summary>
        /// Is runtime environment without graphics card
        /// </summary>
        public static bool RunningWithoutGPU
        {
            get
            {
                var noGPU = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;
                var emulateNoGPU = (Instance != null ? Instance._forceNoGPU : false);
                return noGPU || emulateNoGPU;
            }
        }

        /// <summary>
        /// Is runtime environment without graphics card
        /// </summary>
        public static bool RunningHeadless => Application.isBatchMode || (Instance != null ? Instance._forceBatchMode : false);

        // We are computing these values to be optimal based on the base mesh vertex density.
        float _lodAlphaBlackPointFade;
        float _lodAlphaBlackPointWhitePointFade;

        bool _canSkipCulling = false;

        public static int sp_crestTime = Shader.PropertyToID("_CrestTime");
        readonly int sp_texelsPerWave = Shader.PropertyToID("_TexelsPerWave");
        readonly int sp_oceanCenterPosWorld = Shader.PropertyToID("_OceanCenterPosWorld");
        readonly int sp_meshScaleLerp = Shader.PropertyToID("_MeshScaleLerp");
        readonly int sp_sliceCount = Shader.PropertyToID("_SliceCount");
        readonly int sp_clipByDefault = Shader.PropertyToID("_CrestClipByDefault");
        readonly int sp_lodAlphaBlackPointFade = Shader.PropertyToID("_CrestLodAlphaBlackPointFade");
        readonly int sp_lodAlphaBlackPointWhitePointFade = Shader.PropertyToID("_CrestLodAlphaBlackPointWhitePointFade");
        static int sp_ForceUnderwater = Shader.PropertyToID("_ForceUnderwater");
        public static int sp_perCascadeInstanceData = Shader.PropertyToID("_CrestPerCascadeInstanceData");
        public static int sp_cascadeData = Shader.PropertyToID("_CrestCascadeData");

#if UNITY_EDITOR
        static float _lastUpdateEditorTime = -1f;
        public static float LastUpdateEditorTime => _lastUpdateEditorTime;
        static int _editorFrames = 0;
#endif

        BuildCommandBuffer _commandbufferBuilder;

        // This must exactly match struct with same name in HLSL
        // :CascadeParams
        public struct CascadeParams
        {
            public Vector2 _posSnapped;
            public float _scale;

            public float _textureRes;
            public float _oneOverTextureRes;

            public float _texelWidth;

            public float _weight;

            public float _maxWavelength;
        }
        public ComputeBuffer _bufCascadeDataTgt;
        public ComputeBuffer _bufCascadeDataSrc;

        // This must exactly match struct with same name in HLSL
        // :PerCascadeInstanceData
        public struct PerCascadeInstanceData
        {
            public float _meshScaleLerp;
            public float _farNormalsWeight;
            public float _geoGridWidth;
            public Vector2 _normalScrollSpeeds;

            // Align to 32 bytes
            public Vector3 __padding;
        }
        public ComputeBuffer _bufPerCascadeInstanceData;

        CascadeParams[] _cascadeParamsSrc = new CascadeParams[LodDataMgr.MAX_LOD_COUNT + 1];
        CascadeParams[] _cascadeParamsTgt = new CascadeParams[LodDataMgr.MAX_LOD_COUNT + 1];

        PerCascadeInstanceData[] _perCascadeInstanceData = new PerCascadeInstanceData[LodDataMgr.MAX_LOD_COUNT];

        // When leaving the last prefab stage, OnDisabled will be called but GetCurrentPrefabStage will return nothing
        // which will fail the prefab check and disable the OceanRenderer in the scene. We need to track it ourselves.
#pragma warning disable 414
        static bool s_IsPrefabStage = false;
#pragma warning restore 414

        // Drive state from OnEnable and OnDisable? OnEnable on RegisterLodDataInput seems to get called on script reload
        void OnEnable()
        {
            // We don't run in "prefab scenes", i.e. when editing a prefab. Bail out if prefab scene is detected.
#if UNITY_EDITOR
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                s_IsPrefabStage = true;
                return;
            }
#endif

            // Setup a default time provider, and add the override one (from the inspector)
            _timeProviderStack.Clear();

            // Put a base TP that should always be available as a fallback
            PushTimeProvider(new TimeProviderDefault());

            // Add the TP from the inspector
            if (_timeProvider != null)
            {
                PushTimeProvider(_timeProvider);
            }

            if (!_primaryLight && _searchForPrimaryLightOnStartup)
            {
                _primaryLight = RenderSettings.sun;
            }

            if (!VerifyRequirements())
            {
                enabled = false;
                return;
            }

#if UNITY_EDITOR
            if (EditorApplication.isPlaying && !Validate(this, ValidatedHelper.DebugLog))
            {
                enabled = false;
                return;
            }
#endif

            Instance = this;
            Scale = Mathf.Clamp(Scale, _minScale, _maxScale);

            _bufPerCascadeInstanceData = new ComputeBuffer(_perCascadeInstanceData.Length, UnsafeUtility.SizeOf<PerCascadeInstanceData>());
            Shader.SetGlobalBuffer("_CrestPerCascadeInstanceData", _bufPerCascadeInstanceData);

            _bufCascadeDataTgt = new ComputeBuffer(_cascadeParamsTgt.Length, UnsafeUtility.SizeOf<CascadeParams>());
            Shader.SetGlobalBuffer(sp_cascadeData, _bufCascadeDataTgt);

            // Not used by graphics shaders, so not set globally (global does not work for compute)
            _bufCascadeDataSrc = new ComputeBuffer(_cascadeParamsSrc.Length, UnsafeUtility.SizeOf<CascadeParams>());

            _lodTransform = new LodTransform();
            _lodTransform.InitLODData(_lodCount);

            // Resolution is 4 tiles across.
            var baseMeshDensity = _lodDataResolution * 0.25f / _geometryDownSampleFactor;
            // 0.4f is the "best" value when base mesh density is 8. Scaling down from there produces results similar to
            // hand crafted values which looked good when the ocean is flat.
            _lodAlphaBlackPointFade = 0.4f / (baseMeshDensity / 8f);
            // We could calculate this in the shader, but we can save two subtractions this way.
            _lodAlphaBlackPointWhitePointFade = 1f - _lodAlphaBlackPointFade - _lodAlphaBlackPointFade;

            Root = OceanBuilder.GenerateMesh(this, _oceanChunkRenderers, _lodDataResolution, _geometryDownSampleFactor, _lodCount);

            // Make sure we have correct defaults in case simulations are not enabled.
            LodDataMgrClipSurface.BindNullToGraphicsShaders();
            LodDataMgrDynWaves.BindNullToGraphicsShaders();
            LodDataMgrFlow.BindNullToGraphicsShaders();
            LodDataMgrFoam.BindNullToGraphicsShaders();
            LodDataMgrSeaFloorDepth.BindNullToGraphicsShaders();
            LodDataMgrShadow.BindNullToGraphicsShaders();

            CreateDestroySubSystems();

            _commandbufferBuilder = new BuildCommandBuffer();

            ValidateViewpoint();

            if (_attachDebugGUI && GetComponent<OceanDebugGUI>() == null)
            {
                gameObject.AddComponent<OceanDebugGUI>().hideFlags = HideFlags.DontSave;
            }

#if UNITY_EDITOR
            EditorApplication.update -= EditorUpdate;
            EditorApplication.update += EditorUpdate;
#endif
            foreach (var lodData in _lodDatas)
            {
                lodData.OnEnable();
            }

            _canSkipCulling = false;

            _generatedSettingsHash = CalculateSettingsHash();
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            // We don't run in "prefab scenes", i.e. when editing a prefab. Bail out if prefab scene is detected.
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                // We have just left a prefab scene on the stack and are now in another prefab scene.
                return;
            }
            else if (s_IsPrefabStage)
            {
                // We have left the last prefab scene and are now back to a normal scene. We do not want to disable the
                // OceanRenderer.
                s_IsPrefabStage = false;
                return;
            }
#endif

            CleanUp();

            Instance = null;
        }

#if UNITY_EDITOR
        static void EditorUpdate()
        {
            if (Instance == null) return;

            if (!EditorApplication.isPlaying)
            {
                if (EditorApplication.timeSinceStartup - _lastUpdateEditorTime > 1f / Mathf.Clamp(Instance._editModeFPS, 0.01f, 60f))
                {
                    _editorFrames++;

                    _lastUpdateEditorTime = (float)EditorApplication.timeSinceStartup;

                    Instance.RunUpdate();
                }
            }
        }
#endif

        public static int FrameCount
        {
            get
            {
#if UNITY_EDITOR
                if (!EditorApplication.isPlaying)
                {
                    return _editorFrames;
                }
                else
#endif
                {
                    return Time.frameCount;
                }
            }
        }

        void CreateDestroySubSystems()
        {
            if (!RunningWithoutGPU)
            {
                {
                    if (_lodDataAnimWaves == null)
                    {
                        _lodDataAnimWaves = new LodDataMgrAnimWaves(this);
                        _lodDatas.Add(_lodDataAnimWaves);
                    }
                }

                if (CreateClipSurfaceData && !RunningHeadless)
                {
                    if (_lodDataClipSurface == null)
                    {
                        _lodDataClipSurface = new LodDataMgrClipSurface(this);
                        _lodDatas.Add(_lodDataClipSurface);
                    }
                }
                else
                {
                    if (_lodDataClipSurface != null)
                    {
                        _lodDataClipSurface.OnDisable();
                        _lodDatas.Remove(_lodDataClipSurface);
                        _lodDataClipSurface = null;
                    }
                }

                if (CreateDynamicWaveSim)
                {
                    if (_lodDataDynWaves == null)
                    {
                        _lodDataDynWaves = new LodDataMgrDynWaves(this);
                        _lodDatas.Add(_lodDataDynWaves);
                    }
                }
                else
                {
                    if (_lodDataDynWaves != null)
                    {
                        _lodDataDynWaves.OnDisable();
                        _lodDatas.Remove(_lodDataDynWaves);
                        _lodDataDynWaves = null;
                    }
                }

                if (CreateFlowSim)
                {
                    if (_lodDataFlow == null)
                    {
                        _lodDataFlow = new LodDataMgrFlow(this);
                        _lodDatas.Add(_lodDataFlow);
                    }

                    if (FlowProvider != null && !(FlowProvider is QueryFlow))
                    {
                        FlowProvider.CleanUp();
                        FlowProvider = null;
                    }
                }
                else
                {
                    if (_lodDataFlow != null)
                    {
                        _lodDataFlow.OnDisable();
                        _lodDatas.Remove(_lodDataFlow);
                        _lodDataFlow = null;
                    }

                    if (FlowProvider != null && FlowProvider is QueryFlow)
                    {
                        FlowProvider.CleanUp();
                        FlowProvider = null;
                    }
                }
                if (FlowProvider == null)
                {
                    FlowProvider = _lodDataAnimWaves.Settings.CreateFlowProvider(this);
                }

                if (CreateFoamSim && !RunningHeadless)
                {
                    if (_lodDataFoam == null)
                    {
                        _lodDataFoam = new LodDataMgrFoam(this);
                        _lodDatas.Add(_lodDataFoam);
                    }
                }
                else
                {
                    if (_lodDataFoam != null)
                    {
                        _lodDataFoam.OnDisable();
                        _lodDatas.Remove(_lodDataFoam);
                        _lodDataFoam = null;
                    }
                }

                if (CreateSeaFloorDepthData)
                {
                    if (_lodDataSeaDepths == null)
                    {
                        _lodDataSeaDepths = new LodDataMgrSeaFloorDepth(this);
                        _lodDatas.Add(_lodDataSeaDepths);
                    }
                }
                else
                {
                    if (_lodDataSeaDepths != null)
                    {
                        _lodDataSeaDepths.OnDisable();
                        _lodDatas.Remove(_lodDataSeaDepths);
                        _lodDataSeaDepths = null;
                    }
                }

                if (CreateShadowData && !RunningHeadless)
                {
                    if (_lodDataShadow == null)
                    {
                        _lodDataShadow = new LodDataMgrShadow(this);
                        _lodDatas.Add(_lodDataShadow);
                    }
                }
                else
                {
                    if (_lodDataShadow != null)
                    {
                        _lodDataShadow.OnDisable();
                        _lodDatas.Remove(_lodDataShadow);
                        _lodDataShadow = null;
                    }
                }
            }

            // Potential extension - add 'type' field to collprovider and change provider if settings have changed - this would support runtime changes.
            if (CollisionProvider == null)
            {
                var settings = _lodDataAnimWaves != null ? _lodDataAnimWaves.Settings : _simSettingsAnimatedWaves;

                if (settings != null)
                {
                    CollisionProvider = settings.CreateCollisionProvider();
                }
            }
        }

        bool VerifyRequirements()
        {
#if UNITY_EDITOR
            // If running a build, don't assert any requirements at all. Requirements are for
            // the runtime, not for making builds.
            if (BuildPipeline.isBuildingPlayer)
            {
                return true;
            }
#endif

            if (!RunningWithoutGPU)
            {
                if (Application.platform == RuntimePlatform.WebGLPlayer)
                {
                    Debug.LogError("Crest does not support WebGL backends.", this);
                    return false;
                }
#if UNITY_EDITOR
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES2 ||
                    SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3 ||
                    SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore)
                {
                    Debug.LogError("Crest does not support OpenGL backends.", this);
                    return false;
                }
#endif
                if (SystemInfo.graphicsShaderLevel < 45)
                {
                    Debug.LogError("Crest requires graphics devices that support shader level 4.5 or above.", this);
                    return false;
                }
                if (!SystemInfo.supportsComputeShaders)
                {
                    Debug.LogError("Crest requires graphics devices that support compute shaders.", this);
                    return false;
                }
                if (!SystemInfo.supports2DArrayTextures)
                {
                    Debug.LogError("Crest requires graphics devices that support 2D array textures.", this);
                    return false;
                }
            }

            return true;
        }

        void ValidateViewpoint()
        {
            if (Viewpoint == null)
            {
                Debug.LogError("Crest needs to know where to focus the ocean detail. Please set the <i>ViewCamera</i> or the <i>Viewpoint</i> property that will render the ocean, or tag the primary camera as <i>MainCamera</i>.", this);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void InitStatics()
        {
            // Init here from 2019.3 onwards
            Instance = null;

            sp_ForceUnderwater = Shader.PropertyToID("_ForceUnderwater");
            sp_perCascadeInstanceData = Shader.PropertyToID("_CrestPerCascadeInstanceData");
            sp_cascadeData = Shader.PropertyToID("_CrestCascadeData");
            sp_crestTime = Shader.PropertyToID("_CrestTime");
        }

        void LateUpdate()
        {
#if UNITY_EDITOR
            // Don't run immediately if in edit mode - need to count editor frames so this is run through EditorUpdate()
            if (!EditorApplication.isPlaying)
            {
                return;
            }
#endif

            RunUpdate();
        }

        int CalculateSettingsHash()
        {
            var settingsHash = Hashy.CreateHash();

            // Add all the settings that require rebuilding..
            Hashy.AddInt(_layer, ref settingsHash);
            Hashy.AddInt(_lodDataResolution, ref settingsHash);
            Hashy.AddInt(_geometryDownSampleFactor, ref settingsHash);
            Hashy.AddInt(_lodCount, ref settingsHash);
            Hashy.AddBool(_forceBatchMode, ref settingsHash);
            Hashy.AddBool(_forceNoGPU, ref settingsHash);
            Hashy.AddBool(_hideOceanTileGameObjects, ref settingsHash);

#pragma warning disable 0618
            Hashy.AddObject(_layerName, ref settingsHash);
#pragma warning restore 0618

            // Also include anything from the simulation settings for rebuilding.
            foreach (var lod in _lodDatas)
            {
                // Null means it does not support settings.
                if (lod.SettingsBase != null)
                {
                    lod.SettingsBase.AddToSettingsHash(ref settingsHash);
                }
            }

            return settingsHash;
        }

        void RunUpdate()
        {
            // Rebuild if needed. Editor-only i suppose?
#if UNITY_EDITOR
            if (CalculateSettingsHash() != _generatedSettingsHash)
            {
                enabled = false;
                enabled = true;
            }
#endif

            // Run queries *before* changing the ocean position, as it needs the current LOD positions to associate with the current queries
#if UNITY_EDITOR
            // Issue #630 - seems to be a terrible memory leak coming from creating async gpu readbacks. We don't rely on queries in edit mode AFAIK
            // so knock this out.
            if (EditorApplication.isPlaying)
#endif
            {
                CollisionProvider?.UpdateQueries();
                FlowProvider?.UpdateQueries();
            }

            // set global shader params
            Shader.SetGlobalFloat(sp_texelsPerWave, MinTexelsPerWave);
            Shader.SetGlobalFloat(sp_crestTime, CurrentTime);
            Shader.SetGlobalFloat(sp_sliceCount, CurrentLodCount);
            Shader.SetGlobalFloat(sp_clipByDefault, _defaultClippingState == DefaultClippingState.EverythingClipped ? 1f : 0f);
            Shader.SetGlobalFloat(sp_lodAlphaBlackPointFade, _lodAlphaBlackPointFade);
            Shader.SetGlobalFloat(sp_lodAlphaBlackPointWhitePointFade, _lodAlphaBlackPointWhitePointFade);

            // LOD 0 is blended in/out when scale changes, to eliminate pops. Here we set it as a global, whereas in OceanChunkRenderer it
            // is applied to LOD0 tiles only through instance data. This global can be used in compute, where we only apply this factor for slice 0.
            var needToBlendOutShape = ScaleCouldIncrease;
            var meshScaleLerp = needToBlendOutShape ? ViewerAltitudeLevelAlpha : 0f;
            Shader.SetGlobalFloat(sp_meshScaleLerp, meshScaleLerp);

            ValidateViewpoint();

            if (_followViewpoint && Viewpoint != null)
            {
                LateUpdatePosition();
                LateUpdateScale();
                LateUpdateViewerHeight();
            }

            CreateDestroySubSystems();

            LateUpdateLods();

            if (Viewpoint != null)
            {
                LateUpdateTiles();
            }

            LateUpdateResetMaxDisplacementFromShape();

            WritePerFrameMaterialParams();

#if UNITY_EDITOR
            if (EditorApplication.isPlaying || !_showOceanProxyPlane)
#endif
            {
                _commandbufferBuilder.BuildAndExecute();
            }
#if UNITY_EDITOR
            else
            {
                // If we're not running, reset the frame data to avoid validation warnings
                for (int i = 0; i < _lodTransform._renderData.Length; i++)
                {
                    _lodTransform._renderData[i]._frame = -1;
                }
                for (int i = 0; i < _lodTransform._renderDataSource.Length; i++)
                {
                    _lodTransform._renderDataSource[i]._frame = -1;
                }
            }
#endif
        }

        void WritePerFrameMaterialParams()
        {
            if (OceanMaterial != null)
            {
                // Hack - due to SV_IsFrontFace occasionally coming through as true for back faces,
                // add a param here that forces ocean to be in underwater state. I think the root
                // cause here might be imprecision or numerical issues at ocean tile boundaries, although
                // i'm not sure why cracks are not visible in this case.
                OceanMaterial.SetFloat(sp_ForceUnderwater, ViewerHeightAboveWater < -2f ? 1f : 0f);
            }

            _lodTransform.WriteCascadeParams(_cascadeParamsTgt, _cascadeParamsSrc);
            _bufCascadeDataTgt.SetData(_cascadeParamsTgt);
            _bufCascadeDataSrc.SetData(_cascadeParamsSrc);

            WritePerCascadeInstanceData(_perCascadeInstanceData);
            _bufPerCascadeInstanceData.SetData(_perCascadeInstanceData);
        }

        void WritePerCascadeInstanceData(PerCascadeInstanceData[] instanceData)
        {
            for (int lodIdx = 0; lodIdx < CurrentLodCount; lodIdx++)
            {
                // blend LOD 0 shape in/out to avoid pop, if the ocean might scale up later (it is smaller than its maximum scale)
                var needToBlendOutShape = lodIdx == 0 && ScaleCouldIncrease;
                instanceData[lodIdx]._meshScaleLerp = needToBlendOutShape ? ViewerAltitudeLevelAlpha : 0f;

                // blend furthest normals scale in/out to avoid pop, if scale could reduce
                var needToBlendOutNormals = lodIdx == CurrentLodCount - 1 && ScaleCouldDecrease;
                instanceData[lodIdx]._farNormalsWeight = needToBlendOutNormals ? ViewerAltitudeLevelAlpha : 1f;

                // geometry data
                // compute grid size of geometry. take the long way to get there - make sure we land exactly on a power of two
                // and not inherit any of the lossy-ness from lossyScale.
                var scale_pow_2 = CalcLodScale(lodIdx);
                instanceData[lodIdx]._geoGridWidth = scale_pow_2 / (0.25f * _lodDataResolution / _geometryDownSampleFactor);

                var mul = 1.875f; // fudge 1
                var pow = 1.4f; // fudge 2
                var texelWidth = instanceData[lodIdx]._geoGridWidth / _geometryDownSampleFactor;
                instanceData[lodIdx]._normalScrollSpeeds[0] = Mathf.Pow(Mathf.Log(1f + 2f * texelWidth) * mul, pow);
                instanceData[lodIdx]._normalScrollSpeeds[1] = Mathf.Pow(Mathf.Log(1f + 4f * texelWidth) * mul, pow);
            }
        }

        void LateUpdatePosition()
        {
            Vector3 pos = Viewpoint.position;

            // maintain y coordinate - sea level
            pos.y = Root.position.y;

            // Don't land very close to regular positions where things are likely to snap to, because different tiles might
            // land on either side of a snap boundary due to numerical error and snap to the wrong positions. Nudge away from
            // common by using increments of 1/60 which have lots of factors.
            // :OceanGridPrecisionErrors
            if (Mathf.Abs(pos.x * 60f - Mathf.Round(pos.x * 60f)) < 0.001f)
            {
                pos.x += 0.002f;
            }
            if (Mathf.Abs(pos.z * 60f - Mathf.Round(pos.z * 60f)) < 0.001f)
            {
                pos.z += 0.002f;
            }

            Root.position = pos;

            Shader.SetGlobalVector(sp_oceanCenterPosWorld, Root.position);
        }

        void LateUpdateScale()
        {
            // reach maximum detail at slightly below sea level. this should combat cases where visual range can be lost
            // when water height is low and camera is suspended in air. i tried a scheme where it was based on difference
            // to water height but this does help with the problem of horizontal range getting limited at bad times.
            float maxDetailY = SeaLevel - _maxVertDispFromWaves * _dropDetailHeightBasedOnWaves;
            float camDistance = Mathf.Abs(Viewpoint.position.y - maxDetailY);

            // offset level of detail to keep max detail in a band near the surface
            camDistance = Mathf.Max(camDistance - 4f, 0f);

            // scale ocean mesh based on camera distance to sea level, to keep uniform detail.
            const float HEIGHT_LOD_MUL = 1f;
            float level = camDistance * HEIGHT_LOD_MUL;
            level = Mathf.Max(level, _minScale);
            if (_maxScale != -1f) level = Mathf.Min(level, 1.99f * _maxScale);

            float l2 = Mathf.Log(level) / Mathf.Log(2f);
            float l2f = Mathf.Floor(l2);

            ViewerAltitudeLevelAlpha = l2 - l2f;

            Scale = Mathf.Pow(2f, l2f);
            Root.localScale = new Vector3(Scale, 1f, Scale);
        }

        void LateUpdateViewerHeight()
        {
            var camera = ViewCamera;

            _sampleHeightHelper.Init(camera.transform.position, 0f, true);

            _sampleHeightHelper.Sample(out var waterHeight);

            ViewerHeightAboveWater = camera.transform.position.y - waterHeight;
        }

        void LateUpdateLods()
        {
            // Do any per-frame update for each LOD type.

            _lodTransform.UpdateTransforms();

            _lodDataAnimWaves?.UpdateLodData();
            _lodDataClipSurface?.UpdateLodData();
            _lodDataDynWaves?.UpdateLodData();
            _lodDataFlow?.UpdateLodData();
            _lodDataFoam?.UpdateLodData();
            _lodDataSeaDepths?.UpdateLodData();
            _lodDataShadow?.UpdateLodData();
        }

        void LateUpdateTiles()
        {
            var isUnderwaterActive = UnderwaterRenderer.Instance != null && UnderwaterRenderer.Instance.IsActive;

            var definitelyUnderwater = false;
            var volumeExtinctionLength = 0f;

            if (isUnderwaterActive)
            {
                definitelyUnderwater = ViewerHeightAboveWater < -5f;
                var density = _material.GetVector("_DepthFogDensity");
                var minimumFogDensity = Mathf.Min(Mathf.Min(density.x, density.y), density.z);
                var underwaterCullLimit = Mathf.Clamp(_underwaterCullLimit, UNDERWATER_CULL_LIMIT_MINIMUM, UNDERWATER_CULL_LIMIT_MAXIMUM);
                volumeExtinctionLength = -Mathf.Log(underwaterCullLimit) / minimumFogDensity;
            }

            var canSkipCulling = WaterBody.WaterBodies.Count == 0 && _canSkipCulling;

            foreach (OceanChunkRenderer tile in _oceanChunkRenderers)
            {
                if (tile.Rend == null)
                {
                    continue;
                }

                var isCulled = false;

                // If there are local bodies of water, this will do overlap tests between the ocean tiles
                // and the water bodies and turn off any that don't overlap.
                if (!canSkipCulling)
                {
                    var chunkBounds = tile.Rend.bounds;

                    var overlappingOne = false;
                    foreach (var body in WaterBody.WaterBodies)
                    {
                        var bounds = body.AABB;

                        bool overlapping =
                            bounds.max.x > chunkBounds.min.x && bounds.min.x < chunkBounds.max.x &&
                            bounds.max.z > chunkBounds.min.z && bounds.min.z < chunkBounds.max.z;
                        if (overlapping)
                        {
                            overlappingOne = true;
                            break;
                        }
                    }

                    isCulled = !overlappingOne && WaterBody.WaterBodies.Count > 0;
                }

                // Cull tiles the viewer cannot see through the underwater fog.
                if (!isCulled && isUnderwaterActive)
                {
                    isCulled = definitelyUnderwater && (Viewpoint.position - tile.Rend.bounds.ClosestPoint(Viewpoint.position)).magnitude >= volumeExtinctionLength;
                }

                tile.Rend.enabled = !isCulled;
            }

            // Can skip culling next time around if water body count stays at 0
            _canSkipCulling = WaterBody.WaterBodies.Count == 0;
        }

        void LateUpdateResetMaxDisplacementFromShape()
        {
            if (FrameCount != _maxDisplacementCachedTime)
            {
                _maxHorizDispFromShape = _maxVertDispFromShape = _maxVertDispFromWaves = 0f;
            }

            _maxDisplacementCachedTime = FrameCount;
        }

        /// <summary>
        /// Could the ocean horizontal scale increase (for e.g. if the viewpoint gains altitude). Will be false if ocean already at maximum scale.
        /// </summary>
        public bool ScaleCouldIncrease { get { return _maxScale == -1f || Root.localScale.x < _maxScale * 0.99f; } }
        /// <summary>
        /// Could the ocean horizontal scale decrease (for e.g. if the viewpoint drops in altitude). Will be false if ocean already at minimum scale.
        /// </summary>
        public bool ScaleCouldDecrease { get { return _minScale == -1f || Root.localScale.x > _minScale * 1.01f; } }

        /// <summary>
        /// User shape inputs can report in how far they might displace the shape horizontally and vertically. The max value is
        /// saved here. Later the bounding boxes for the ocean tiles will be expanded to account for this potential displacement.
        /// </summary>
        public void ReportMaxDisplacementFromShape(float maxHorizDisp, float maxVertDisp, float maxVertDispFromWaves)
        {
            _maxHorizDispFromShape += maxHorizDisp;
            _maxVertDispFromShape += maxVertDisp;
            _maxVertDispFromWaves += maxVertDispFromWaves;
        }
        float _maxHorizDispFromShape = 0f;
        float _maxVertDispFromShape = 0f;
        float _maxVertDispFromWaves = 0f;
        int _maxDisplacementCachedTime = 0;
        /// <summary>
        /// The maximum horizontal distance that the shape scripts are displacing the shape.
        /// </summary>
        public float MaxHorizDisplacement { get { return _maxHorizDispFromShape; } }
        /// <summary>
        /// The maximum height that the shape scripts are displacing the shape.
        /// </summary>
        public float MaxVertDisplacement { get { return _maxVertDispFromShape; } }

        /// <summary>
        /// Provides ocean shape to CPU.
        /// </summary>
        public ICollProvider CollisionProvider { get; private set; }
        public IFlowProvider FlowProvider { get; private set; }

        private void CleanUp()
        {
            foreach (var lodData in _lodDatas)
            {
                lodData.OnDisable();
            }
            _lodDatas.Clear();

#if UNITY_EDITOR
            if (!EditorApplication.isPlaying && Root != null)
            {
                DestroyImmediate(Root.gameObject);
            }
            else
#endif
            if (Root != null)
            {
                Destroy(Root.gameObject);
            }

            Root = null;

            _lodTransform = null;
            _lodDataAnimWaves = null;
            _lodDataClipSurface = null;
            _lodDataDynWaves = null;
            _lodDataFlow = null;
            _lodDataFoam = null;
            _lodDataSeaDepths = null;
            _lodDataShadow = null;

            if (CollisionProvider != null)
            {
                CollisionProvider.CleanUp();
                CollisionProvider = null;
            }

            if (FlowProvider != null)
            {
                FlowProvider.CleanUp();
                FlowProvider = null;
            }

            _oceanChunkRenderers.Clear();

            _bufPerCascadeInstanceData?.Dispose();
            _bufCascadeDataTgt?.Dispose();
            _bufCascadeDataSrc?.Dispose();
        }

#if UNITY_EDITOR
        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnReLoadScripts()
        {
            Instance = FindObjectOfType<OceanRenderer>();
        }

        private void OnDrawGizmos()
        {
            // Don't need proxy if in play mode
            if (EditorApplication.isPlaying)
            {
                return;
            }

            // Create proxy if not present already, and proxy enabled
            if (_proxyPlane == null && _showOceanProxyPlane)
            {
                _proxyPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
                DestroyImmediate(_proxyPlane.GetComponent<Collider>());
                _proxyPlane.hideFlags = HideFlags.HideAndDontSave;
                _proxyPlane.transform.parent = transform;
                _proxyPlane.transform.localPosition = Vector3.zero;
                _proxyPlane.transform.localRotation = Quaternion.identity;
                _proxyPlane.transform.localScale = 4000f * Vector3.one;

                _proxyPlane.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find(kProxyShader));
            }

            // Change active state of proxy if necessary
            if (_proxyPlane != null && _proxyPlane.activeSelf != _showOceanProxyPlane)
            {
                _proxyPlane.SetActive(_showOceanProxyPlane);

                // Scene view doesnt automatically refresh which makes the option confusing, so force it
                EditorWindow view = EditorWindow.GetWindow<SceneView>();
                view.Repaint();
            }

            if (Root != null)
            {
                Root.gameObject.SetActive(!_showOceanProxyPlane);
            }
        }
#endif
    }

#if UNITY_EDITOR
    public partial class OceanRenderer : IValidated
    {
        public static void RunValidation(OceanRenderer ocean)
        {
            ocean.Validate(ocean, ValidatedHelper.DebugLog);

            // ShapeGerstnerBatched
            var gerstners = FindObjectsOfType<ShapeGerstnerBatched>();
            foreach (var gerstner in gerstners)
            {
                gerstner.Validate(ocean, ValidatedHelper.DebugLog);
            }

            // ShapeGerstner
            foreach (var component in FindObjectsOfType<ShapeGerstner>())
            {
                component.Validate(ocean, ValidatedHelper.DebugLog);
            }

            // ShapeFFT
            foreach (var component in FindObjectsOfType<ShapeFFT>())
            {
                component.Validate(ocean, ValidatedHelper.DebugLog);
            }

#pragma warning disable 0618
            // UnderwaterEffect
            var underwaters = FindObjectsOfType<UnderwaterEffect>();
            foreach (var underwater in underwaters)
            {
                underwater.Validate(ocean, ValidatedHelper.DebugLog);
            }
#pragma warning restore 0618

            // OceanDepthCache
            var depthCaches = FindObjectsOfType<OceanDepthCache>();
            foreach (var depthCache in depthCaches)
            {
                depthCache.Validate(ocean, ValidatedHelper.DebugLog);
            }

            // FloatingObjectBase
            var floatingObjects = FindObjectsOfType<FloatingObjectBase>();
            foreach (var floatingObject in floatingObjects)
            {
                floatingObject.Validate(ocean, ValidatedHelper.DebugLog);
            }

            // Inputs
            var inputs = FindObjectsOfType<RegisterLodDataInputBase>();
            foreach (var input in inputs)
            {
                input.Validate(ocean, ValidatedHelper.DebugLog);
            }

            // WaterBody
            var waterBodies = FindObjectsOfType<WaterBody>();
            foreach (var waterBody in waterBodies)
            {
                waterBody.Validate(ocean, ValidatedHelper.DebugLog);
            }

            Debug.Log("Validation complete!", ocean);
        }

        public bool Validate(OceanRenderer ocean, ValidatedHelper.ShowMessage showMessage)
        {
            var isValid = true;

            isValid = ValidateObsolete(ocean, showMessage);

            if (_material == null)
            {
                showMessage
                (
                    "No ocean material specified.",
                    "Assign a valid ocean material to the Material property of the <i>OceanRenderer</i> component.",
                    ValidatedHelper.MessageType.Error, ocean
                );

                isValid = false;
            }

            // OceanRenderer
            if (FindObjectsOfType<OceanRenderer>().Length > 1)
            {
                showMessage
                (
                    "Multiple <i>OceanRenderer</i> components detected in open scenes, this is not typical - usually only one <i>OceanRenderer</i> is expected to be present.",
                    "Remove extra <i>OceanRenderer</i> components.",
                    ValidatedHelper.MessageType.Warning, ocean
                );
            }

            // ShapeGerstnerBatched
            var gerstnerBatches = FindObjectsOfType<ShapeGerstnerBatched>();
            var gerstners = FindObjectsOfType<ShapeGerstner>();
            var ffts = FindObjectsOfType<ShapeFFT>();
            if (gerstnerBatches.Length == 0 && gerstners.Length == 0 && ffts.Length == 0)
            {
                showMessage
                (
                    "No ShapeGerstnerBatched component found, so ocean will appear flat (no waves).",
                    "Assign a ShapeGerstnerBatched component to a GameObject.",
                    ValidatedHelper.MessageType.Info, ocean
                );
            }

            // Ocean Detail Parameters
            var baseMeshDensity = _lodDataResolution * 0.25f / _geometryDownSampleFactor;

            if (baseMeshDensity < 8)
            {
                showMessage
                (
                    "Base mesh density is lower than 8. There will be visible gaps in the ocean surface.",
                    "Increase the <i>LOD Data Resolution</i> or decrease the <i>Geometry Down Sample Factor</i>.",
                    ValidatedHelper.MessageType.Error, ocean
                );
            }
            else if (baseMeshDensity < 16)
            {
                showMessage
                (
                    "Base mesh density is lower than 16. There will be visible transitions when traversing the ocean surface. ",
                    "Increase the <i>LOD Data Resolution</i> or decrease the <i>Geometry Down Sample Factor</i>.",
                    ValidatedHelper.MessageType.Warning, ocean
                );
            }

            var hasMaterial = ocean != null && ocean._material != null;
            var oceanColourIncorrectText = "Ocean colour will be incorrect. ";

            // Check lighting. There is an edge case where the lighting data is invalid because settings has changed.
            // We don't need to check anything if the following material options are used.
            if (hasMaterial && !ocean._material.IsKeywordEnabled("_PROCEDURALSKY_ON") &&
                !ocean._material.IsKeywordEnabled("_OVERRIDEREFLECTIONCUBEMAP_ON"))
            {
                var alternativesText = "Alternatively, try the <i>Procedural Sky</i> or <i>Override Reflection " +
                    "Cubemap</i> option on the ocean material.";

                if (RenderSettings.defaultReflectionMode == DefaultReflectionMode.Skybox)
                {
                    var isLightingDataMissing = Lightmapping.giWorkflowMode != Lightmapping.GIWorkflowMode.Iterative &&
                        !Lightmapping.lightingDataAsset;

                    // Generated lighting will be wrong without a skybox.
                    if (RenderSettings.skybox == null)
                    {
                        showMessage
                        (
                            "There is no skybox set in the Lighting window. " + oceanColourIncorrectText,
                            "Configure a valid skybox. " + alternativesText,
                            ValidatedHelper.MessageType.Warning, ocean
                        );
                    }
                    // Spherical Harmonics is missing and required.
                    else if (isLightingDataMissing)
                    {
                        showMessage
                        (
                            "Lighting data is missing which provides baked spherical harmonics." + oceanColourIncorrectText,
                            "Generate lighting or enable Auto Generate from the Lighting window. " + alternativesText,
                            ValidatedHelper.MessageType.Warning, ocean
                        );
                    }
                }
                else
                {
                    // We need a cubemap if using custom reflections.
                    if (RenderSettings.customReflection == null)
                    {
                        showMessage
                        (
                            "Environmental Reflections is set to Custom, but no cubemap has been provided. " + oceanColourIncorrectText,
                            "Assign a cubemap in the Lighting window. " + alternativesText,
                            ValidatedHelper.MessageType.Warning, ocean
                        );
                    }
                }
            }
            // Check override reflections cubemap option. Procedural skybox will override this, but it is a waste to
            // have the keyword enabled and not use it.
            else if (hasMaterial && ocean._material.IsKeywordEnabled("_OVERRIDEREFLECTIONCUBEMAP_ON") &&
                ocean._material.GetTexture("_ReflectionCubemapOverride") == null)
            {
                showMessage
                (
                    "<i>Override Reflection Cubemap</i> is enabled but no cubemap has been provided. " + oceanColourIncorrectText,
                    "Assign a cubemap or disable the checkbox on the ocean material.",
                    ValidatedHelper.MessageType.Warning, ocean
                );
            }

            // Validate scene view effects options.
            if (SceneView.lastActiveSceneView != null && !EditorApplication.isPlaying)
            {
                var sceneView = SceneView.lastActiveSceneView;

                // Validate "Animated Materials".
                if (ocean != null && !ocean._showOceanProxyPlane && !sceneView.sceneViewState.alwaysRefresh)
                {
                    showMessage
                    (
                        "<i>Animated Materials</i> is not enabled on the scene view. The ocean's framerate will appear low as updates are not real-time.",
                        "Enable <i>Animated Materials</i> on the scene view.",
                        ValidatedHelper.MessageType.Info, ocean,
                        _ =>
                        {
                            SceneView.lastActiveSceneView.sceneViewState.alwaysRefresh = true;
                            // Required after changing sceneViewState according to:
                            // https://docs.unity3d.com/ScriptReference/SceneView.SceneViewState.html
                            SceneView.RepaintAll();
                        }
                    );
                }

#if UNITY_POSTPROCESSING_ENABLED
                // Validate "Post-Processing".
                // Only check built-in renderer and Camera.main with enabled PostProcessLayer component.
                if (GraphicsSettings.currentRenderPipeline == null && Camera.main != null &&
                    Camera.main.TryGetComponent<UnityEngine.Rendering.PostProcessing.PostProcessLayer>(out var ppLayer)
                    && ppLayer.enabled && sceneView.sceneViewState.showImageEffects)
                {
                    showMessage
                    (
                        "<i>Post Processing</i> is enabled on the scene view. " +
                        "There is a Unity bug where gizmos and grid lines will render over opaque objects. " +
                        "Please see <i>Known Issues</i> in the documentation for a link to vote on having this issue resolved.",
                        "Disable <i>Post Processing</i> on the scene view.",
                        ValidatedHelper.MessageType.Warning, ocean,
                        _ =>
                        {
                            sceneView.sceneViewState.showImageEffects = false;
                            // Required after changing sceneViewState according to:
                            // https://docs.unity3d.com/ScriptReference/SceneView.SceneViewState.html
                            SceneView.RepaintAll();
                        }
                    );
                }
#endif
            }

            // SimSettingsAnimatedWaves
            if (_simSettingsAnimatedWaves)
            {
                _simSettingsAnimatedWaves.Validate(ocean, showMessage);
            }

            if (transform.eulerAngles.magnitude > 0.0001f)
            {
                showMessage
                (
                    $"There must be no rotation on the ocean GameObject, and no rotation on any parent. Currently the rotation Euler angles are {transform.eulerAngles}.",
                    "Clear this rotation from the GameObject.",
                    ValidatedHelper.MessageType.Error, ocean
                );
            }

            // For safety.
            if (ocean == null || ocean.OceanMaterial == null)
            {
                return isValid;
            }

            if (ocean.OceanMaterial.HasProperty(LodDataMgrFoam.MATERIAL_KEYWORD_PROPERTY) && ocean.CreateFoamSim != ocean.OceanMaterial.IsKeywordEnabled(LodDataMgrFoam.MATERIAL_KEYWORD))
            {
                if (ocean.CreateFoamSim)
                {
                    showMessage(LodDataMgrFoam.ERROR_MATERIAL_KEYWORD_MISSING, LodDataMgrFoam.ERROR_MATERIAL_KEYWORD_MISSING_FIX,
                        ValidatedHelper.MessageType.Error, ocean.OceanMaterial,
                        (material) => ValidatedHelper.FixSetMaterialOptionEnabled(material, LodDataMgrFoam.MATERIAL_KEYWORD, LodDataMgrFoam.MATERIAL_KEYWORD_PROPERTY, true));
                }
                else
                {
                    showMessage(LodDataMgrFoam.ERROR_MATERIAL_KEYWORD_ON_FEATURE_OFF, LodDataMgrFoam.ERROR_MATERIAL_KEYWORD_ON_FEATURE_OFF_FIX,
                        ValidatedHelper.MessageType.Info, ocean);
                }
            }

            if (ocean.OceanMaterial.HasProperty(LodDataMgrFlow.MATERIAL_KEYWORD_PROPERTY) && ocean.CreateFlowSim != ocean.OceanMaterial.IsKeywordEnabled(LodDataMgrFlow.MATERIAL_KEYWORD))
            {
                if (ocean.CreateFlowSim)
                {
                    showMessage(LodDataMgrFlow.ERROR_MATERIAL_KEYWORD_MISSING, LodDataMgrFlow.ERROR_MATERIAL_KEYWORD_MISSING_FIX,
                        ValidatedHelper.MessageType.Error, ocean.OceanMaterial,
                        (material) => ValidatedHelper.FixSetMaterialOptionEnabled(material, LodDataMgrFlow.MATERIAL_KEYWORD, LodDataMgrFlow.MATERIAL_KEYWORD_PROPERTY, true));
                }
                else
                {
                    showMessage(LodDataMgrFlow.ERROR_MATERIAL_KEYWORD_ON_FEATURE_OFF, LodDataMgrFlow.ERROR_MATERIAL_KEYWORD_ON_FEATURE_OFF_FIX,
                        ValidatedHelper.MessageType.Info, ocean);
                }
            }

            if (ocean.OceanMaterial.HasProperty(LodDataMgrShadow.MATERIAL_KEYWORD_PROPERTY) && ocean.CreateShadowData != ocean.OceanMaterial.IsKeywordEnabled(LodDataMgrShadow.MATERIAL_KEYWORD))
            {
                if (ocean.CreateShadowData)
                {
                    showMessage(LodDataMgrShadow.ERROR_MATERIAL_KEYWORD_MISSING, LodDataMgrShadow.ERROR_MATERIAL_KEYWORD_MISSING_FIX,
                        ValidatedHelper.MessageType.Error, ocean.OceanMaterial,
                        (material) => ValidatedHelper.FixSetMaterialOptionEnabled(material, LodDataMgrShadow.MATERIAL_KEYWORD, LodDataMgrShadow.MATERIAL_KEYWORD_PROPERTY, true));
                }
                else
                {
                    showMessage(LodDataMgrShadow.ERROR_MATERIAL_KEYWORD_ON_FEATURE_OFF, LodDataMgrShadow.ERROR_MATERIAL_KEYWORD_ON_FEATURE_OFF_FIX,
                        ValidatedHelper.MessageType.Info, ocean);
                }
            }

            if (ocean.OceanMaterial.HasProperty(LodDataMgrClipSurface.MATERIAL_KEYWORD_PROPERTY) && ocean.CreateClipSurfaceData != ocean.OceanMaterial.IsKeywordEnabled(LodDataMgrClipSurface.MATERIAL_KEYWORD))
            {
                if (ocean.CreateClipSurfaceData)
                {
                    showMessage(LodDataMgrClipSurface.ERROR_MATERIAL_KEYWORD_MISSING, LodDataMgrClipSurface.ERROR_MATERIAL_KEYWORD_MISSING_FIX,
                        ValidatedHelper.MessageType.Error, ocean.OceanMaterial,
                        (material) => ValidatedHelper.FixSetMaterialOptionEnabled(material, LodDataMgrClipSurface.MATERIAL_KEYWORD, LodDataMgrClipSurface.MATERIAL_KEYWORD_PROPERTY, true));
                }
                else
                {
                    showMessage(LodDataMgrClipSurface.ERROR_MATERIAL_KEYWORD_ON_FEATURE_OFF, LodDataMgrClipSurface.ERROR_MATERIAL_KEYWORD_ON_FEATURE_OFF_FIX,
                        ValidatedHelper.MessageType.Info, ocean);
                }
            }

            return isValid;
        }

        void OnValidate()
        {
            // Must be at least 0.25, and must be on a power of 2
            _minScale = Mathf.Pow(2f, Mathf.Round(Mathf.Log(Mathf.Max(_minScale, 0.25f), 2f)));

            // Max can be -1 which means no maximum
            if (_maxScale != -1f)
            {
                // otherwise must be at least 0.25, and must be on a power of 2
                _maxScale = Mathf.Pow(2f, Mathf.Round(Mathf.Log(Mathf.Max(_maxScale, _minScale), 2f)));
            }

            // Gravity 0 makes waves freeze which is weird but doesn't seem to break anything so allowing this for now
            _gravityMultiplier = Mathf.Max(_gravityMultiplier, 0f);

            // LOD data resolution multiple of 2 for general GPU texture reasons (like pixel quads)
            _lodDataResolution -= _lodDataResolution % 2;

            _geometryDownSampleFactor = Mathf.ClosestPowerOfTwo(Mathf.Max(_geometryDownSampleFactor, 1));

            var remGeo = _lodDataResolution % _geometryDownSampleFactor;
            if (remGeo > 0)
            {
                var newLDR = _lodDataResolution - (_lodDataResolution % _geometryDownSampleFactor);
                Debug.LogWarning
                (
                    "Adjusted Lod Data Resolution from " + _lodDataResolution + " to " + newLDR + " to ensure the Geometry Down Sample Factor is a factor (" + _geometryDownSampleFactor + ").",
                    this
                );

                _lodDataResolution = newLDR;
            }
        }

        internal static void FixSetFeatureEnabled(SerializedObject oceanSO, string paramName, bool enabled)
        {
            oceanSO.FindProperty(paramName).boolValue = enabled;
        }

#pragma warning disable 0618
        public bool ValidateObsolete(OceanRenderer ocean, ValidatedHelper.ShowMessage showMessage)
        {
            var isValid = true;

            if (_layerName != "")
            {
                showMessage
                (
                    "<i>Layer Name</i> on the <i>Ocean Renderer</i> is deprecated and will be removed. " +
                    "Use <i>Layer</i> instead.",
                    $"Set <i>Layer</i> to <i>{_layerName}</i> using the <i>Layer Name</i> to complete the migration.",
                    ValidatedHelper.MessageType.Warning, this,
                    (SerializedObject serializedObject) =>
                    {
                        serializedObject.FindProperty("_layer").intValue = LayerMask.NameToLayer(_layerName);
                        serializedObject.FindProperty("_layerName").stringValue = "";
                    }
                );
            }

            return isValid;
        }
#pragma warning restore 0618
    }

    [CustomEditor(typeof(OceanRenderer))]
    public class OceanRendererEditor : ValidatedEditor
    {
        OceanRenderer _target;
        MaterialEditor _materialEditor;

        void OnEnable()
        {
            _target = (OceanRenderer)target;

            if (_target._material != null)
            {
                // Create an instance of the default MaterialEditor.
                _materialEditor = (MaterialEditor)CreateEditor(_target._material);
            }
        }

        void OnDisable()
        {
            if (_materialEditor != null)
            {
                // Free the memory used by default MaterialEditor.
                DestroyImmediate(_materialEditor);
            }
        }

        public override void OnInspectorGUI()
        {
            var currentAssignedTP = serializedObject.FindProperty("_timeProvider").objectReferenceValue;

            base.OnInspectorGUI();

            var target = this.target as OceanRenderer;

            // Detect if user changed TP, if so update stack
            var newlyAssignedTP = serializedObject.FindProperty("_timeProvider").objectReferenceValue;
            if (currentAssignedTP != newlyAssignedTP)
            {
                if (currentAssignedTP != null)
                {
                    target.PopTimeProvider(currentAssignedTP as TimeProviderBase);
                }
                if (newlyAssignedTP != null)
                {
                    target.PushTimeProvider(newlyAssignedTP as TimeProviderBase);
                }
            }

            if (GUILayout.Button("Validate Setup"))
            {
                OceanRenderer.RunValidation(target);
            }

            if (GUILayout.Button("Open Material Online Help"))
            {
                Application.OpenURL(Internal.Constants.HELP_URL_BASE_USER + "configuration.html" + Internal.Constants.HELP_URL_RP + "#material-parameters");
            }

            DrawMaterialEditor();
        }

        // Adapted from: http://answers.unity.com/answers/975894/view.html
        void DrawMaterialEditor()
        {
            Material oldMaterial = null;

            if (_materialEditor != null)
            {
                oldMaterial = (Material)_materialEditor.target;
            }

            if (oldMaterial != _target._material)
            {
                serializedObject.ApplyModifiedProperties();

                if (_materialEditor != null)
                {
                    // Free the memory used by the previous MaterialEditor.
                    DestroyImmediate(_materialEditor);
                }

                if (_target._material != null)
                {
                    // Create a new instance of the default MaterialEditor.
                    _materialEditor = (MaterialEditor)CreateEditor(_target._material);
                }
            }

            if (_materialEditor != null)
            {
                // Draw the material's foldout and the material shader field. Required to call OnInspectorGUI.
                _materialEditor.DrawHeader();

                // We need to prevent the user from editing Unity's default materials.
                bool isDefaultMaterial = !AssetDatabase.GetAssetPath(_target._material).StartsWith("Assets");

                using (new EditorGUI.DisabledGroupScope(isDefaultMaterial))
                {
                    // Draw the material properties. Works only if the foldout of DrawHeader is open.
                    _materialEditor.OnInspectorGUI();
                }
            }
        }
    }
#endif
}
