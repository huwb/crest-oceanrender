// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using UnityEngine;

namespace Crest
{
    /// <summary>
    /// Registers a custom input to the wave shape. Attach this GameObjects that you want to render into the displacmeent textures to affect ocean shape.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu(MENU_PREFIX + "Animated Waves Input")]
    [HelpURL(Internal.Constants.HELP_URL_BASE_USER + "ocean-simulation.html" + Internal.Constants.HELP_URL_RP + "#animated-waves")]
    public class RegisterAnimWavesInput : RegisterLodDataInputWithSplineSupport<LodDataMgrAnimWaves>
    {
        /// <summary>
        /// The version of this asset. Can be used to migrate across versions. This value should
        /// only be changed when the editor upgrades the version.
        /// </summary>
        [SerializeField, HideInInspector]
#pragma warning disable 414
        int _version = 0;
#pragma warning restore 414

        public override bool Enabled => true;

        [Header("Anim Waves Input Settings")]
        [SerializeField, Tooltip("Which octave to render into, for example set this to 2 to use render into the 2m-4m octave. These refer to the same octaves as the wave spectrum editor. Set this value to 0 to render into all LODs.")]
        float _octaveWavelength = 0f;
        public override float Wavelength
        {
            get
            {
                return _octaveWavelength;
            }
        }

        public readonly static Color s_gizmoColor = new Color(0f, 1f, 0f, 0.5f);
        protected override Color GizmoColor => s_gizmoColor;

        protected override string ShaderPrefix => "Crest/Inputs/Animated Waves";

        protected override string SplineShaderName => "Crest/Inputs/Animated Waves/Set Base Water Height Using Geometry";
        protected override Vector2 DefaultCustomData => Vector2.zero;

        [SerializeField, Tooltip(k_displacementCorrectionTooltip)]
        bool _followHorizontalMotion = true;
        protected override bool FollowHorizontalMotion => _followHorizontalMotion;

        [SerializeField, Tooltip("Inform ocean how much this input will displace the ocean surface vertically. This is used to set bounding box heights for the ocean tiles.")]
        float _maxDisplacementVertical = 0f;
        [SerializeField, Tooltip("Inform ocean how much this input will displace the ocean surface horizontally. This is used to set bounding box widths for the ocean tiles.")]
        float _maxDisplacementHorizontal = 0f;

        [SerializeField, Tooltip("Use the bounding box of an attached renderer component to determine the max vertical displacement.")]
        [Predicated(typeof(MeshRenderer)), DecoratedField]
        bool _reportRendererBoundsToOceanSystem = true;

        protected override void Update()
        {
            base.Update();

            if (OceanRenderer.Instance == null)
            {
                return;
            }

            var maxDispVert = _maxDisplacementVertical;

            // let ocean system know how far from the sea level this shape may displace the surface
            if (_reportRendererBoundsToOceanSystem)
            {
                var minY = _renderer.bounds.min.y;
                var maxY = _renderer.bounds.max.y;
                var seaLevel = OceanRenderer.Instance.SeaLevel;
                maxDispVert = Mathf.Max(maxDispVert, Mathf.Abs(seaLevel - minY), Mathf.Abs(seaLevel - maxY));
            }

            if (_maxDisplacementHorizontal > 0f || maxDispVert > 0f)
            {
                OceanRenderer.Instance.ReportMaxDisplacementFromShape(_maxDisplacementHorizontal, maxDispVert, 0f);
            }
        }
    }
}
