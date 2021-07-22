// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Crest
{
    /// <summary>
    /// Registers a custom input to the clip surface simulation. Attach this to GameObjects that you want to use to
    /// clip the surface of the ocean.
    /// </summary>
    [AddComponentMenu(MENU_PREFIX + "Clip Surface Input")]
    [HelpURL(Internal.Constants.HELP_URL_BASE_USER + "ocean-simulation.html" + Internal.Constants.HELP_URL_RP + "#clip-surface")]
    public class RegisterClipSurfaceInput : RegisterLodDataInput<LodDataMgrClipSurface>
    {
        /// <summary>
        /// The version of this asset. Can be used to migrate across versions. This value should
        /// only be changed when the editor upgrades the version.
        /// </summary>
        [SerializeField, HideInInspector]
#pragma warning disable 414
        int _version = 0;
#pragma warning restore 414

        bool _enabled = true;
        public override bool Enabled => _enabled;

        [Header("Convex Hull Options")]

        [Tooltip("Prevents inputs from cancelling each other out when aligned vertically. It is imperfect so custom logic might be needed for your use case.")]
        [SerializeField] bool _disableClipSurfaceWhenTooFarFromSurface = false;

        [Tooltip("Large, choppy waves require higher iterations to have accurate holes.")]
        [SerializeField] uint _animatedWavesDisplacementSamplingIterations = 4;

        [Header("Signed Distance")]
        public Transform _transform; // TODO

        public override float Wavelength => 0f;

        protected override Color GizmoColor => new Color(0f, 1f, 1f, 0.5f);

        protected override string ShaderPrefix => "Crest/Inputs/Clip Surface";

        // The clip surface samples at the displaced position in the ocean shader, so the displacement correction is not needed.
        protected override bool FollowHorizontalMotion => true;

        PropertyWrapperMPB _mpb;
        SampleHeightHelper _sampleHeightHelper = new SampleHeightHelper();

        static int sp_DisplacementSamplingIterations = Shader.PropertyToID("_DisplacementSamplingIterations");
        static readonly int sp_SignedDistanceShapeMatrix = Shader.PropertyToID("_SignedDistanceShapeMatrix");

        private void LateUpdate()
        {
            if (OceanRenderer.Instance == null || _renderer == null)
            {
                return;
            }

            // Prevents possible conflicts since overlapping doesn't work for every case.
            if (_disableClipSurfaceWhenTooFarFromSurface)
            {
                var position = transform.position;
                _sampleHeightHelper.Init(position, 0f);

                if (_sampleHeightHelper.Sample(out float waterHeight))
                {
                    position.y = waterHeight;
                    _enabled = Mathf.Abs(_renderer.bounds.ClosestPoint(position).y - waterHeight) < 1;
                }
            }
            else
            {
                _enabled = true;
            }

            // find which lod this object is overlapping
            var rect = new Rect(transform.position.x, transform.position.z, 0f, 0f);
            var lodIdx = LodDataMgrAnimWaves.SuggestDataLOD(rect);

            if (lodIdx > -1)
            {
                if (_mpb == null)
                {
                    _mpb = new PropertyWrapperMPB();
                }

                _renderer.GetPropertyBlock(_mpb.materialPropertyBlock);

                _mpb.SetInt(LodDataMgr.sp_LD_SliceIndex, lodIdx);
                _mpb.SetInt(sp_DisplacementSamplingIterations, (int)_animatedWavesDisplacementSamplingIterations);

                if (_transform != null)
                {
                    _mpb.SetMatrix(sp_SignedDistanceShapeMatrix, Matrix4x4.TRS(_transform.position, _transform.rotation, _transform.lossyScale).inverse);
                }

                _renderer.SetPropertyBlock(_mpb.materialPropertyBlock);
            }
        }

#if UNITY_EDITOR
        protected override string FeatureToggleName => "_createClipSurfaceData";
        protected override string FeatureToggleLabel => "Create Clip Surface Data";
        protected override bool FeatureEnabled(OceanRenderer ocean) => ocean.CreateClipSurfaceData;
        protected override string RequiredShaderKeywordProperty => LodDataMgrClipSurface.MATERIAL_KEYWORD_PROPERTY;
        protected override string RequiredShaderKeyword => LodDataMgrClipSurface.MATERIAL_KEYWORD;

        protected override string MaterialFeatureDisabledError => LodDataMgrClipSurface.ERROR_MATERIAL_KEYWORD_MISSING;
        protected override string MaterialFeatureDisabledFix => LodDataMgrClipSurface.ERROR_MATERIAL_KEYWORD_MISSING_FIX;

        static Mesh s_SphereMesh;

        protected override void OnDrawGizmosSelected()
        {
            base.OnDrawGizmosSelected();

            if (_renderer == null || _transform == null)
            {
                return;
            }

            var color = Color.magenta;
            color.a = 0.9f;

            Gizmos.color = color;
            Gizmos.matrix = Matrix4x4.TRS(_transform.position, _transform.rotation, _transform.lossyScale);

            if (_renderer.sharedMaterial.IsKeywordEnabled("_SHAPE_BOX"))
            {
                Gizmos.DrawCube(Vector3.zero, Vector3.one);
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            }
            else if (_renderer.sharedMaterial.IsKeywordEnabled("_SHAPE_SPHERE"))
            {
                if (s_SphereMesh == null)
                {
                    s_SphereMesh = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
                }

                // Gizmos.DrawSphere is too low resolution.
                Gizmos.DrawMesh(s_SphereMesh, submeshIndex: 0, Vector3.zero, Quaternion.identity, Vector3.one);
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(Vector3.zero, 1f);
            }
        }
#endif
    }
}
