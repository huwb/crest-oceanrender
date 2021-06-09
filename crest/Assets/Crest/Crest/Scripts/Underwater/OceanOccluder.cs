// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Crest
{
    // @volatile:OceanOccluderMaskValues These MUST match the values in OceanOccluderHelpers.hlsl
    public enum OceanOccluderType
    {
        OccludeAll = 0,
        CancelOcclusion = 1,
        OccludeWaterBehind = 2,
        OccludeWaterInFront = 3,
    }

    [RequireComponent(typeof(Renderer))]
    public class OceanOccluder : MonoBehaviour
    {
        private static Camera _currentCamera;
        private Renderer _renderer;

        public static readonly int sp_OceanOccluderType = Shader.PropertyToID("_OceanOccluderType");

        [Tooltip("What kind of ocean occluder is this?\nThe first two options should be used for opaque surfaces as they have a lower overhead. You can disable all ocean rendering on one material, and then have it cancelled-out by the other to re-enable water when outside of an enclosed space in the water.\nThe latter two should be used for transparencies but have a bit more overhead.")]
        [FormerlySerializedAs("MaskType")]
        public OceanOccluderType OccluderType = OceanOccluderType.OccludeWaterInFront;
        public Renderer Renderer => _renderer;

        void Start()
        {
            _renderer = GetComponent<Renderer>();
            if (_renderer == null)
            {
                Debug.LogError($"Ocean Occluder can only be added to Game Objects with a Renderer attached!", this);
                enabled = false;
            }
        }

        [RuntimeInitializeOnLoadMethod]
        static void RunOnStart()
        {
#if UNITY_2018
            RenderPipeline.beginCameraRendering -= BeginCameraRendering;
            RenderPipeline.beginCameraRendering += BeginCameraRendering;
#else
        RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
        RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
#endif
        }

#if UNITY_2018
        private static void BeginCameraRendering(Camera camera)
#else
        private static void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
#endif
        {
            _currentCamera = camera;
        }


        bool _hasCopied = false;

        // Called when visible to a camera
        void OnWillRenderObject()
        {
            // check if built-in pipeline being used
            if (Camera.current != null)
            {
                _currentCamera = Camera.current;
            }

            if (OccluderType == OceanOccluderType.OccludeWaterInFront && !_hasCopied)
            {
                // TODO(TRC):Now This hack exists because we have to copy the properties from the ocean renderer to the
                // transparent material so it can render the fog effect behind it properly. How we solve it is an
                // important problem. We need a way especially of transferring the shader defines across in a simple
                // and comprehensive manner.
                Material material = GetComponent<MeshRenderer>().material;
                material.CopyPropertiesFromMaterial(OceanRenderer.Instance.OceanMaterial);
                material.SetTexture("_SurfaceNormal", OceanRenderer.Instance.OceanMaterial.GetTexture("_Normals"));
            }

            var underwater = _currentCamera.GetComponent<IUnderwaterPostProcessPerCameraData>();
            if (underwater != null && underwater.enabled)
            {
                underwater.RegisterOceanOccluder(this);
            }
        }
    }
}
