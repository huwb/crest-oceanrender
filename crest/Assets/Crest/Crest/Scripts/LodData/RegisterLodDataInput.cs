// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Crest.Spline;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Crest
{
    using OceanInput = CrestSortedList<int, ILodDataInput>;

    /// <summary>
    /// Comparer that always returns less or greater, never equal, to get work around unique key constraint
    /// </summary>
    public class DuplicateKeyComparer<TKey> : IComparer<TKey> where TKey : IComparable
    {
        public int Compare(TKey x, TKey y)
        {
            int result = x.CompareTo(y);

            // If non-zero, use result, otherwise return greater (never equal)
            return result != 0 ? result : 1;
        }
    }

    public interface ILodDataInput
    {
        /// <summary>
        /// Draw the input (the render target will be bound)
        /// </summary>
        void Draw(CommandBuffer buf, float weight, int isTransition, int lodIdx);

        /// <summary>
        /// The wavelength of the input - used to choose which level of detail to apply the input to.
        /// This is primarily used for displacement/surface shape inputs which support rendering to
        /// specific LODs (which are then combined later). Specify 0 for no preference / render to all LODs.
        /// </summary>
        float Wavelength { get; }

        /// <summary>
        /// Whether to apply this input.
        /// </summary>
        bool Enabled { get; }
    }

    /// <summary>
    /// Base class for scripts that register input to the various LOD data types.
    /// </summary>
    [ExecuteAlways]
    public abstract partial class RegisterLodDataInputBase : MonoBehaviour, ILodDataInput
    {
#if UNITY_EDITOR
        [SerializeField, Tooltip("Check that the shader applied to this object matches the input type (so e.g. an Animated Waves input object has an Animated Waves input shader.")]
        [Predicated(typeof(MeshRenderer)), DecoratedField]
        bool _checkShaderName = true;
#endif

        public const string MENU_PREFIX = Internal.Constants.MENU_SCRIPTS + "LOD Inputs/Crest Register ";

        public abstract float Wavelength { get; }

        public abstract bool Enabled { get; }

        public static int sp_Weight = Shader.PropertyToID("_Weight");
        public static int sp_DisplacementAtInputPosition = Shader.PropertyToID("_DisplacementAtInputPosition");

        // By default do not follow horizontal motion of waves. This means that the ocean input will appear on the surface at its XZ location, instead
        // of moving horizontally with the waves.
        protected virtual bool FollowHorizontalMotion => false;

        protected abstract string ShaderPrefix { get; }

        static DuplicateKeyComparer<int> s_comparer = new DuplicateKeyComparer<int>();
        static Dictionary<Type, OceanInput> s_registrar = new Dictionary<Type, OceanInput>();

        public static OceanInput GetRegistrar(Type lodDataMgrType)
        {
            OceanInput registered;
            if (!s_registrar.TryGetValue(lodDataMgrType, out registered))
            {
                registered = new OceanInput(s_comparer);
                s_registrar.Add(lodDataMgrType, registered);
            }
            return registered;
        }

        protected Renderer _renderer;
        protected Material _material;
        SampleHeightHelper _sampleHelper = new SampleHeightHelper();

        void InitRendererAndMaterial(bool verifyShader)
        {
            _renderer = GetComponent<Renderer>();

            if (_renderer)
            {
#if UNITY_EDITOR
                if (Application.isPlaying && _checkShaderName && verifyShader)
                {
                    ValidatedHelper.ValidateInputMesh(RendererRequired, gameObject, ValidatedHelper.DebugLog);

                    if (TryGetComponent<MeshRenderer>(out var meshRenderer))
                    {
                        ValidatedHelper.ValidateMaterial(meshRenderer.sharedMaterial, ShaderPrefix, gameObject, ValidatedHelper.DebugLog);
                    }
                }
#endif

                _material = _renderer.sharedMaterial;
            }
        }

        protected void Start()
        {
            InitRendererAndMaterial(true);
        }

        protected virtual void Update()
        {
#if UNITY_EDITOR
            if (!EditorApplication.isPlaying)
            {
                InitRendererAndMaterial(true);
            }
#endif
        }

        public virtual void Draw(CommandBuffer buf, float weight, int isTransition, int lodIdx)
        {
            if (_renderer && _material && weight > 0f)
            {
                buf.SetGlobalFloat(sp_Weight, weight);
                buf.SetGlobalFloat(LodDataMgr.sp_LD_SliceIndex, lodIdx);

                if (!FollowHorizontalMotion)
                {
                    // This can be called multiple times per frame - one for each LOD potentially
                    _sampleHelper.Init(transform.position, 0f, true, this);
                    _sampleHelper.Sample(out Vector3 displacement, out _, out _);
                    buf.SetGlobalVector(sp_DisplacementAtInputPosition, displacement);
                }
                else
                {
                    buf.SetGlobalVector(sp_DisplacementAtInputPosition, Vector3.zero);
                }

                buf.DrawRenderer(_renderer, _material);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void InitStatics()
        {
            // Init here from 2019.3 onwards
            s_registrar.Clear();
            sp_Weight = Shader.PropertyToID("_Weight");
        }
    }

    /// <summary>
    /// Registers input to a particular LOD data.
    /// </summary>
    [ExecuteAlways]
    public abstract class RegisterLodDataInput<LodDataType> : RegisterLodDataInputBase
        where LodDataType : LodDataMgr
    {
        protected const string k_displacementCorrectionTooltip = "Whether this input data should displace horizontally with waves. If false, data will not move from side to side with the waves. Adds a small performance overhead when disabled.";

        [SerializeField, Predicated(typeof(MeshRenderer)), DecoratedField]
        bool _disableRenderer = true;

        protected abstract Color GizmoColor { get; }

        int _registeredQueueValue = int.MinValue;

        bool GetQueue(out int queue)
        {
            var rend = GetComponent<Renderer>();
            if (rend && rend.sharedMaterial != null)
            {
                queue = rend.sharedMaterial.renderQueue;
                return true;
            }
            queue = int.MinValue;
            return false;
        }

        protected virtual void OnEnable()
        {
            if (_disableRenderer)
            {
                var rend = GetComponent<Renderer>();
                if (rend)
                {
                    rend.enabled = false;
                }
            }

            int q;
            GetQueue(out q);

            var registrar = GetRegistrar(typeof(LodDataType));
            registrar.Add(q, this);
            _registeredQueueValue = q;
        }

        protected virtual void OnDisable()
        {
            var registrar = GetRegistrar(typeof(LodDataType));
            if (registrar != null)
            {
                registrar.Remove(this);
            }
        }

        protected override void Update()
        {
            base.Update();

#if UNITY_EDITOR
            if (!EditorApplication.isPlaying)
            {
                int q;
                if (GetQueue(out q))
                {
                    if (q != _registeredQueueValue)
                    {
                        var registrar = GetRegistrar(typeof(LodDataType));
                        registrar.Remove(this);
                        registrar.Add(q, this);
                        _registeredQueueValue = q;
                    }
                }
            }
#endif
        }

        protected void OnDrawGizmosSelected()
        {
            var mf = GetComponent<MeshFilter>();
            if (mf)
            {
                Gizmos.color = GizmoColor;
                Gizmos.DrawWireMesh(mf.sharedMesh, transform.position, transform.rotation, transform.lossyScale);
            }
        }
    }

    public abstract class RegisterLodDataInputWithSplineSupport<LodDataType>
        : RegisterLodDataInputWithSplineSupport<LodDataType, SplinePointDataNone>
        where LodDataType : LodDataMgr
    {
    }

    [ExecuteAlways]
    public abstract class RegisterLodDataInputWithSplineSupport<LodDataType, SplinePointCustomData>
        : RegisterLodDataInput<LodDataType>
        , ISplinePointCustomDataSetup
#if UNITY_EDITOR
        , IReceiveSplinePointOnDrawGizmosSelectedMessages
#endif
        where LodDataType : LodDataMgr
        where SplinePointCustomData : MonoBehaviour, ISplinePointCustomData
    {
        [Header("Spline settings")]
        [SerializeField, Predicated(typeof(Spline.Spline)), DecoratedField]
        bool _overrideSplineSettings = false;
        [SerializeField, Predicated("_overrideSplineSettings", typeof(Spline.Spline)), DecoratedField]
        float _radius = 20f;
        [SerializeField, Predicated("_overrideSplineSettings", typeof(Spline.Spline)), Delayed]
        int _subdivisions = 1;
        [SerializeField, Predicated("_overrideSplineSettings", typeof(Spline.Spline)), Delayed]
        int _smoothingIterations = 0;

        protected Material _splineMaterial;
        Spline.Spline _spline;
        Mesh _splineMesh;

        protected abstract string SplineShaderName { get; }
        protected abstract Vector2 DefaultCustomData { get; }

        void Awake()
        {
            if (TryGetComponent<Spline.Spline>(out _spline))
            {
                var radius = _overrideSplineSettings ? _radius : _spline.Radius;
                var subdivs = _overrideSplineSettings ? _subdivisions : _spline.Subdivisions;
                var smooth = _overrideSplineSettings ? _smoothingIterations : _spline.SmoothingIterations;
                ShapeGerstnerSplineHandling.GenerateMeshFromSpline<SplinePointCustomData>(_spline, transform, subdivs, radius, smooth, DefaultCustomData, ref _splineMesh);

                if (_splineMaterial == null)
                {
                    _splineMaterial = new Material(Shader.Find(SplineShaderName));
                }
            }
        }

        public override void Draw(CommandBuffer buf, float weight, int isTransition, int lodIdx)
        {
            if (weight <= 0f) return;

            if (_splineMesh != null && _splineMaterial != null)
            {
                buf.SetGlobalFloat(sp_Weight, weight);
                buf.SetGlobalFloat(LodDataMgr.sp_LD_SliceIndex, lodIdx);
                buf.SetGlobalVector(sp_DisplacementAtInputPosition, Vector3.zero);
                buf.DrawMesh(_splineMesh, transform.localToWorldMatrix, _splineMaterial);
            }
            else
            {
                base.Draw(buf, weight, isTransition, lodIdx);
            }
        }

        public bool AttachDataToSplinePoint(GameObject splinePoint)
        {
            if (typeof(SplinePointCustomData) == typeof(SplinePointDataNone))
            {
                // No custom data required
                return false;
            }

            if (splinePoint.TryGetComponent(out SplinePointCustomData _))
            {
                // Already existing, nothing to do
                return false;
            }

            splinePoint.AddComponent<SplinePointCustomData>();
            return true;
        }

#if UNITY_EDITOR
        protected override bool RendererRequired => _spline == null;

        protected override void Update()
        {
            base.Update();

            // Check for spline and rebuild spline mesh each frame in edit mode
            if (!EditorApplication.isPlaying)
            {
                if (_spline == null)
                {
                    TryGetComponent<Spline.Spline>(out _spline);
                }

                if (_spline != null)
                {
                    var radius = _overrideSplineSettings ? _radius : _spline.Radius;
                    var subdivs = _overrideSplineSettings ? _subdivisions : _spline.Subdivisions;
                    var smooth = _overrideSplineSettings ? _smoothingIterations : _spline.SmoothingIterations;
                    ShapeGerstnerSplineHandling.GenerateMeshFromSpline<SplinePointCustomData>(_spline, transform, subdivs, radius, smooth, DefaultCustomData, ref _splineMesh);

                    if (_splineMaterial == null)
                    {
                        _splineMaterial = new Material(Shader.Find(SplineShaderName));
                    }
                }
                else
                {
                    _splineMesh = null;
                }
            }
        }

        protected new void OnDrawGizmosSelected()
        {
            Gizmos.color = GizmoColor;
            Gizmos.DrawWireMesh(_splineMesh, transform.position, transform.rotation, transform.lossyScale);
        }

        public void OnSplinePointDrawGizmosSelected(SplinePoint point)
        {
            OnDrawGizmosSelected();
        }
#endif // UNITY_EDITOR
    }

#if UNITY_EDITOR
    public abstract partial class RegisterLodDataInputBase : IValidated
    {
        protected virtual bool RendererRequired => true;

        protected virtual string FeatureToggleLabel => null;
        protected virtual string FeatureToggleName => null;
        protected virtual bool FeatureEnabled(OceanRenderer ocean) => true;

        protected virtual string RequiredShaderKeyword => null;
        // NOTE: Temporary until shader keywords are the same across pipelines.
        protected virtual string RequiredShaderKeywordProperty => null;

        protected virtual string MaterialFeatureDisabledError => null;
        protected virtual string MaterialFeatureDisabledFix => null;

        public virtual bool Validate(OceanRenderer ocean, ValidatedHelper.ShowMessage showMessage)
        {
            var isValid = ValidatedHelper.ValidateInputMesh(RendererRequired, gameObject, showMessage);

            if (TryGetComponent<MeshRenderer>(out var meshRenderer))
            {
                isValid = ValidatedHelper.ValidateMaterial(meshRenderer.sharedMaterial, ShaderPrefix, gameObject, showMessage) && isValid;
            }

            if (ocean != null && !FeatureEnabled(ocean))
            {
                showMessage($"<i>{FeatureToggleLabel}</i> must be enabled on the <i>OceanRenderer</i> component.",
                    $"Enable the <i>{FeatureToggleLabel}</i> option on the <i>OceanRenderer</i> component.",
                    ValidatedHelper.MessageType.Error, ocean,
                    (so) => OceanRenderer.FixSetFeatureEnabled(so, FeatureToggleName, true)
                    );
                isValid = false;
            }

            if (ocean != null && !string.IsNullOrEmpty(RequiredShaderKeyword) && ocean.OceanMaterial.HasProperty(RequiredShaderKeywordProperty) && !ocean.OceanMaterial.IsKeywordEnabled(RequiredShaderKeyword))
            {
                showMessage(MaterialFeatureDisabledError, MaterialFeatureDisabledFix,
                    ValidatedHelper.MessageType.Error, ocean.OceanMaterial,
                    (material) => ValidatedHelper.FixSetMaterialOptionEnabled(material, RequiredShaderKeyword, RequiredShaderKeywordProperty, true));
                isValid = false;
            }

            return isValid;
        }
    }

    [CustomEditor(typeof(RegisterLodDataInputBase), true), CanEditMultipleObjects]
    class RegisterLodDataInputBaseEditor : ValidatedEditor { }
#endif
}
