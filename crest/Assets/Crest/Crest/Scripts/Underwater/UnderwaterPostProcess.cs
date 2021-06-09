// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

using static Crest.UnderwaterPostProcessUtils;

namespace Crest
{
    /// <summary>
    /// Underwater Post Process. If a camera needs to go underwater it needs to have this script attached. This adds fullscreen passes and should
    /// only be used if necessary. This effect disables itself when camera is not close to the water volume.
    ///
    /// For convenience, all shader material settings are copied from the main ocean shader. This includes underwater
    /// specific features such as enabling the meniscus.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class UnderwaterPostProcess : MonoBehaviour, IUnderwaterPostProcessPerCameraData
    {
        [Header("Settings"), SerializeField, Tooltip(toolipCopyOceanParamsEachFrame)]
        bool _copyOceanMaterialParamsEachFrame = true;

        [SerializeField, Tooltip("Assign this to a material that uses shader `Crest/Underwater/Post Process`, with the same features enabled as the ocean surface material(s).")]
        Material _underwaterPostProcessMaterial;

        [SerializeField, Tooltip(UnderwaterPostProcessUtils.tooltipFilterOceanData), Range(UnderwaterPostProcessUtils.MinFilterOceanDataValue, UnderwaterPostProcessUtils.MaxFilterOceanDataValue)]
        public int _filterOceanData = UnderwaterPostProcessUtils.DefaultFilterOceanDataValue;

        [SerializeField, Tooltip(tooltipMeniscus)]
        bool _meniscus = true;

        [Header("Debug Options")]
        [SerializeField] bool _viewPostProcessMask = false;
        [SerializeField] bool _disableOceanMask = false;
        [SerializeField, Tooltip(tooltipHorizonSafetyMarginMultiplier), Range(0f, 1f)]
        float _horizonSafetyMarginMultiplier = UnderwaterPostProcessUtils.DefaultHorizonSafetyMarginMultiplier;
        // end public debug options

        private Camera _mainCamera;
        private RenderTexture _oceanMask;
        private RenderTexture _oceanDepthBuffer;
        private RenderTexture _oceanOccluderMask;
        private RenderTexture _oceanOccluderDepthBuffer;
        private CommandBuffer _maskCommandBuffer;
        private CommandBuffer _postProcessCommandBuffer;

        private Plane[] _cameraFrustumPlanes;

        private Material _oceanMaskMaterial = null;
        private Material _oceanOccluderMaskMaterial = null;

        private PropertyWrapperMaterial _underwaterPostProcessMaterialWrapper;

        private List<OceanOccluder> _oceanOccluderMasksToRender;
        public List<OceanOccluder> OceanOccluderMasksToRender => _oceanOccluderMasksToRender;

        private const string SHADER_OCEAN_MASK = "Crest/Underwater/Ocean Mask";
        private const string OCEAN_OCCLUDER_MASK = "Crest/Underwater/Ocean Occluder Mask";

        UnderwaterSphericalHarmonicsData _sphericalHarmonicsData = new UnderwaterSphericalHarmonicsData();

        bool _eventsRegistered = false;
        bool _firstRender = true;

        int sp_CrestCameraColorTexture = Shader.PropertyToID("_CrestCameraColorTexture");

        // Only one camera is supported.
        public static UnderwaterPostProcess Instance { get; private set; }

        public void RegisterOceanOccluder(OceanOccluder _oceanOccluder)
        {
            _oceanOccluderMasksToRender.Add(_oceanOccluder);
        }

        private bool InitialisedCorrectly()
        {
            _mainCamera = GetComponent<Camera>();
            if (_mainCamera == null)
            {
                Debug.LogError("UnderwaterPostProcess must be attached to a camera", this);
                return false;
            }

            if (_underwaterPostProcessMaterial == null)
            {
                Debug.LogError("UnderwaterPostProcess must have a post processing material assigned", this);
                return false;
            }

            {
                var maskShader = Shader.Find(SHADER_OCEAN_MASK);
                _oceanMaskMaterial = maskShader ? new Material(maskShader) : null;
                if (_oceanMaskMaterial == null)
                {
                    Debug.LogError($"Could not create a material with shader {SHADER_OCEAN_MASK}", this);
                    return false;
                }
            }

            {
                var oceanOccluderShader = Shader.Find(OCEAN_OCCLUDER_MASK);
                _oceanOccluderMaskMaterial = oceanOccluderShader ? new Material(oceanOccluderShader) : null;
                if (_oceanOccluderMaskMaterial == null)
                {
                    Debug.LogError($"Could not create a material with shader {OCEAN_OCCLUDER_MASK}", this);
                    return false;
                }
            }

            // TODO: Use run-time materials only.
            return true;
        }

        bool CheckMaterial()
        {
            var success = true;

            var keywords = _underwaterPostProcessMaterial.shaderKeywords;
            foreach (var keyword in keywords)
            {
                if (keyword == "_COMPILESHADERWITHDEBUGINFO_ON") continue;

                if (!OceanRenderer.Instance.OceanMaterial.IsKeywordEnabled(keyword))
                {
                    Debug.LogWarning($"Keyword {keyword} was enabled on the underwater material {_underwaterPostProcessMaterial.name} but not on the ocean material {OceanRenderer.Instance.OceanMaterial.name}, underwater appearance may not match ocean surface in standalone builds.", this);

                    success = false;
                }
            }

            return success;
        }

        void Awake()
        {
            if (!InitialisedCorrectly())
            {
                enabled = false;
                return;
            }

            if (_postProcessCommandBuffer == null)
            {
                _postProcessCommandBuffer = new CommandBuffer()
                {
                    name = "Underwater Pass",
                };
            }

            // Stop the material from being saved on-edits at runtime
            _underwaterPostProcessMaterial = new Material(_underwaterPostProcessMaterial);
            _underwaterPostProcessMaterialWrapper = new PropertyWrapperMaterial(_underwaterPostProcessMaterial);

            _oceanOccluderMasksToRender = new List<OceanOccluder>();
        }

        private void OnDestroy()
        {
            if (OceanRenderer.Instance && _eventsRegistered)
            {
                OceanRenderer.Instance.ViewerLessThan2mAboveWater -= ViewerLessThan2mAboveWater;
                OceanRenderer.Instance.ViewerMoreThan2mAboveWater -= ViewerMoreThan2mAboveWater;
            }

            _eventsRegistered = false;
        }

        void OnEnable()
        {
            Instance = this;
            _mainCamera.AddCommandBuffer(CameraEvent.AfterForwardAlpha, _postProcessCommandBuffer);
        }

        void OnDisable()
        {
            Instance = null;
            _mainCamera.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, _postProcessCommandBuffer);
        }

