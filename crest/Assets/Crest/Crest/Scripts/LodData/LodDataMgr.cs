// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using UnityEngine;
using UnityEngine.Rendering;

namespace Crest
{
    /// <summary>
    /// Base class for data/behaviours created on each LOD.
    /// </summary>
    public abstract class LodDataMgr
    {
        public abstract string SimName { get; }

        public abstract RenderTextureFormat TextureFormat { get; }

        // NOTE: This MUST match the value in OceanConstants.hlsl, as it
        // determines the size of the texture arrays in the shaders.
        public const int MAX_LOD_COUNT = 15;

        // NOTE: these MUST match the values in OceanConstants.hlsl
        // 64 recommended as a good common minimum: https://www.reddit.com/r/GraphicsProgramming/comments/aeyfkh/for_compute_shaders_is_there_an_ideal_numthreads/
        public const int THREAD_GROUP_SIZE_X = 8;
        public const int THREAD_GROUP_SIZE_Y = 8;

        protected abstract int GetParamIdSampler(bool sourceLod = false);

        protected abstract bool NeedToReadWriteTextureData { get; }

        protected RenderTexture _targets;

        public RenderTexture DataTexture { get { return _targets; } }

        public static int sp_LD_SliceIndex = Shader.PropertyToID("_LD_SliceIndex");
        protected static int sp_LODChange = Shader.PropertyToID("_LODChange");

        // shape texture resolution
        int _shapeRes = -1;

        // ocean scale last frame - used to detect scale changes
        float _oceanLocalScalePrev = -1f;

        int _scaleDifferencePow2 = 0;
        protected int ScaleDifferencePow2 { get { return _scaleDifferencePow2; } }

        public bool enabled { get; protected set; }

        protected OceanRenderer _ocean;

        public LodDataMgr(OceanRenderer ocean)
        {
            _ocean = ocean;
        }

        public virtual void Start()
        {
            enabled = InitData();
        }

        public static RenderTexture CreateLodDataTextures(RenderTextureDescriptor desc, string name, bool needToReadWriteTextureData)
        {
            RenderTexture result = new RenderTexture(desc);
            result.wrapMode = TextureWrapMode.Clamp;
            result.antiAliasing = 1;
            result.filterMode = FilterMode.Bilinear;
            result.anisoLevel = 0;
            result.useMipMap = false;
            result.name = name;
            result.dimension = TextureDimension.Tex2DArray;
            result.volumeDepth = OceanRenderer.Instance.CurrentLodCount;
            result.enableRandomWrite = needToReadWriteTextureData;
            result.Create();
            return result;
        }

        protected virtual bool InitData()
        {
            Debug.Assert(SystemInfo.SupportsRenderTextureFormat(TextureFormat), "The graphics device does not support the render texture format " + TextureFormat.ToString());

            Debug.Assert(OceanRenderer.Instance.CurrentLodCount <= MAX_LOD_COUNT);

            var resolution = OceanRenderer.Instance.LodDataResolution;
            var desc = new RenderTextureDescriptor(resolution, resolution, TextureFormat, 0);
            _targets = CreateLodDataTextures(desc, SimName, NeedToReadWriteTextureData);

            return true;
        }

        public virtual void UpdateLodData()
        {
            int width = OceanRenderer.Instance.LodDataResolution;
            // debug functionality to resize RT if different size was specified.
            if (_shapeRes == -1)
            {
                _shapeRes = width;
            }
            else if (width != _shapeRes)
            {
                _targets.Release();
                _targets.width = _targets.height = _shapeRes;
                _targets.Create();

                _shapeRes = width;
            }

            // determine if this LOD has changed scale and by how much (in exponent of 2)
            float oceanLocalScale = OceanRenderer.Instance.Root.localScale.x;
            if (_oceanLocalScalePrev == -1f) _oceanLocalScalePrev = oceanLocalScale;
            float ratio = oceanLocalScale / _oceanLocalScalePrev;
            _oceanLocalScalePrev = oceanLocalScale;
            float ratio_l2 = Mathf.Log(ratio) / Mathf.Log(2f);
            _scaleDifferencePow2 = Mathf.RoundToInt(ratio_l2);
        }

        public void BindResultData(IPropertyWrapper properties, bool blendOut = true)
        {
            BindData(properties, _targets, blendOut, ref OceanRenderer.Instance._lodTransform._renderData);
        }

