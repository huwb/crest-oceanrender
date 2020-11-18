// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using System.Collections.Generic;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
#if UNITY_EDITOR
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.Experimental.SceneManagement;
#endif

#if !UNITY_2019_4_OR_NEWER
#error This version of Crest requires Unity 2019.4 or later.
#endif

namespace Crest
{
    /// <summary>
    /// The main script for the ocean system. Attach this to a GameObject to create an ocean. This script initializes the various data types and systems
    /// and moves/scales the ocean based on the viewpoint. It also hosts a number of global settings that can be tweaked here.
    /// </summary>
    [ExecuteAlways, SelectionBase]
    public partial class OceanRenderer : MonoBehaviour
    {
        [Tooltip("The viewpoint which drives the ocean detail. Defaults to the camera."), SerializeField]
        Transform _viewpoint;
        public Transform Viewpoint
        {
            get
            {
#if UNITY_EDITOR
                if (_followSceneCamera)
                {
                    var sceneViewCamera = EditorHelpers.GetActiveSceneViewCamera();
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
                    var sceneViewCamera = EditorHelpers.GetActiveSceneViewCamera();
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

        [Tooltip("Optional provider for time, can be used to hard-code time for automation, or provide server time. Defaults to local Unity time."), SerializeField]
        TimeProviderBase _timeProvider = null;
        TimeProviderDefault _timeProviderDefault = new TimeProviderDefault();
        public ITimeProvider TimeProvider
        {
            get
            {
                if (_timeProvider != null)
                {
                    return _timeProvider;
                }

                return _timeProviderDefault ?? (_timeProviderDefault = new TimeProviderDefault());
            }
        }

        public float CurrentTime => TimeProvider.CurrentTime;
        public float DeltaTime => TimeProvider.DeltaTime;
        public float DeltaTimeDynamics => TimeProvider.DeltaTimeDynamics;

        [Tooltip("The primary directional light. Required if shadowing is enabled.")]
        public Light _primaryLight;
        [Tooltip("If Primary Light is not set, search the scene for all directional lights and pick the brightest to use as the sun light.")]
        [SerializeField, PredicatedField("_primaryLight", true)]
        bool _searchForPrimaryLightOnStartup = true;

        [Header("Ocean Params")]

        [SerializeField, Tooltip("Material to use for the ocean surface")]
        Material _material = null;
        public Material OceanMaterial { get { return _material; } }

        [SerializeField]
        string _layerName = "Water";
        public string LayerName { get { return _layerName; } }

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

        [SerializeField, Delayed, Tooltip("Resolution of ocean LOD data. Use even numbers like 256 or 384. This is 4x the old 'Base Vert Density' param, so if you used 64 for this param, set this to 256. Press 'Rebuild Ocean' button below to apply.")]
        int _lodDataResolution = 256;
        public int LodDataResolution { get { return _lodDataResolution; } }

        [SerializeField, Delayed, Tooltip("How much of the water shape gets tessellated by geometry. If set to e.g. 4, every geometry quad will span 4x4 LOD data texels. Use power of 2 values like 1, 2, 4... Press 'Rebuild Ocean' button below to apply.")]
        int _geometryDownSampleFactor = 2;

        [SerializeField, Tooltip("Number of ocean tile scales/LODs to generate. Press 'Rebuild Ocean' button below to apply."), Range(2, LodDataMgr.MAX_LOD_COUNT)]
        int _lodCount = 7;


        [Header("Simulation Params")]

        public SimSettingsAnimatedWaves _simSettingsAnimatedWaves;

        [Tooltip("Water depth information used for shallow water, shoreline foam, wave attenuation, among others."), SerializeField]
        bool _createSeaFloorDepthData = true;
        public bool CreateSeaFloorDepthData { get { return _createSeaFloorDepthData; } }
        [PredicatedField("_createSeaFloorDepthData")]
        public SimSettingsDepth _simSettingsDepth;

        [Tooltip("Simulation of foam created in choppy water and dissipating over time."), SerializeField]
        bool _createFoamSim = true;
        public bool CreateFoamSim { get { return _createFoamSim; } }
        [PredicatedField("_createFoamSim")]
        public SimSettingsFoam _simSettingsFoam;

        [Tooltip("Dynamic waves generated from interactions with objects such as boats."), SerializeField]
        bool _createDynamicWaveSim = false;
        public bool CreateDynamicWaveSim { get { return _createDynamicWaveSim; } }
        [PredicatedField("_createDynamicWaveSim")]
        public SimSettingsWave _simSettingsDynamicWaves;

        [Tooltip("Horizontal motion of water body, akin to water currents."), SerializeField]
        bool _createFlowSim = false;
        public bool CreateFlowSim { get { return _createFlowSim; } }
        [PredicatedField("_createFlowSim")]
        public SimSettingsFlow _simSettingsFlow;

        [Tooltip("Shadow information used for lighting water."), SerializeField]
        bool _createShadowData = false;
        public bool CreateShadowData { get { return _createShadowData; } }
        [PredicatedField("_createShadowData")]
        public SimSettingsShadow _simSettingsShadow;

        [Tooltip("Clip surface information for clipping the ocean surface."), SerializeField]
        bool _createClipSurfaceData = false;
        public bool CreateClipSurfaceData { get { return _createClipSurfaceData; } }
        public enum DefaultClippingState
        {
            NothingClipped,
            EverythingClipped,
        }
        [Tooltip("Whether to clip nothing by default (and clip inputs remove patches of surface), or to clip everything by default (and clip inputs add patches of surface).")]
        [PredicatedField("_createClipSurfaceData")]
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

        [Tooltip("Move ocean with Scene view camera if Scene window is focused."), SerializeField, PredicatedField("_showOceanProxyPlane", true)]
#pragma warning disable 414
        bool _followSceneCamera = true;
#pragma warning restore 414

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

        SampleHeightHelper _sampleHeightHelper = new SampleHeightHelper();

        public static OceanRenderer Instance { get; private set; }

        // We are computing these values to be optimal based on the base mesh vertex density.
        float _lodAlphaBlackPointFade;
        float _lodAlphaBlackPointWhitePointFade;

        bool _canSkipCulling = false;

        readonly int sp_crestTime = Shader.PropertyToID("_CrestTime");
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

            // Align to 32 bytes
            public float __padding;
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

        // Drive state from OnEnable and OnDisable? OnEnable on RegisterLodDataInput seems to get called on script reload
        void OnEnable()
        {
            // We don't run in "prefab scenes", i.e. when editing a prefab. Bail out if prefab scene is detected.
#if UNITY_EDITOR
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                return;
            }
#endif

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
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            // We don't run in "prefab scenes", i.e. when editing a prefab. Bail out if prefab scene is detected.
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
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
            {
                if (_lodDataAnimWaves == null)
                {
                    _lodDataAnimWaves = new LodDataMgrAnimWaves(this);
                    _lodDatas.Add(_lodDataAnimWaves);
                }
            }

            if (CreateClipSurfaceData)
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

            if (CreateFoamSim)
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

            if (CreateShadowData)
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

            // Potential extension - add 'type' field to collprovider and change provider if settings have changed - this would support runtime changes.
            if (CollisionProvider == null)
            {
                CollisionProvider = _lodDataAnimWaves.Settings.CreateCollisionProvider();
            }
        }

        bool VerifyRequirements()
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

            return true;
        }

        void ValidateViewpoint()
        {
            if (Viewpoint == null)
            {
                Debug.LogError("Crest needs to know where to focus the ocean detail. Please set the <i>ViewCamera</i> or the <i>Viewpoint</i> property that will render the ocean, or tag the primary camera as <i>MainCamera</i>.", this);
            }
        }

#if UNITY_2019_3_OR_NEWER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
        static void InitStatics()
        {
            // Init here from 2019.3 onwards
            Instance = null;

            sp_ForceUnderwater = Shader.PropertyToID("_ForceUnderwater");
            sp_perCascadeInstanceData = Shader.PropertyToID("_CrestPerCascadeInstanceData");
            sp_cascadeData = Shader.PropertyToID("_CrestCascadeData");
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

        void RunUpdate()
        {
            // Run queries *before* changing the ocean position, as it needs the current LOD positions to associate with the current queries
#if UNITY_EDITOR
            // Issue #630 - seems to be a terrible memory leak coming from creating async gpu readbacks. We don't rely on queries in edit mode AFAIK
            // so knock this out.
            if (EditorApplication.isPlaying)
#endif
            {
                CollisionProvider.UpdateQueries();
                FlowProvider.UpdateQueries();
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
            // Hack - due to SV_IsFrontFace occasionally coming through as true for back faces,
            // add a param here that forces ocean to be in underwater state. I think the root
            // cause here might be imprecision or numerical issues at ocean tile boundaries, although
            // i'm not sure why cracks are not visible in this case.
            OceanMaterial.SetFloat(sp_ForceUnderwater, ViewerHeightAboveWater < -2f ? 1f : 0f);

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
            // If there are local bodies of water, this will do overlap tests between the ocean tiles
            // and the water bodies and turn off any that don't overlap.
            if (WaterBody.WaterBodies.Count == 0 && _canSkipCulling)
            {
                return;
            }

            foreach (OceanChunkRenderer tile in _oceanChunkRenderers)
            {
                if (tile.Rend == null)
                {
                    continue;
                }

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

                tile.Rend.enabled = overlappingOne || WaterBody.WaterBodies.Count == 0;
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

            _bufPerCascadeInstanceData.Dispose();
            _bufCascadeDataTgt.Dispose();
            _bufCascadeDataSrc.Dispose();
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

            // UnderwaterEffect
            var underwaters = FindObjectsOfType<UnderwaterEffect>();
            foreach (var underwater in underwaters)
            {
                underwater.Validate(ocean, ValidatedHelper.DebugLog);
            }

            // OceanDepthCache
            var depthCaches = FindObjectsOfType<OceanDepthCache>();
            foreach (var depthCache in depthCaches)
            {
                depthCache.Validate(ocean, ValidatedHelper.DebugLog);
            }

            // AssignLayer
            var assignLayers = FindObjectsOfType<AssignLayer>();
            foreach (var assign in assignLayers)
            {
                assign.Validate(ocean, ValidatedHelper.DebugLog);
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

#if !UNITY_2019_4_9_OR_NEWER
            if (_camera == null)
            {
                showMessage
                (
                    "Not setting the camera property will result in using Camera.main which has a significant " +
                    "performance cost. This is improved in Unity 2019.4.9 and above.",
                    ValidatedHelper.MessageType.Warning, ocean
                );
            }
#endif

            if (_material == null)
            {
                showMessage
                (
                    "A material for the ocean must be assigned on the Material property of the OceanRenderer.",
                    ValidatedHelper.MessageType.Error, ocean
                );

                isValid = false;
            }

            // OceanRenderer
            if (FindObjectsOfType<OceanRenderer>().Length > 1)
            {
                showMessage
                (
                    "Multiple OceanRenderer scripts detected in open scenes, this is not typical - usually only one OceanRenderer is expected to be present.",
                    ValidatedHelper.MessageType.Warning, ocean
                );
            }

            // ShapeGerstnerBatched
            var gerstners = FindObjectsOfType<ShapeGerstnerBatched>();
            if (gerstners.Length == 0)
            {
                showMessage
                (
                    "No ShapeGerstnerBatched script found, so ocean will appear flat (no waves).",
                    ValidatedHelper.MessageType.Info, ocean
                );
            }

            // Ocean Detail Parameters
            var baseMeshDensity = _lodDataResolution * 0.25f / _geometryDownSampleFactor;

            if (baseMeshDensity < 8)
            {
                showMessage
                (
                    "Base mesh density is lower than 8. There will be visible gaps in the ocean surface. " +
                    "Increase the <i>LOD Data Resolution</i> or decrease the <i>Geometry Down Sample Factor</i>.",
                    ValidatedHelper.MessageType.Error, ocean
                );
            }
            else if (baseMeshDensity < 16)
            {
                showMessage
                (
                    "Base mesh density is lower than 16. There will be visible transitions when traversing the ocean surface. " +
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
                            "There is no skybox set in the lighting settings window. " +
                            oceanColourIncorrectText +
                            alternativesText,
                            ValidatedHelper.MessageType.Warning, ocean
                        );
                    }
                    // Spherical Harmonics is missing and required.
                    else if (isLightingDataMissing)
                    {
                        showMessage
                        (
                            "Lighting data is missing which provides baked spherical harmonics." +
                            oceanColourIncorrectText +
                            "Generate lighting or enable Auto Generate from the Lighting window. " +
                            alternativesText,
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
                            "Environmental Reflections is set to Custom, but no cubemap has been provided. " +
                            oceanColourIncorrectText +
                            "Assign a cubemap in the lighting settings window. " +
                            alternativesText,
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
                    "<i>Override Reflection Cubemap</i> is enabled but no cubemap has been provided. " +
                    oceanColourIncorrectText +
                    "Assign a cubemap or disable the checkbox on the ocean material.",
                    ValidatedHelper.MessageType.Warning, ocean
                );
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
                    ValidatedHelper.MessageType.Error, ocean
                );
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
    }

    [CustomEditor(typeof(OceanRenderer))]
    public class OceanRendererEditor : ValidatedEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var target = this.target as OceanRenderer;

            if (GUILayout.Button("Rebuild Ocean"))
            {
                target.enabled = false;
                target.enabled = true;
            }

            if (GUILayout.Button("Validate Setup"))
            {
                OceanRenderer.RunValidation(target);
            }
        }
    }
#endif
}
