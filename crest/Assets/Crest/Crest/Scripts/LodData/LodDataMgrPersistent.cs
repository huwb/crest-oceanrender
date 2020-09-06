// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using UnityEngine;
using UnityEngine.Rendering;

namespace Crest
{
    /// <summary>
    /// A persistent simulation that moves around with a displacement LOD.
    /// </summary>
    public abstract class LodDataMgrPersistent : LodDataMgr
    {
        protected override bool NeedToReadWriteTextureData { get { return true; } }

        RenderTexture _sources;
        PropertyWrapperCompute _renderSimProperties;

        readonly int sp_LD_TexArray_Target = Shader.PropertyToID("_LD_TexArray_Target");

        protected ComputeShader _shader;

        protected abstract string ShaderSim { get; }
        protected abstract int krnl_ShaderSim { get; }

        float _substepDtPrevious = 1f / 60f;

        readonly int sp_SimDeltaTime = Shader.PropertyToID("_SimDeltaTime");
        readonly int sp_SimDeltaTimePrev = Shader.PropertyToID("_SimDeltaTimePrev");

        public LodDataMgrPersistent(OceanRenderer ocean) : base(ocean)
        {
        }

        public override void Start()
        {
            base.Start();

            CreateProperties();
        }

        void CreateProperties()
        {
            _shader = ComputeShaderHelpers.LoadShader(ShaderSim);
            if (_shader == null)
            {
                enabled = false;
                return;
            }
            _renderSimProperties = new PropertyWrapperCompute();
        }

        protected override bool InitData()
        {
            if (!base.InitData()) return false;

            int resolution = OceanRenderer.Instance.LodDataResolution;
            var desc = new RenderTextureDescriptor(resolution, resolution, TextureFormat, 0);
            _sources = CreateLodDataTextures(desc, SimName + "_1", NeedToReadWriteTextureData);

            TextureArrayHelpers.ClearToBlack(_targets);
            TextureArrayHelpers.ClearToBlack(_sources);

            return true;
        }

        public void ValidateSourceData(bool usePrevTransform)
        {
            var renderDataToValidate = usePrevTransform ?
                OceanRenderer.Instance._lodTransform._renderDataSource
                : OceanRenderer.Instance._lodTransform._renderData;
            int validationFrame = usePrevTransform ? BuildCommandBufferBase._lastUpdateFrame - OceanRenderer.FrameCount : 0;
            foreach (var renderData in renderDataToValidate)
            {
                renderData.Validate(validationFrame, SimName);
            }
        }

        public void BindSourceData(IPropertyWrapper properties, bool paramsOnly, bool usePrevTransform, bool sourceLod = false)
        {
            var renderData = usePrevTransform ?
                OceanRenderer.Instance._lodTransform._renderDataSource
                : OceanRenderer.Instance._lodTransform._renderData;

            BindData(properties, paramsOnly ? TextureArrayHelpers.BlackTextureArray : (Texture)_sources, true, ref renderData, sourceLod);
        }

        public abstract void GetSimSubstepData(float frameDt, out int numSubsteps, out float substepDt);

        public override void BuildCommandBuffer(OceanRenderer ocean, CommandBuffer buf)
        {
            base.BuildCommandBuffer(ocean, buf);

            var lodCount = ocean.CurrentLodCount;

            float substepDt;
            int numSubsteps;
            GetSimSubstepData(ocean.DeltaTime, out numSubsteps, out substepDt);

            for (int stepi = 0; stepi < numSubsteps; stepi++)
            {
                Swap(ref _sources, ref _targets);

                _renderSimProperties.Initialise(buf, _shader, krnl_ShaderSim);

                _renderSimProperties.SetFloat(sp_SimDeltaTime, substepDt);
                _renderSimProperties.SetFloat(sp_SimDeltaTimePrev, _substepDtPrevious);

                // compute which lod data we are sampling source data from. if a scale change has happened this can be any lod up or down the chain.
                // this is only valid on the first update step, after that the scale src/target data are in the right places.
                var srcDataIdxChange = ((stepi == 0) ? ScaleDifferencePow2 : 0);

                // only take transform from previous frame on first substep
                var usePreviousFrameTransform = stepi == 0;

                // bind data to slot 0 - previous frame data
                ValidateSourceData(usePreviousFrameTransform);
                BindSourceData(_renderSimProperties, false, usePreviousFrameTransform, true);

                SetAdditionalSimParams(_renderSimProperties);

                buf.SetGlobalFloat(sp_LODChange, srcDataIdxChange);

                _renderSimProperties.SetTexture(
                    sp_LD_TexArray_Target,
                    DataTexture
                );

                // Bind current data
                BindData(_renderSimProperties, null, false, ref OceanRenderer.Instance._lodTransform._renderData, false);

                buf.DispatchCompute(_shader, krnl_ShaderSim,
                    OceanRenderer.Instance.LodDataResolution / THREAD_GROUP_SIZE_X,
                    OceanRenderer.Instance.LodDataResolution / THREAD_GROUP_SIZE_Y,
                    OceanRenderer.Instance.CurrentLodCount);

                for (var lodIdx = lodCount - 1; lodIdx >= 0; lodIdx--)
                {
                    buf.SetRenderTarget(_targets, _targets.depthBuffer, 0, CubemapFace.Unknown, lodIdx);
                    SubmitDraws(lodIdx, buf);
                }

                _substepDtPrevious = substepDt;
            }

            // any post-sim steps. the dyn waves updates the copy sim material, which the anim wave will later use to copy in
            // the dyn waves results.
            for (var lodIdx = lodCount - 1; lodIdx >= 0; lodIdx--)
            {
                BuildCommandBufferInternal(lodIdx);
            }
        }

        protected virtual bool BuildCommandBufferInternal(int lodIdx)
        {
            return true;
        }

        /// <summary>
        /// Set any sim-specific shader params.
        /// </summary>
        protected virtual void SetAdditionalSimParams(IPropertyWrapper simMaterial)
        {
        }
    }
}