        // Avoid heap allocations instead BindData
        protected Vector4[] _BindData_paramIdPosScales = new Vector4[MAX_LOD_COUNT + 1];
        // Used in child
        protected Vector4[] _BindData_paramIdOceans = new Vector4[MAX_LOD_COUNT + 1];
        protected virtual void BindData(IPropertyWrapper properties, Texture applyData, bool blendOut, ref LodTransform.RenderData[] renderData, bool sourceLod = false)
        {
            if (applyData)
            {
                properties.SetTexture(GetParamIdSampler(sourceLod), applyData);
            }

            for (int lodIdx = 0; lodIdx < OceanRenderer.Instance.CurrentLodCount; lodIdx++)
            {
                // NOTE: gets zeroed by unity, see https://www.alanzucconi.com/2016/10/24/arrays-shaders-unity-5-4/
                _BindData_paramIdPosScales[lodIdx] = new Vector4(
                    renderData[lodIdx]._posSnapped.x, renderData[lodIdx]._posSnapped.z,
                    OceanRenderer.Instance.CalcLodScale(lodIdx), 0f);
                _BindData_paramIdOceans[lodIdx] = new Vector4(renderData[lodIdx]._texelWidth, renderData[lodIdx]._textureRes, 1f, 1f / renderData[lodIdx]._textureRes);
            }

            // Duplicate the last element as the shader accesses element {slice index + 1] in a few situations. This way going
            // off the end of this parameter is the same as going off the end of the texture array with our clamped sampler.
            _BindData_paramIdPosScales[OceanRenderer.Instance.CurrentLodCount] = _BindData_paramIdPosScales[OceanRenderer.Instance.CurrentLodCount - 1];
            _BindData_paramIdOceans[OceanRenderer.Instance.CurrentLodCount] = _BindData_paramIdOceans[OceanRenderer.Instance.CurrentLodCount - 1];
            // Never use this last lod - it exists to give 'something' but should not be used
            _BindData_paramIdOceans[OceanRenderer.Instance.CurrentLodCount].z = 0f;

            properties.SetVectorArray(LodTransform.ParamIdPosScale(sourceLod), _BindData_paramIdPosScales);
            properties.SetVectorArray(LodTransform.ParamIdOcean(sourceLod), _BindData_paramIdOceans);
        }

        public virtual void BuildCommandBuffer(OceanRenderer ocean, CommandBuffer buf)
        {
        }

        public static void Swap<T>(ref T a, ref T b)
        {
            var temp = b;
            b = a;
            a = temp;
        }

        public interface IDrawFilter
        {
            float Filter(ILodDataInput data, out int isTransition);
        }

        protected void SubmitDraws(int lodIdx, CommandBuffer buf)
        {
            var lt = OceanRenderer.Instance._lodTransform;
            lt._renderData[lodIdx].Validate(0, SimName);

            lt.SetViewProjectionMatrices(lodIdx, buf);

            var drawList = RegisterLodDataInputBase.GetRegistrar(GetType());
            foreach (var draw in drawList)
            {
                if (!draw.Value.Enabled)
                {
                    continue;
                }

                draw.Value.Draw(buf, 1f, 0, lodIdx);
            }
        }

        protected void SubmitDrawsFiltered(int lodIdx, CommandBuffer buf, IDrawFilter filter)
        {
            var lt = OceanRenderer.Instance._lodTransform;
            lt._renderData[lodIdx].Validate(0, SimName);

            lt.SetViewProjectionMatrices(lodIdx, buf);

            var drawList = RegisterLodDataInputBase.GetRegistrar(GetType());
            foreach (var draw in drawList)
            {
                if (!draw.Value.Enabled)
                {
                    continue;
                }

                int isTransition;
                float weight = filter.Filter(draw.Value, out isTransition);
                if (weight > 0f)
                {
                    draw.Value.Draw(buf, weight, isTransition, lodIdx);
                }
            }
        }

        protected struct TextureArrayParamIds
        {
            private int _paramId;
            private int _paramId_Source;
            public TextureArrayParamIds(string textureArrayName)
            {
                _paramId = Shader.PropertyToID(textureArrayName);
                // Note: string concatonation does generate a small amount of
                // garbage. However, this is called on initialisation so should
                // be ok for now? Something worth considering for the future if
                // we want to go garbage-free.
                _paramId_Source = Shader.PropertyToID(textureArrayName + "_Source");
            }
            public int GetId(bool sourceLod) { return sourceLod ? _paramId_Source : _paramId; }
        }

        internal virtual void OnEnable()
        {
        }
        internal virtual void OnDisable()
        {
        }

#if UNITY_2019_3_OR_NEWER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
        static void InitStatics()
        {
            // Init here from 2019.3 onwards
            sp_LD_SliceIndex = Shader.PropertyToID("_LD_SliceIndex");
            sp_LODChange = Shader.PropertyToID("_LODChange");
        }
    }
}
