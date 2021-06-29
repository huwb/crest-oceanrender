// Crest Ocean System

// Copyright 2021 Wave Harmonic Ltd

using UnityEngine;
using UnityEngine.Rendering;
using Crest.Spline;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Crest
{
    /// <summary>
    /// FFT ocean wave shape
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu(Internal.Constants.MENU_PREFIX_SCRIPTS + "Shape FFT")]
    [HelpURL(Internal.Constants.HELP_URL_BASE_USER + "wave-conditions.html" + Internal.Constants.HELP_URL_RP + "#shapefft-preview")]
    public partial class ShapeFFT : MonoBehaviour, LodDataMgrAnimWaves.IShapeUpdatable
#if UNITY_EDITOR
        , IReceiveSplinePointOnDrawGizmosSelectedMessages
#endif
    {
        public bool _UseThisFFTForHeightQueries = false;

        /// <summary>
        /// The version of this asset. Can be used to migrate across versions. This value should
        /// only be changed when the editor upgrades the version.
        /// </summary>
        [SerializeField, HideInInspector]
#pragma warning disable 414
        int _version = 0;
#pragma warning restore 414

        [Header("Wave Conditions")]
        [Tooltip("Impacts how aligned waves are with wind.")]
        [Range(0, 1)]
        public float _windTurbulence = 0.145f;

        [Tooltip("The spectrum that defines the ocean surface shape. Assign asset of type Crest/Ocean Waves Spectrum."), Embedded]
        public OceanWaveSpectrum _spectrum;
        OceanWaveSpectrum _activeSpectrum = null;

        [Tooltip("When true, the wave spectrum is evaluated once on startup in editor play mode and standalone builds, rather than every frame. This is less flexible but reduces the performance cost significantly."), SerializeField]
        bool _spectrumFixedAtRuntime = true;

        [Tooltip("Primary wave direction heading (deg). This is the angle from x axis in degrees that the waves are oriented towards. If a spline is being used to place the waves, this angle is relative ot the spline."), Range(-180, 180)]
        public float _waveDirectionHeadingAngle = 0f;
        public float WindDirRadForFFT => _meshForDrawingWaves != null ? 0f : _waveDirectionHeadingAngle * Mathf.Deg2Rad;
        public Vector2 PrimaryWaveDirection => new Vector2(Mathf.Cos(Mathf.PI * _waveDirectionHeadingAngle / 180f), Mathf.Sin(Mathf.PI * _waveDirectionHeadingAngle / 180f));

        [Tooltip("When true, uses the wind speed on this component rather than the wind speed from the Ocean Renderer component.")]
        public bool _overrideGlobalWindSpeed = false;
        [Tooltip("Wind speed in km/h. Controls wave conditions."), Range(0, 150f, 2f), Predicated("_overrideGlobalWindSpeed")]
        public float _windSpeed = 20f;
        public float WindSpeedForFFT => (_overrideGlobalWindSpeed ? _windSpeed : OceanRenderer.Instance._globalWindSpeed) / 3.6f;

        [Tooltip("Multiplier for these waves to scale up/down."), Range(0f, 1f)]
        public float _weight = 1f;

        [Tooltip("How much these waves respect the shallow water attenuation setting in the Animated Waves Settings. Set to 0 to ignore shallow water."), SerializeField, Range(0f, 1f)]
        float _respectShallowWaterAttenuation = 1f;

        [HideInInspector]
        [Delayed, Tooltip("How many wave components to generate in each octave.")]
        public int _componentsPerOctave = 8;

        [HideInInspector]
        [Tooltip("Change to get a different set of waves.")]
        public int _randomSeed = 0;

        [Header("Generation Settings")]
        [Tooltip("Resolution to use for wave generation buffers. Low resolutions are more efficient but can result in noticeable patterns in the shape."), Delayed]
        public int _resolution = 32;

        [Tooltip("In Editor, shows the wave generation buffers on screen."), SerializeField]
#pragma warning disable 414
        bool _debugDrawSlicesInEditor = false;
#pragma warning restore 414

        [Header("Spline Settings")]
        [SerializeField]
        bool _overrideSplineSettings = false;
        [SerializeField, Predicated("_overrideSplineSettings"), DecoratedField]
        float _radius = 50f;
        [SerializeField, Predicated("_overrideSplineSettings"), Delayed]
        int _subdivisions = 1;
        [SerializeField, Predicated("_overrideSplineSettings"), Delayed]
        int _smoothingIterations = 60;

        [SerializeField]
        float _featherWaveStart = 0.1f;

        [Header("Culling")]
        [Tooltip("Maximum amount surface will be displaced vertically from sea level. Increase this if gaps appear at bottom of screen."), SerializeField]
        float _maxVerticalDisplacement = 10f;
        [Tooltip("Maximum amount a point on the surface will be displaced horizontally by waves from its rest position. Increase this if gaps appear at sides of screen."), SerializeField]
        float _maxHorizontalDisplacement = 15f;

        Mesh _meshForDrawingWaves;

        float _windTurbulenceOld;
        float _windSpeedOld;
        float _windDirRadOld;
        OceanWaveSpectrum _spectrumOld;

        public class FFTBatch : ILodDataInput
        {
            ShapeFFT _shapeFFT;

            Material _material;
            Mesh _mesh;

            int _waveBufferSliceIndex;

            public FFTBatch(ShapeFFT shapeFFT, float wavelength, int waveBufferSliceIndex, Material material, Mesh mesh)
            {
                _shapeFFT = shapeFFT;
                Wavelength = wavelength;
                _waveBufferSliceIndex = waveBufferSliceIndex;
                _mesh = mesh;
                _material = material;
            }

            // The ocean input system uses this to decide which lod this batch belongs in
            public float Wavelength { get; private set; }

            public bool Enabled { get => true; set { } }

            public void Draw(CommandBuffer buf, float weight, int isTransition, int lodIdx)
            {
                var finalWeight = weight * _shapeFFT._weight;
                if (finalWeight > 0f)
                {
                    buf.SetGlobalInt(LodDataMgr.sp_LD_SliceIndex, lodIdx);
                    buf.SetGlobalFloat(RegisterLodDataInputBase.sp_Weight, finalWeight);
                    buf.SetGlobalInt(sp_WaveBufferSliceIndex, _waveBufferSliceIndex);
                    buf.SetGlobalFloat(sp_AverageWavelength, Wavelength * 1.5f);
                    // Either use a full screen quad, or a provided mesh renderer to draw the waves
                    if (_mesh == null)
                    {
                        buf.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 3);
                    }
                    else if (_material != null)
                    {
                        buf.DrawMesh(_mesh, _shapeFFT.transform.localToWorldMatrix, _material);
                    }
                }
            }
        }

        const int CASCADE_COUNT = 16;

        FFTBatch[] _batches = null;

        // Used to populate data on first frame
        bool _firstUpdate = true;

        Material _matGenerateWaves;

        static readonly int sp_WaveBuffer = Shader.PropertyToID("_WaveBuffer");
        static readonly int sp_WaveBufferSliceIndex = Shader.PropertyToID("_WaveBufferSliceIndex");
        static readonly int sp_AverageWavelength = Shader.PropertyToID("_AverageWavelength");
        static readonly int sp_RespectShallowWaterAttenuation = Shader.PropertyToID("_RespectShallowWaterAttenuation");
        static readonly int sp_FeatherWaveStart = Shader.PropertyToID("_FeatherWaveStart");
        readonly int sp_AxisX = Shader.PropertyToID("_AxisX");

        /// <summary>
        /// Min wavelength for a cascade in the wave buffer. Does not depend on viewpoint.
        /// </summary>
        public float MinWavelength(int cascadeIdx)
        {
            var diameter = 0.5f * (1 << cascadeIdx);
            var texelSize = diameter / _resolution;
            // Nyquist rate
            return texelSize * 2f;
        }

        public void CrestUpdate(CommandBuffer buf)
        {
#if UNITY_EDITOR
            UpdateEditorOnly();
#endif

            var updateDataEachFrame = !_spectrumFixedAtRuntime;
#if UNITY_EDITOR
            if (!EditorApplication.isPlaying) updateDataEachFrame = true;
#endif
            // Ensure batches assigned to correct slots
            if (_firstUpdate || updateDataEachFrame)
            {
                InitBatches();

                _firstUpdate = false;
            }

            _matGenerateWaves.SetFloat(sp_RespectShallowWaterAttenuation, _respectShallowWaterAttenuation);
            _matGenerateWaves.SetFloat(sp_FeatherWaveStart, _featherWaveStart);

            // If using geo, the primary wave dir is used by the input shader to rotate the waves relative
            // to the geo rotation. If not, the wind direction is already used in the FFT gen.
            var waveDir = _meshForDrawingWaves != null ? PrimaryWaveDirection : Vector2.right;
            _matGenerateWaves.SetVector(sp_AxisX, waveDir);

            // If geometry is being used, the ocean input shader will rotate the waves to align to geo
            var windDirRad = WindDirRadForFFT;
            var windSpeedMPS = WindSpeedForFFT;

            // Don't create tons of generators when values are varying. Notify so that existing generators may be adapted.
            if (_windTurbulenceOld != _windTurbulence || _windDirRadOld != windDirRad || _windSpeedOld != windSpeedMPS || _spectrumOld != _spectrum)
            {
                FFTCompute.OnGenerationDataUpdated(_resolution, _windTurbulenceOld, _windDirRadOld, _windSpeedOld, _spectrumOld, _windTurbulence, windDirRad, windSpeedMPS, _spectrum);
            }

            var waveData = FFTCompute.GenerateDisplacements(buf, _resolution, _windTurbulence, windDirRad, windSpeedMPS, OceanRenderer.Instance.CurrentTime, _activeSpectrum, updateDataEachFrame);

            _windTurbulenceOld = _windTurbulence;
            _windDirRadOld = windDirRad;
            _windSpeedOld = windSpeedMPS;
            _spectrumOld = _spectrum;
            _matGenerateWaves.SetTexture(sp_WaveBuffer, waveData);

            ReportMaxDisplacement();
        }

