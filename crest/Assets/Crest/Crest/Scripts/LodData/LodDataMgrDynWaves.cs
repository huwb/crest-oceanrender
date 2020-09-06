// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using UnityEngine;

namespace Crest
{
    using SettingsType = SimSettingsWave;

    /// <summary>
    /// A dynamic shape simulation that moves around with a displacement LOD.
    /// </summary>
    public class LodDataMgrDynWaves : LodDataMgrPersistent
    {
        protected override string ShaderSim { get { return "UpdateDynWaves"; } }
        protected override int krnl_ShaderSim { get { return _shader.FindKernel(ShaderSim); } }

        public override string SimName { get { return "DynamicWaves"; } }
        public override RenderTextureFormat TextureFormat { get { return RenderTextureFormat.RGHalf; } }

        public bool _rotateLaplacian = true;

        public const string DYNWAVES_KEYWORD = "CREST_DYNAMIC_WAVE_SIM_ON_INTERNAL";

        bool[] _active;
        public bool SimActive(int lodIdx) { return _active[lodIdx]; }

        readonly int sp_HorizDisplace = Shader.PropertyToID("_HorizDisplace");
        readonly int sp_DisplaceClamp = Shader.PropertyToID("_DisplaceClamp");
        readonly int sp_Damping = Shader.PropertyToID("_Damping");
        readonly int sp_Gravity = Shader.PropertyToID("_Gravity");
        readonly int sp_LaplacianAxisX = Shader.PropertyToID("_LaplacianAxisX");

        SettingsType _defaultSettings;
        public SettingsType Settings
        {
            get
            {
                if (_ocean._simSettingsDynamicWaves != null) return _ocean._simSettingsDynamicWaves;

                if (_defaultSettings == null)
                {
                    _defaultSettings = ScriptableObject.CreateInstance<SettingsType>();
                    _defaultSettings.name = SimName + " Auto-generated Settings";
                }
                return _defaultSettings;
            }
        }

        public LodDataMgrDynWaves(OceanRenderer ocean) : base(ocean)
        {
            Start();
        }

        protected override bool InitData()
        {
            if (!base.InitData()) return false;

            _active = new bool[OceanRenderer.Instance.CurrentLodCount];
            for (int i = 0; i < _active.Length; i++) _active[i] = true;
            return true;
        }

        internal override void OnEnable()
        {
            base.OnEnable();

            Shader.EnableKeyword(DYNWAVES_KEYWORD);
        }

        internal override void OnDisable()
        {
            base.OnDisable();

            Shader.DisableKeyword(DYNWAVES_KEYWORD);
        }

        protected override bool BuildCommandBufferInternal(int lodIdx)
        {
            if (!base.BuildCommandBufferInternal(lodIdx))
                return false;

            // check if the sim should be running
            float texelWidth = OceanRenderer.Instance._lodTransform._renderData[lodIdx].Validate(0, SimName)._texelWidth;
            _active[lodIdx] = texelWidth >= Settings._minGridSize && (texelWidth <= Settings._maxGridSize || Settings._maxGridSize == 0f);

            return true;
        }

        public void BindCopySettings(IPropertyWrapper target)
        {
            target.SetFloat(sp_HorizDisplace, Settings._horizDisplace);
            target.SetFloat(sp_DisplaceClamp, Settings._displaceClamp);
        }

        protected override void SetAdditionalSimParams(IPropertyWrapper simMaterial)
        {
            base.SetAdditionalSimParams(simMaterial);

            simMaterial.SetFloat(sp_Damping, Settings._damping);
            simMaterial.SetFloat(sp_Gravity, OceanRenderer.Instance.Gravity * Settings._gravityMultiplier);

            float laplacianKernelAngle = _rotateLaplacian ? Mathf.PI * 2f * Random.value : 0f;
            simMaterial.SetVector(sp_LaplacianAxisX, new Vector2(Mathf.Cos(laplacianKernelAngle), Mathf.Sin(laplacianKernelAngle)));

            // assign sea floor depth - to slot 1 current frame data. minor bug here - this depth will actually be from the previous frame,
            // because the depth is scheduled to render just before the animated waves, and this sim happens before animated waves.
            if (OceanRenderer.Instance._lodDataSeaDepths != null)
            {
                OceanRenderer.Instance._lodDataSeaDepths.BindResultData(simMaterial);
            }
            else
            {
                LodDataMgrSeaFloorDepth.BindNull(simMaterial);
            }

            if (OceanRenderer.Instance._lodDataFlow != null)
            {
                OceanRenderer.Instance._lodDataFlow.BindResultData(simMaterial);
            }
            else
            {
                LodDataMgrFlow.BindNull(simMaterial);
            }
        }

        public static void CountWaveSims(int countFrom, out int o_present, out int o_active)
        {
            o_present = OceanRenderer.Instance.CurrentLodCount;
            o_active = 0;
            for (int i = 0; i < o_present; i++)
            {
                if (i < countFrom) continue;
                if (!OceanRenderer.Instance._lodDataDynWaves.SimActive(i)) continue;

                o_active++;
            }
        }

        float MaxSimDt(int lodIdx)
        {
            var ocean = OceanRenderer.Instance;

            // Limit timestep based on Courant constant: https://www.uio.no/studier/emner/matnat/ifi/nedlagte-emner/INF2340/v05/foiler/sim04.pdf
            var Cmax = Settings._courantNumber;
            var minWavelength = ocean._lodTransform.MaxWavelength(lodIdx) / 2f;
            var waveSpeed = OceanWaveSpectrum.ComputeWaveSpeed(minWavelength, Settings._gravityMultiplier);
            // 0.5f because its 2D
            var maxDt = 0.5f * Cmax * ocean.CalcGridSize(lodIdx) / waveSpeed;
            return maxDt;
        }

        public override void GetSimSubstepData(float frameDt, out int numSubsteps, out float substepDt)
        {
            // lod 0 will always be most demanding - wave speed is square root of wavelength, so waves will be fast relative to stability in
            // lowest lod, and slow relative to stability in largest lod.
            float maxDt = MaxSimDt(0);

            numSubsteps = Mathf.CeilToInt(frameDt / maxDt);
            // Always do at least one step so that the sim moves around when time is frozen
            numSubsteps = Mathf.Clamp(numSubsteps, 1, Settings._maxSimStepsPerFrame);
            substepDt = Mathf.Min(maxDt, frameDt / numSubsteps);
        }

        readonly static string s_textureArrayName = "_LD_TexArray_DynamicWaves";
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
