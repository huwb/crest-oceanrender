// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Crest
{
    /// <summary>
    /// Registers a custom input to affect the water height.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu(MENU_PREFIX + "Height Input")]
    public partial class RegisterHeightInput : RegisterLodDataInputWithSplineSupport<LodDataMgrAnimWaves>
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

        public override float Wavelength => 0f;

        public readonly static Color s_gizmoColor = new Color(0f, 1f, 0f, 0.5f);
        protected override Color GizmoColor => s_gizmoColor;

        protected override string ShaderPrefix => "Crest/Inputs/Animated Waves";

        protected override string SplineShaderName => "Crest/Inputs/Animated Waves/Set Base Water Height Using Geometry";
        protected override Vector2 DefaultCustomData => Vector2.zero;

        protected override bool FollowHorizontalMotion => true;

        [Header("Height Input Settings")]
        [SerializeField, Tooltip("Inform ocean how much this input will displace the ocean surface vertically. This is used to set bounding box heights for the ocean tiles.")]
        float _maxDisplacementVertical = 0f;

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
            if (_reportRendererBoundsToOceanSystem && _renderer != null)
            {
                var minY = _renderer.bounds.min.y;
                var maxY = _renderer.bounds.max.y;
                var seaLevel = OceanRenderer.Instance.SeaLevel;
                maxDispVert = Mathf.Max(maxDispVert, Mathf.Abs(seaLevel - minY), Mathf.Abs(seaLevel - maxY));
            }

            if (maxDispVert > 0f)
            {
                OceanRenderer.Instance.ReportMaxDisplacementFromShape(0f, maxDispVert, 0f);
            }
        }

#if UNITY_EDITOR
        // Animated waves are always enabled
        protected override bool FeatureEnabled(OceanRenderer ocean) => true;
#endif // UNITY_EDITOR
    }

#if UNITY_EDITOR
    public partial class RegisterHeightInput
    {
        public override bool Validate(OceanRenderer ocean, ValidatedHelper.ShowMessage showMessage)
        {
            var isValid = base.Validate(ocean, showMessage);

            if (isValid)
            {
                if (ocean != null && ocean._simSettingsAnimatedWaves._renderTextureGraphicsFormat != GraphicsFormat.R32G32B32A32_SFloat)
                {
                    showMessage(
                        "Changing the height of the ocean can reduce precision leading to artefacts like tearing or incorrect normals. " +
                        $"{ocean._simSettingsAnimatedWaves._renderTextureGraphicsFormat} may not have enough precision.",
                        "Change graphics format to <i>R32G32B32A32_SFloat</i>.",
                        ValidatedHelper.MessageType.Warning,
                        ocean
                    );
                }
            }

            return isValid;
        }
    }
#endif
}