#if UNITY_EDITOR
        void UpdateEditorOnly()
        {
            if (_spectrum != null)
            {
                _activeSpectrum = _spectrum;
            }

            if (_activeSpectrum == null)
            {
                _activeSpectrum = ScriptableObject.CreateInstance<OceanWaveSpectrum>();
                _activeSpectrum.name = "Default Waves (auto)";
            }

            // Unassign mesh
            if (_meshForDrawingWaves != null && !TryGetComponent<Spline.Spline>(out _))
            {
                _meshForDrawingWaves = null;
            }
        }
#endif

        void ReportMaxDisplacement()
        {
            OceanRenderer.Instance.ReportMaxDisplacementFromShape(_maxHorizontalDisplacement, _maxVerticalDisplacement, _maxVerticalDisplacement);
        }

        void InitBatches()
        {
            var registered = RegisterLodDataInputBase.GetRegistrar(typeof(LodDataMgrAnimWaves));

            if (_batches != null)
            {
                foreach (var batch in _batches)
                {
                    registered.Remove(batch);
                }
            }

            if (TryGetComponent<Spline.Spline>(out var splineForWaves))
            {
                var radius = _overrideSplineSettings ? _radius : splineForWaves.Radius;
                var subdivs = _overrideSplineSettings ? _subdivisions : splineForWaves.Subdivisions;
                var smooth = _overrideSplineSettings ? _smoothingIterations : splineForWaves.SmoothingIterations;
                if (ShapeGerstnerSplineHandling.GenerateMeshFromSpline(splineForWaves, transform, subdivs, radius, smooth, Vector2.one, ref _meshForDrawingWaves))
                {
                    _meshForDrawingWaves.name = gameObject.name + "_mesh";
                }
            }

            if (_meshForDrawingWaves == null)
            {
                _matGenerateWaves = new Material(Shader.Find("Hidden/Crest/Inputs/Animated Waves/Gerstner Global"));
            }
            else
            {
                _matGenerateWaves = new Material(Shader.Find("Crest/Inputs/Animated Waves/Gerstner Geometry"));
            }

            // Submit draws to create the FFT waves
            _batches = new FFTBatch[CASCADE_COUNT];
            for (int i = 0; i < CASCADE_COUNT; i++)
            {
                if (i == -1) break;
                _batches[i] = new FFTBatch(this, MinWavelength(i), i, _matGenerateWaves, _meshForDrawingWaves);
                registered.Add(0, _batches[i]);
            }
        }

        private void OnEnable()
        {
            _firstUpdate = true;

            // Initialise with spectrum
            if (_spectrum != null)
            {
                _activeSpectrum = _spectrum;
            }

            if (_activeSpectrum == null)
            {
                _activeSpectrum = ScriptableObject.CreateInstance<OceanWaveSpectrum>();
                _activeSpectrum.name = "Default Waves (auto)";
            }

#if UNITY_EDITOR
            if (EditorApplication.isPlaying && !Validate(OceanRenderer.Instance, ValidatedHelper.DebugLog))
            {
                enabled = false;
                return;
            }
#endif

            LodDataMgrAnimWaves.RegisterUpdatable(this);

            // TODO this is probably only useful for testing, and actually wants to be kicked off via a button/UI
            // in the SimSettingsAnimWaves, or from a separate editor window or such
            if (_UseThisFFTForHeightQueries)
            {
                FFTBaker.Bake(this, 256, 32, 16, 16);
            }
        }

        void OnDisable()
        {
            LodDataMgrAnimWaves.DeregisterUpdatable(this);

            if (_batches != null)
            {
                var registered = RegisterLodDataInputBase.GetRegistrar(typeof(LodDataMgrAnimWaves));
                foreach (var batch in _batches)
                {
                    registered.Remove(batch);
                }

                _batches = null;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            DrawMesh();
        }

        void DrawMesh()
        {
            if (_meshForDrawingWaves != null)
            {
                Gizmos.color = RegisterAnimWavesInput.s_gizmoColor;
                Gizmos.DrawWireMesh(_meshForDrawingWaves, 0, transform.position, transform.rotation, transform.lossyScale);
            }
        }

        void OnGUI()
        {
            if (_debugDrawSlicesInEditor)
            {
                FFTCompute.OnGUI(_resolution, _windTurbulence, WindDirRadForFFT, WindSpeedForFFT, _activeSpectrum);
            }
        }

        public void OnSplinePointDrawGizmosSelected(SplinePoint point)
        {
            DrawMesh();
        }
#endif
    }

#if UNITY_EDITOR
    public partial class ShapeFFT : IValidated
    {
        public bool Validate(OceanRenderer ocean, ValidatedHelper.ShowMessage showMessage)
        {
            var isValid = true;

            if (TryGetComponent<Spline.Spline>(out var spline) && !spline.Validate(ocean, ValidatedHelper.Suppressed))
            {
                showMessage
                (
                    "A <i>Spline</i> component is attached but it has validation errors.",
                    "Check this component in the Inspector for issues.",
                    ValidatedHelper.MessageType.Error, this
                );
            }

            return isValid;
        }
    }

    // Here for the help boxes
    [CustomEditor(typeof(ShapeFFT))]
    public class ShapeFFTEditor : ValidatedEditor { }
#endif
}