        private void ViewerMoreThan2mAboveWater(OceanRenderer ocean)
        {
            // TODO(TRC):Now This "optimisation" doesn't work if you have underwater windows, because the ocean mask
            // needs to be rendered for them to work no matter the heigh of the camera- > it gives the ocean holes.

            // I think it might be worth re-considering remove the event system for this stuff and implementing this as
            // separate enabler script that spins-up to enable/disabel to the post-process effect. (maybe?)
            //
            // That way we can actively check if there are any UnderwaterEffectFilters, and then only enable this if
            // there aren't any? :DeOptimiseForFilters
            // enabled = false;
        }

        private void ViewerLessThan2mAboveWater(OceanRenderer ocean)
        {
            enabled = true;
        }

        void OnPreRender()
        {
            XRHelpers.Update(_mainCamera);

            // Allocate planes only once
            if (_cameraFrustumPlanes == null)
            {
                _cameraFrustumPlanes = GeometryUtility.CalculateFrustumPlanes(_mainCamera);
                _maskCommandBuffer = new CommandBuffer();
                _maskCommandBuffer.name = "Ocean Mask Command Buffer";
                _mainCamera.AddCommandBuffer(
                    CameraEvent.BeforeForwardAlpha,
                    _maskCommandBuffer
                );
            }
            else
            {
                GeometryUtility.CalculateFrustumPlanes(_mainCamera, _cameraFrustumPlanes);
                _maskCommandBuffer.Clear();
            }

            RenderTextureDescriptor descriptor = XRHelpers.IsRunning
                    ? XRHelpers.EyeRenderTextureDescriptor
                    : new RenderTextureDescriptor(_mainCamera.pixelWidth, _mainCamera.pixelHeight);
            InitialiseMaskTextures(descriptor, true, ref _oceanMask, ref _oceanDepthBuffer);
            // TODO(TRC):Now only initialise these if there are any transparent ocean occluders
            InitialiseMaskTextures(descriptor, false, ref _oceanOccluderMask, ref _oceanOccluderDepthBuffer);

            PopulateOceanMask(
                _maskCommandBuffer, _mainCamera,
                OceanRenderer.Instance.Tiles,
                _oceanOccluderMasksToRender,
                _cameraFrustumPlanes,
                _oceanMask, _oceanDepthBuffer, _oceanMaskMaterial,
                _oceanOccluderMask, _oceanOccluderDepthBuffer, _oceanOccluderMaskMaterial,
                _sphericalHarmonicsData,
                _horizonSafetyMarginMultiplier,
                _disableOceanMask
            );

            _oceanOccluderMasksToRender.Clear();

            if (OceanRenderer.Instance == null)
            {
                _eventsRegistered = false;
                return;
            }

            if (!_eventsRegistered)
            {
                OceanRenderer.Instance.ViewerLessThan2mAboveWater += ViewerLessThan2mAboveWater;
                OceanRenderer.Instance.ViewerMoreThan2mAboveWater += ViewerMoreThan2mAboveWater;
                // TODO(TRC):Now See :DeOptimiseForFilters
                // enabled = OceanRenderer.Instance.ViewerHeightAboveWater < 2f;
                _eventsRegistered = true;
            }

            if (GL.wireframe)
            {
                return;
            }

            descriptor.useDynamicScale = _mainCamera.allowDynamicResolution;
            // Format must be correct for CopyTexture to work. Hopefully this is good enough.
            if (_mainCamera.allowHDR) descriptor.colorFormat = RenderTextureFormat.DefaultHDR;

            var temporaryColorBuffer = RenderTexture.GetTemporary(descriptor);

            UpdatePostProcessMaterial(
                temporaryColorBuffer,
                _mainCamera,
                _underwaterPostProcessMaterialWrapper,
                _sphericalHarmonicsData,
                _meniscus,
                _firstRender || _copyOceanMaterialParamsEachFrame,
                _viewPostProcessMask,
                _horizonSafetyMarginMultiplier,
                _filterOceanData
            );

            _postProcessCommandBuffer.Clear();

            if (_mainCamera.allowMSAA)
            {
                // Use blit if MSAA is active because transparents were not included with CopyTexture.
                // Not sure if we need an MSAA resolve? Not sure how to do that...
                _postProcessCommandBuffer.Blit(BuiltinRenderTextureType.CameraTarget, temporaryColorBuffer);
            }
            else
            {
                // Copy the frame buffer as we cannot read/write at the same time. If it causes problems, replace with Blit.
                _postProcessCommandBuffer.CopyTexture(BuiltinRenderTextureType.CameraTarget, temporaryColorBuffer);
            }

            _underwaterPostProcessMaterialWrapper.SetTexture(sp_CrestCameraColorTexture, temporaryColorBuffer);

            _postProcessCommandBuffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget, 0, CubemapFace.Unknown, -1);
            _postProcessCommandBuffer.DrawProcedural(Matrix4x4.identity, _underwaterPostProcessMaterial, -1, MeshTopology.Triangles, 3, 1);

            RenderTexture.ReleaseTemporary(temporaryColorBuffer);

            _firstRender = false;
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(UnderwaterPostProcess))]
    public class UnderwaterPostProcessEditor : Editor {}
#endif
}
