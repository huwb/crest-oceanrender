// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Crest
{
    using SettingsType = SimSettingsShadow;

    /// <summary>
    /// Stores shadowing data to use during ocean shading. Shadowing is persistent and supports sampling across
    /// many frames and jittered sampling for (very) soft shadows.
    /// </summary>
    public class LodDataMgrShadow : LodDataMgr
    {
        public override string SimName { get { return "Shadow"; } }
        public override RenderTextureFormat TextureFormat { get { return RenderTextureFormat.RG16; } }
        protected override bool NeedToReadWriteTextureData { get { return true; } }

        public static bool s_processData = true;

        Light _mainLight;
        Camera _cameraMain;

        // SRP version needs access to this externally, hence public get
        public CommandBuffer BufCopyShadowMap { get; private set; }

        RenderTexture _sources;
        PropertyWrapperCompute _renderProperties;
        ComputeShader _updateShadowShader;
        private int krnl_UpdateShadow;
        public const string UpdateShadow = "UpdateShadow";

        readonly int sp_CenterPos = Shader.PropertyToID("_CenterPos");
        readonly int sp_Scale = Shader.PropertyToID("_Scale");
        readonly int sp_CamPos = Shader.PropertyToID("_CamPos");
        readonly int sp_CamForward = Shader.PropertyToID("_CamForward");
        readonly int sp_JitterDiameters_CurrentFrameWeights = Shader.PropertyToID("_JitterDiameters_CurrentFrameWeights");
        readonly int sp_MainCameraProjectionMatrix = Shader.PropertyToID("_MainCameraProjectionMatrix");
        readonly int sp_SimDeltaTime = Shader.PropertyToID("_SimDeltaTime");
        readonly int sp_LD_SliceIndex_Source = Shader.PropertyToID("_LD_SliceIndex_Source");
        readonly int sp_LD_TexArray_Target = Shader.PropertyToID("_LD_TexArray_Target");

        SettingsType _defaultSettings;
        public SettingsType Settings
        {
            get
            {
                if (_ocean._simSettingsShadow != null) return _ocean._simSettingsShadow;

                if (_defaultSettings == null)
                {
                    _defaultSettings = ScriptableObject.CreateInstance<SettingsType>();
                    _defaultSettings.name = SimName + " Auto-generated Settings";
                }
                return _defaultSettings;
            }
        }

        public LodDataMgrShadow(OceanRenderer ocean) : base(ocean)
        {
            Start();
        }

        public override void Start()
        {
            base.Start();

            _renderProperties = new PropertyWrapperCompute();
            _updateShadowShader = ComputeShaderHelpers.LoadShader(UpdateShadow);
            if (_updateShadowShader == null)
            {
                enabled = false;
                return;
            }

            try
            {
                krnl_UpdateShadow = _updateShadowShader.FindKernel(UpdateShadow);
            }
            catch (Exception)
            {
                Debug.LogError("Could not load shadow update kernel. Disabling shadows.", _ocean);
                enabled = false;
                return;
            }

            // Setup the camera.
            UpdateCameraMain();

#if UNITY_EDITOR
            if (!OceanRenderer.Instance.OceanMaterial.IsKeywordEnabled("_SHADOWS_ON"))
            {
                Debug.LogWarning("Shadowing is not enabled on the current ocean material and will not be visible.", _ocean);
            }
#endif
        }

        protected override bool InitData()
        {
            if (!base.InitData()) return false;

            int resolution = OceanRenderer.Instance.LodDataResolution;
            var desc = new RenderTextureDescriptor(resolution, resolution, TextureFormat, 0);
            _sources = CreateLodDataTextures(desc, SimName + "_1", NeedToReadWriteTextureData);

            TextureArrayHelpers.ClearToBlack(_sources);
            TextureArrayHelpers.ClearToBlack(_targets);

            return true;
        }

        bool StartInitLight()
        {
            _mainLight = OceanRenderer.Instance._primaryLight;

            if (_mainLight.type != LightType.Directional)
            {
                Debug.LogError("Primary light must be of type Directional.", OceanRenderer.Instance);
                return false;
            }

            if (_mainLight.shadows == LightShadows.None)
            {
                Debug.LogError("Shadows must be enabled on primary light to enable ocean shadowing (types Hard and Soft are equivalent for the ocean system).", OceanRenderer.Instance);
                return false;
            }

            return true;
        }

        public override void UpdateLodData()
        {
            if (!enabled)
            {
                return;
            }

            base.UpdateLodData();

            if (_mainLight != OceanRenderer.Instance._primaryLight)
            {
                if (_mainLight)
                {
                    _mainLight.RemoveCommandBuffer(LightEvent.BeforeScreenspaceMask, BufCopyShadowMap);
                    BufCopyShadowMap = null;
                    TextureArrayHelpers.ClearToBlack(_sources);
                    TextureArrayHelpers.ClearToBlack(_targets);
                }
                _mainLight = null;
            }

            if (!OceanRenderer.Instance._primaryLight)
            {
                if (!Settings._allowNullLight)
                {
                    Debug.LogWarning("Primary light must be specified on OceanRenderer script to enable shadows.", OceanRenderer.Instance);
                }
                return;
            }

            if (!_mainLight)
            {
                if (!StartInitLight())
                {
                    enabled = false;
                    return;
                }
            }

            if (BufCopyShadowMap == null && s_processData)
            {
                BufCopyShadowMap = new CommandBuffer();
                BufCopyShadowMap.name = "Shadow data";
                _mainLight.AddCommandBuffer(LightEvent.BeforeScreenspaceMask, BufCopyShadowMap);
            }
            else if (!s_processData && BufCopyShadowMap != null)
            {
                _mainLight.RemoveCommandBuffer(LightEvent.BeforeScreenspaceMask, BufCopyShadowMap);
                BufCopyShadowMap = null;
            }

            if (!s_processData)
            {
                return;
            }

            // Update the camera if it has changed.
            if (_cameraMain.transform != OceanRenderer.Instance.Viewpoint)
            {
                UpdateCameraMain();
            }

            Swap(ref _sources, ref _targets);

            BufCopyShadowMap.Clear();

            ValidateSourceData();

            // clear the shadow collection. it will be overwritten with shadow values IF the shadows render,
            // which only happens if there are (nontransparent) shadow receivers around. this is only reliable
            // in play mode, so don't do it in edit mode.
#if UNITY_EDITOR
            if (UnityEditor.EditorApplication.isPlaying)
#endif
            {
                TextureArrayHelpers.ClearToBlack(_targets);
            }

            {
                var lt = OceanRenderer.Instance._lodTransform;
                for (var lodIdx = lt.LodCount - 1; lodIdx >= 0; lodIdx--)
                {
                    _renderProperties.Initialise(BufCopyShadowMap, _updateShadowShader, krnl_UpdateShadow);

                    lt._renderData[lodIdx].Validate(0, SimName);
                    _renderProperties.SetVector(sp_CenterPos, lt._renderData[lodIdx]._posSnapped);
                    var scale = OceanRenderer.Instance.CalcLodScale(lodIdx);
                    _renderProperties.SetVector(sp_Scale, new Vector3(scale, 1f, scale));

                    if (OceanRenderer.Instance.Viewpoint != null)
                    {
                        _renderProperties.SetVector(sp_CamPos, OceanRenderer.Instance.Viewpoint.position);
                        _renderProperties.SetVector(sp_CamForward, OceanRenderer.Instance.Viewpoint.forward);
                    }

                    _renderProperties.SetVector(sp_JitterDiameters_CurrentFrameWeights, new Vector4(Settings._jitterDiameterSoft, Settings._jitterDiameterHard, Settings._currentFrameWeightSoft, Settings._currentFrameWeightHard));
                    _renderProperties.SetMatrix(sp_MainCameraProjectionMatrix, _cameraMain.projectionMatrix * _cameraMain.worldToCameraMatrix);
                    _renderProperties.SetFloat(sp_SimDeltaTime, OceanRenderer.Instance.DeltaTimeDynamics);

                    // compute which lod data we are sampling previous frame shadows from. if a scale change has happened this can be any lod up or down the chain.
                    var srcDataIdx = lodIdx + ScaleDifferencePow2;
                    srcDataIdx = Mathf.Clamp(srcDataIdx, 0, lt.LodCount - 1);
                    _renderProperties.SetInt(sp_LD_SliceIndex, lodIdx);
                    _renderProperties.SetInt(sp_LD_SliceIndex_Source, srcDataIdx);
                    BindSourceData(_renderProperties, false);
                    _renderProperties.SetTexture(sp_LD_TexArray_Target, _targets);

                    BufCopyShadowMap.DispatchCompute(_updateShadowShader, krnl_UpdateShadow,
                        OceanRenderer.Instance.LodDataResolution / THREAD_GROUP_SIZE_X,
                        OceanRenderer.Instance.LodDataResolution / THREAD_GROUP_SIZE_Y,
                        1);
                }

                // Process registered inputs.
                for (var lodIdx = lt.LodCount - 1; lodIdx >= 0; lodIdx--)
                {
                    BufCopyShadowMap.SetRenderTarget(_targets, _targets.depthBuffer, 0, CubemapFace.Unknown, lodIdx);
                    SubmitDraws(lodIdx, BufCopyShadowMap);
                }
            }
        }

        void UpdateCameraMain()
        {
            var viewpoint = OceanRenderer.Instance.Viewpoint;
            _cameraMain = viewpoint != null ? viewpoint.GetComponent<Camera>() : null;

            if (_cameraMain == null)
            {
                Debug.LogError("Could not find main camera, disabling shadow data", _ocean);
                enabled = false;
                return;
            }
        }

        public void ValidateSourceData()
        {
#if UNITY_EDITOR
            if (!UnityEditor.EditorApplication.isPlaying)
            {
                // Don't validate when not in play mode in editor as shadows won't be updating.
                return;
            }
#endif

            foreach (var renderData in OceanRenderer.Instance._lodTransform._renderDataSource)
            {
                renderData.Validate(BuildCommandBufferBase._lastUpdateFrame - OceanRenderer.FrameCount, SimName);
            }
        }

        public void BindSourceData(IPropertyWrapper simMaterial, bool paramsOnly)
        {
            var rd = OceanRenderer.Instance._lodTransform._renderDataSource;
            BindData(simMaterial, paramsOnly ? Texture2D.blackTexture : _sources as Texture, true, ref rd, true);
        }

        internal override void OnEnable()
        {
            base.OnEnable();

            RemoveCommandBuffers();
        }

        internal override void OnDisable()
        {
            base.OnDisable();

            RemoveCommandBuffers();
        }

        void RemoveCommandBuffers()
        {
            if (BufCopyShadowMap != null)
            {
                if (_mainLight)
                {
                    _mainLight.RemoveCommandBuffer(LightEvent.BeforeScreenspaceMask, BufCopyShadowMap);
                }
                BufCopyShadowMap = null;
            }
        }

        readonly static string s_textureArrayName = "_LD_TexArray_Shadow";
        private static TextureArrayParamIds s_textureArrayParamIds = new TextureArrayParamIds(s_textureArrayName);
        public static int ParamIdSampler(bool sourceLod = false) { return s_textureArrayParamIds.GetId(sourceLod); }
        protected override int GetParamIdSampler(bool sourceLod = false)
        {
            return ParamIdSampler(sourceLod);
        }
        public static void BindNull(IPropertyWrapper properties, bool sourceLod = false)
        {
            properties.SetTexture(ParamIdSampler(sourceLod), TextureArrayHelpers.BlackTextureArray);
        }

#if UNITY_2019_3_OR_NEWER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
        static void InitStatics()
        {
            // Init here from 2019.3 onwards
            s_textureArrayParamIds = new TextureArrayParamIds(s_textureArrayName);
        }
    }
}
