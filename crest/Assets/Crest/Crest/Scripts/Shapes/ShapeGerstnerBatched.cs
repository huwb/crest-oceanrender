// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Crest
{
    /// <summary>
    /// Support script for Gerstner wave ocean shapes.
    /// Generates a number of batches of Gerstner waves.
    /// </summary>
    [ExecuteAlways]
    public partial class ShapeGerstnerBatched : MonoBehaviour, ICollProvider, IFloatingOrigin
    {
        public enum GerstnerMode
        {
            Global,
            Geometry,
        }

        [Tooltip("If set to 'Global', waves will render everywhere. If set to 'Geometry', the geometry on this GameObject will be rendered from a top down perspective to generate the waves. This allows having local wave conditions by placing Quad geometry where desired. The geometry must have one of the Gerstner shaders on it such as 'Crest/Inputs/Animated Waves/Gerstner Batch Geometry'.")]
        public GerstnerMode _mode = GerstnerMode.Global;

        [Tooltip("The spectrum that defines the ocean surface shape. Create asset of type Crest/Ocean Waves Spectrum.")]
        public OceanWaveSpectrum _spectrum;

        [Tooltip("Wind direction (angle from x axis in degrees)"), Range(-180, 180)]
        public float _windDirectionAngle = 0f;
        public Vector2 WindDir => new Vector2(Mathf.Cos(Mathf.PI * _windDirectionAngle / 180f), Mathf.Sin(Mathf.PI * _windDirectionAngle / 180f));

        public class GerstnerBatch : ILodDataInput
        {
            public GerstnerBatch(ShapeGerstnerBatched gerstner, int batchIndex, MeshRenderer rend, bool directTowardsPoint, bool sdfShorelines)
            {
                _gerstner = gerstner;
                _batchIndex = batchIndex;

                _materials = new PropertyWrapperMaterial[]
                {
                    new PropertyWrapperMaterial(new Material(rend.sharedMaterial ?? rend.material)),
                    new PropertyWrapperMaterial(new Material(rend.sharedMaterial ?? rend.material))
                };

                if (directTowardsPoint)
                {
                    _materials[0].material.EnableKeyword(DIRECT_TOWARDS_POINT_KEYWORD);
                    _materials[1].material.EnableKeyword(DIRECT_TOWARDS_POINT_KEYWORD);
                }

                if (sdfShorelines)
                {
                    _materials[0].material.EnableKeyword(SFD_SHORELINES_KEYWORD);
                    _materials[1].material.EnableKeyword(SFD_SHORELINES_KEYWORD);
                }

                _rend = rend;

                // Enabled stays true, because we don't sort the waves into buckets until Draw time, so we don't know if something should
                // be drawn in advance.
                Enabled = true;
            }

            public PropertyWrapperMaterial GetMaterial(int isTransition) => _materials[isTransition];

            // Two materials because as batch may be rendered twice if it has large wavelengths that are being transitioned back
            // and forth across the last 2 LODs.
            PropertyWrapperMaterial[] _materials;

            MeshRenderer _rend;

            ShapeGerstnerBatched _gerstner;
            int _batchIndex = -1;

            // The ocean input system uses this to decide which lod this batch belongs in
            public float Wavelength => OceanRenderer.Instance._lodTransform.MaxWavelength(_batchIndex) / 2f;

            public bool Enabled { get; set; }

            public bool HasWaves { get; set; }

            public void Draw(CommandBuffer buf, float weight, int isTransition, int lodIdx)
            {
                HasWaves = false;
                _gerstner.UpdateBatch(this, _batchIndex);

                if (HasWaves && weight > 0f)
                {
                    PropertyWrapperMaterial mat = GetMaterial(isTransition);
                    mat.SetFloat(RegisterLodDataInputBase.sp_Weight, weight);
                    buf.DrawRenderer(_rend, mat.material);
                }
            }
        }

        GerstnerBatch[] _batches = null;

        [Delayed, Tooltip("How many wave components to generate in each octave.")]
        public int _componentsPerOctave = 8;

        [Range(0f, 1f)]
        public float _weight = 1f;

        public int _randomSeed = 0;

        // Data for all components
        [Header("Wave data (usually populated at runtime)")]
        public bool _evaluateSpectrumAtRuntime = true;
        [PredicatedField("_evaluateSpectrumAtRuntime", true)]
        public float[] _wavelengths;
        [PredicatedField("_evaluateSpectrumAtRuntime", true)]
        public float[] _amplitudes;
        [PredicatedField("_evaluateSpectrumAtRuntime", true)]
        public float[] _angleDegs;
        [PredicatedField("_evaluateSpectrumAtRuntime", true)]
        public float[] _phases;

        [Header("Shorelines (Experimental)")]
        [Tooltip("Generate waves that are directed towards shorelines using a generated signed distance field. Must be set at edit time only, applied on startup. You must enable signed distance fields on the generated depth caches for terrain in order for this to work.")]
        public bool _sdfShorelines = false;
        [PredicatedField("_sdfShorelines")]
        public float _shorelineWavelengthNear = 2.5f;
        [PredicatedField("_sdfShorelines")]
        public float _shorelineWavelengthFar = 15f;
        [PredicatedField("_sdfShorelines")]
        public float _shorelineLerpDistance = 100.0f;
        [PredicatedField("_sdfShorelines")]
        public float _shorelineShorelineWavePeriod = 1.7f;
        [PredicatedField("_sdfShorelines")]
        public float _shorelineAmplitude = 0.3f;
        [PredicatedField("_sdfShorelines")]
        public float _shorelineChop = 2.0f;
        [PredicatedField("_sdfShorelines")]
        public uint _shorelineWavePeriodSeparation = 1;

        [SerializeField, Tooltip("Make waves converge towards a point. Must be set at edit time only, applied on startup."), Header("Direct towards point")]
        bool _directTowardsPoint = false;
        [SerializeField, Tooltip("Target point XZ to converge to.")]
        Vector2 _pointPositionXZ = Vector2.zero;
        [SerializeField, Tooltip("Inner and outer radii. Influence at full strength at inner radius, fades off at outer radius.")]
        Vector2 _pointRadii = new Vector2(100f, 200f);

        const string DIRECT_TOWARDS_POINT_KEYWORD = "CREST_DIRECT_TOWARDS_POINT_INTERNAL";
        const string SFD_SHORELINES_KEYWORD = "CREST_SDF_SHORELINES";

        readonly int sp_TwoPiOverWavelengths = Shader.PropertyToID("_TwoPiOverWavelengths");
        readonly int sp_Amplitudes = Shader.PropertyToID("_Amplitudes");
        readonly int sp_WaveDirX = Shader.PropertyToID("_WaveDirX");
        readonly int sp_WaveDirZ = Shader.PropertyToID("_WaveDirZ");
        readonly int sp_Phases = Shader.PropertyToID("_Phases");
        readonly int sp_ChopAmps = Shader.PropertyToID("_ChopAmps");
        readonly int sp_NumInBatch = Shader.PropertyToID("_NumInBatch");
        readonly int sp_AttenuationInShallows = Shader.PropertyToID("_AttenuationInShallows");
        readonly int sp_NumWaveVecs = Shader.PropertyToID("_NumWaveVecs");
        readonly int sp_TargetPointData = Shader.PropertyToID("_TargetPointData");

        readonly int sp_ShorelineTwoPiOverWavelengthNear = Shader.PropertyToID("_ShorelineTwoPiOverWavelengthNear");
        readonly int sp_ShorelineTwoPiOverWavelengthFar = Shader.PropertyToID("_ShorelineTwoPiOverWavelengthFar");
        readonly int sp_ShorelineLerpDistance = Shader.PropertyToID("_ShorelineLerpDistance");
        readonly int sp_ShorelineTwoPiOverWavePeriod = Shader.PropertyToID("_ShorelineTwoPiOverWavePeriod");
        readonly int sp_ShorelineAmplitude = Shader.PropertyToID("_ShorelineAmplitude");
        readonly int sp_ShorelineChop = Shader.PropertyToID("_ShorelineChop");
        readonly int sp_ShorelineWavePeriodSeparation = Shader.PropertyToID("_ShorelineWavePeriodSeparation");

        // IMPORTANT - this mirrors the constant with the same name in ShapeGerstnerBatch.shader, both must be updated together!
        const int BATCH_SIZE = 32;

        GameObject _renderProxy;

        // scratch data used by batching code
        struct UpdateBatchScratchData
        {
            public readonly static Vector4[] _twoPiOverWavelengthsBatch = new Vector4[BATCH_SIZE / 4];
            public readonly static Vector4[] _ampsBatch = new Vector4[BATCH_SIZE / 4];
            public readonly static Vector4[] _waveDirXBatch = new Vector4[BATCH_SIZE / 4];
            public readonly static Vector4[] _waveDirZBatch = new Vector4[BATCH_SIZE / 4];
            public readonly static Vector4[] _phasesBatch = new Vector4[BATCH_SIZE / 4];
            public readonly static Vector4[] _chopAmpsBatch = new Vector4[BATCH_SIZE / 4];
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            // Initialise with spectrum
            if (_spectrum == null)
            {
                _spectrum = ScriptableObject.CreateInstance<OceanWaveSpectrum>();
                _spectrum.name = "Default Waves (auto)";
            }

            if (EditorApplication.isPlaying && !Validate(OceanRenderer.Instance, ValidatedHelper.DebugLog))
            {
                enabled = false;
                return;
            }

            _spectrum.Upgrade();
#endif

            InitBatches();
        }

#if UNITY_EDITOR
        void Update()
        {
            // We are using the render proxy to hold state since we need to anyway.
            if (_renderProxy != null ? _mode == GerstnerMode.Geometry : _mode == GerstnerMode.Global)
            {
                InitBatches();
            }
        }
#endif

        void InitPhases()
        {
            // Set random seed to get repeatable results
            Random.State randomStateBkp = Random.state;
            Random.InitState(_randomSeed);

            var totalComps = _componentsPerOctave * OceanWaveSpectrum.NUM_OCTAVES;
            _phases = new float[totalComps];
            for (var octave = 0; octave < OceanWaveSpectrum.NUM_OCTAVES; octave++)
            {
                for (var i = 0; i < _componentsPerOctave; i++)
                {
                    var index = octave * _componentsPerOctave + i;
                    var rnd = (i + Random.value) / _componentsPerOctave;
                    _phases[index] = 2f * Mathf.PI * rnd;
                }
            }

            Random.state = randomStateBkp;
        }

        public void SetOrigin(Vector3 newOrigin)
        {
            if (_phases == null) return;

            var windAngle = _windDirectionAngle;
            for (int i = 0; i < _phases.Length; i++)
            {
                var direction = new Vector3(Mathf.Cos((windAngle + _angleDegs[i]) * Mathf.Deg2Rad), 0f, Mathf.Sin((windAngle + _angleDegs[i]) * Mathf.Deg2Rad));
                var phaseOffsetMeters = Vector3.Dot(newOrigin, direction);

                // wave number
                var k = 2f * Mathf.PI / _wavelengths[i];

                _phases[i] = Mathf.Repeat(_phases[i] + phaseOffsetMeters * k, Mathf.PI * 2f);
            }
        }

        int _lastFrameForUpdateData = -1;

        void UpdateData()
        {
            if (OceanRenderer.Instance == null) return;

            // We only want this to be executed once per frame.
            if (_lastFrameForUpdateData == OceanRenderer.FrameCount) return;
            _lastFrameForUpdateData = OceanRenderer.FrameCount;

            if (_evaluateSpectrumAtRuntime)
            {
                UpdateWaveData();
            }

            ReportMaxDisplacement();
        }

        public void UpdateWaveData()
        {
            // Set random seed to get repeatable results
            Random.State randomStateBkp = Random.state;
            Random.InitState(_randomSeed);

            _spectrum.GenerateWaveData(_componentsPerOctave, ref _wavelengths, ref _angleDegs);

            UpdateAmplitudes();

            // Won't run every time so put last in the random sequence
            if (_phases == null || _phases.Length != _wavelengths.Length)
            {
                InitPhases();
            }

            Random.state = randomStateBkp;
        }

        void UpdateAmplitudes()
        {
            if (_amplitudes == null || _amplitudes.Length != _wavelengths.Length)
            {
                _amplitudes = new float[_wavelengths.Length];
            }

            for (int i = 0; i < _wavelengths.Length; i++)
            {
                _amplitudes[i] = _weight * _spectrum.GetAmplitude(_wavelengths[i], _componentsPerOctave);
            }
        }

        private void ReportMaxDisplacement()
        {
            if (_spectrum._chopScales.Length != OceanWaveSpectrum.NUM_OCTAVES)
            {
                Debug.LogError($"OceanWaveSpectrum {_spectrum.name} is out of date, please open this asset and resave in editor.", _spectrum);
            }

            float ampSum = 0f;
            for (int i = 0; i < _wavelengths.Length; i++)
            {
                ampSum += _amplitudes[i] * _spectrum._chopScales[i / _componentsPerOctave];
            }
            OceanRenderer.Instance.ReportMaxDisplacementFromShape(ampSum * _spectrum._chop, ampSum, ampSum);
        }

        void InitBatches()
        {
            // Get the wave
            MeshRenderer rend = GetComponent<MeshRenderer>();
            if (_mode == GerstnerMode.Geometry)
            {
                rend.enabled = false;
#if UNITY_EDITOR
                // Cleanup render proxy used for global mode after switching.
                if (_renderProxy != null)
                {
                    DestroyImmediate(_renderProxy);
                }
#endif
            }
            else if (_mode == GerstnerMode.Global)
            {
                // Create render proxy only if we don't already have one.
                if (_renderProxy == null)
                {
                    // Create a proxy MeshRenderer to feed the rendering
                    _renderProxy = GameObject.CreatePrimitive(PrimitiveType.Quad);
#if UNITY_EDITOR
                    DestroyImmediate(_renderProxy.GetComponent<Collider>());
#else
                    Destroy(_renderProxy.GetComponent<Collider>());
#endif
                    _renderProxy.hideFlags = HideFlags.HideAndDontSave;
                    _renderProxy.transform.parent = transform;
                    rend = _renderProxy.GetComponent<MeshRenderer>();
                    rend.enabled = false;
                    var waveShader = Shader.Find("Hidden/Crest/Inputs/Animated Waves/Gerstner Batch Global");
                    Debug.Assert(waveShader, "Could not load Gerstner wave shader, make sure it is packaged in the build.");
                    if (waveShader == null)
                    {
                        enabled = false;
                        return;
                    }

                    rend.material = new Material(waveShader);
                }
                else
                {
                    rend = _renderProxy.GetComponent<MeshRenderer>();
                }
            }

            var registered = RegisterLodDataInputBase.GetRegistrar(typeof(LodDataMgrAnimWaves));

#if UNITY_EDITOR
            // Unregister after switching modes in the editor.
            if (_batches != null)
            {
                foreach (var batch in _batches)
                {
                    registered.Remove(batch);
                }
            }
#endif

            _batches = new GerstnerBatch[LodDataMgr.MAX_LOD_COUNT];
            for (int i = 0; i < _batches.Length; i++)
            {
                _batches[i] = new GerstnerBatch(this, i, rend, _directTowardsPoint, _sdfShorelines);
            }

            // Submit draws to create the Gerstner waves. LODs from 0 to N-2 render the Gerstner waves from their lod. Additionally, any waves
            // in the biggest lod, or too big for the biggest lod, are rendered into both of the last two LODs N-1 and N-2, as this allows us to
            // move these waves between LODs without pops when the camera changes heights and the LODs need to change scale.

            foreach (var batch in _batches)
            {
                registered.Add(0, batch);
            }
        }

        /// <summary>
        /// Computes Gerstner params for a set of waves, for the given lod idx. Writes shader data to the given property.
        /// Returns number of wave components rendered in this batch.
        /// </summary>
        void UpdateBatch(int lodIdx, int firstComponent, int lastComponentNonInc, GerstnerBatch batch)
        {
            batch.HasWaves = false;

            int numComponents = lastComponentNonInc - firstComponent;
            int numInBatch = 0;
            int dropped = 0;

            float twopi = 2f * Mathf.PI;
            float one_over_2pi = 1f / twopi;

            // register any nonzero components
            for (int i = 0; i < numComponents; i++)
            {
                float wl = _wavelengths[firstComponent + i];

                // compute amp - contains logic for shifting wave components between last two LODs...
                float amp = _amplitudes[firstComponent + i];

                if (amp >= 0.001f)
                {
                    if (numInBatch < BATCH_SIZE)
                    {
                        int vi = numInBatch / 4;
                        int ei = numInBatch - vi * 4;

                        UpdateBatchScratchData._twoPiOverWavelengthsBatch[vi][ei] = 2f * Mathf.PI / wl;
                        UpdateBatchScratchData._ampsBatch[vi][ei] = amp;

                        float chopScale = _spectrum._chopScales[(firstComponent + i) / _componentsPerOctave];
                        UpdateBatchScratchData._chopAmpsBatch[vi][ei] = -chopScale * _spectrum._chop * amp;

                        float angle = Mathf.Deg2Rad * (_windDirectionAngle + _angleDegs[firstComponent + i]);
                        UpdateBatchScratchData._waveDirXBatch[vi][ei] = Mathf.Cos(angle);
                        UpdateBatchScratchData._waveDirZBatch[vi][ei] = Mathf.Sin(angle);

                        // It used to be this, but I'm pushing all the stuff that doesn't depend on position into the phase.
                        //half4 angle = k * (C * _CrestTime + x) + _Phases[vi];
                        float gravityScale = _spectrum._gravityScales[(firstComponent + i) / _componentsPerOctave];
                        float gravity = OceanRenderer.Instance.Gravity * _spectrum._gravityScale;
                        float C = Mathf.Sqrt(wl * gravity * gravityScale * one_over_2pi);
                        float k = twopi / wl;
                        // Repeat every 2pi to keep angle bounded - helps precision on 16bit platforms
                        UpdateBatchScratchData._phasesBatch[vi][ei] = Mathf.Repeat(_phases[firstComponent + i] + k * C * OceanRenderer.Instance.CurrentTime, Mathf.PI * 2f);

                        numInBatch++;
                    }
                    else
                    {
                        dropped++;
                    }
                }
            }

            if (dropped > 0)
            {
                Debug.LogWarning(string.Format("Gerstner LOD{0}: Batch limit reached, dropped {1} wavelengths. To support bigger batch sizes, see the comment around the BATCH_SIZE declaration.", lodIdx, dropped), this);
                numComponents = BATCH_SIZE;
            }

            if (numInBatch == 0)
            {
                // no waves to draw - abort
                return;
            }

            // if we did not fill the batch, put a terminator signal after the last position
            if (numInBatch < BATCH_SIZE)
            {
                int vi_last = numInBatch / 4;
                int ei_last = numInBatch - vi_last * 4;

                for (int vi = vi_last; vi < BATCH_SIZE / 4; vi++)
                {
                    for (int ei = ei_last; ei < 4; ei++)
                    {
                        UpdateBatchScratchData._twoPiOverWavelengthsBatch[vi][ei] = 1f; // wary of NaNs
                        UpdateBatchScratchData._ampsBatch[vi][ei] = 0f;
                        UpdateBatchScratchData._waveDirXBatch[vi][ei] = 0f;
                        UpdateBatchScratchData._waveDirZBatch[vi][ei] = 0f;
                        UpdateBatchScratchData._phasesBatch[vi][ei] = 0f;
                        UpdateBatchScratchData._chopAmpsBatch[vi][ei] = 0f;
                    }

                    ei_last = 0;
                }
            }

            // apply the data to the shape property
            for (int i = 0; i < 2; i++)
            {
                var mat = batch.GetMaterial(i);
                mat.SetVectorArray(sp_TwoPiOverWavelengths, UpdateBatchScratchData._twoPiOverWavelengthsBatch);
                mat.SetVectorArray(sp_Amplitudes, UpdateBatchScratchData._ampsBatch);
                mat.SetVectorArray(sp_WaveDirX, UpdateBatchScratchData._waveDirXBatch);
                mat.SetVectorArray(sp_WaveDirZ, UpdateBatchScratchData._waveDirZBatch);
                mat.SetVectorArray(sp_Phases, UpdateBatchScratchData._phasesBatch);
                mat.SetVectorArray(sp_ChopAmps, UpdateBatchScratchData._chopAmpsBatch);
                mat.SetFloat(sp_NumInBatch, numInBatch);
                mat.SetFloat(sp_AttenuationInShallows, OceanRenderer.Instance._lodDataAnimWaves.Settings.AttenuationInShallows);

                int numVecs = (numInBatch + 3) / 4;
                mat.SetInt(sp_NumWaveVecs, numVecs);
                mat.SetInt(LodDataMgr.sp_LD_SliceIndex, lodIdx - i);
                OceanRenderer.Instance._lodDataAnimWaves.BindResultData(mat);

                if (OceanRenderer.Instance._lodDataSeaDepths != null)
                {
                    OceanRenderer.Instance._lodDataSeaDepths.BindResultData(mat, false);
                }
                else
                {
                    LodDataMgrSeaFloorDepth.BindNull(mat, false);
                }

                if (_directTowardsPoint)
                {
                    mat.SetVector(sp_TargetPointData, new Vector4(_pointPositionXZ.x, _pointPositionXZ.y, _pointRadii.x, _pointRadii.y));
                }
            }

            batch.HasWaves = true;
        }

        void UpdateBatch(GerstnerBatch batch, int batchIdx)
        {
#if UNITY_EDITOR
            if (_spectrum == null) return;
#endif

            // Default to disabling all batches
            batch.HasWaves = false;

            if (OceanRenderer.Instance == null)
            {
                return;
            }

            UpdateData();

            if (_wavelengths.Length == 0)
            {
                return;
            }

            int lodIdx = Mathf.Min(batchIdx, OceanRenderer.Instance.CurrentLodCount - 1);

            int componentIdx = 0;

            // seek forward to first wavelength that is big enough to render into current LODs
            float minWl = OceanRenderer.Instance._lodTransform.MaxWavelength(batchIdx) / 2f;
            while (componentIdx < _wavelengths.Length && _wavelengths[componentIdx] < minWl)
            {
                componentIdx++;
            }

            // Assemble wavelengths into current batch
            int startCompIdx = componentIdx;
            while (componentIdx < _wavelengths.Length && _wavelengths[componentIdx] < 2f * minWl)
            {
                componentIdx++;
            }

            // One or more wavelengths - update the batch
            if (componentIdx > startCompIdx)
            {
                //Debug.Log($"Batch {batch}, lodIdx {lodIdx}, range: {minWl} -> {2f * minWl}, indices: {startCompIdx} -> {componentIdx}");
                UpdateBatch(lodIdx, startCompIdx, componentIdx, batch);
            }

            if (_sdfShorelines)
            {
                // apply the data to the shape property
                for (int i = 0; i < 2; i++)
                {
                    var mat = batch.GetMaterial(i);
                    mat.SetFloat(sp_ShorelineTwoPiOverWavelengthNear, (Mathf.PI * 2.0f) / _shorelineWavelengthNear);
                    mat.SetFloat(sp_ShorelineTwoPiOverWavelengthFar, (Mathf.Sqrt(_shorelineLerpDistance) * Mathf.PI * 2.0f) / _shorelineWavelengthFar);
                    mat.SetFloat(sp_ShorelineLerpDistance, _shorelineLerpDistance);
                    mat.SetFloat(sp_ShorelineTwoPiOverWavePeriod, (Mathf.PI * 2.0f) / _shorelineShorelineWavePeriod);
                    mat.SetFloat(sp_ShorelineAmplitude, _shorelineAmplitude);
                    mat.SetFloat(sp_ShorelineChop, _shorelineChop);
                    mat.SetInt(sp_ShorelineWavePeriodSeparation, (int)_shorelineWavePeriodSeparation);
                }
            }
        }

        void OnDisable()
        {
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
            var mf = GetComponent<MeshFilter>();
            if (mf)
            {
                Gizmos.color = RegisterAnimWavesInput.s_gizmoColor;
                Gizmos.DrawWireMesh(mf.sharedMesh, transform.position, transform.rotation, transform.lossyScale);
            }

            if (_directTowardsPoint)
            {
                Gizmos.color = Color.black;
                Gizmos.DrawWireSphere(new Vector3(_pointPositionXZ.x, transform.position.y, _pointPositionXZ.y), _pointRadii.y);
                Gizmos.color = Color.white;
                Gizmos.DrawWireSphere(new Vector3(_pointPositionXZ.x, transform.position.y, _pointPositionXZ.y), _pointRadii.x);
            }
        }
#endif

        float ComputeWaveSpeed(float wavelength/*, float depth*/)
        {
            // wave speed of deep sea ocean waves: https://en.wikipedia.org/wiki/Wind_wave
            // https://en.wikipedia.org/wiki/Dispersion_(water_waves)#Wave_propagation_and_dispersion
            float g = 9.81f;
            float k = 2f * Mathf.PI / wavelength;
            //float h = max(depth, 0.01);
            //float cp = sqrt(abs(tanh_clamped(h * k)) * g / k);
            float cp = Mathf.Sqrt(g / k);
            return cp;
        }

        public bool GetSurfaceVelocity(ref Vector3 i_worldPos, float i_minSpatialLength, out Vector3 o_surfaceVel)
        {
            o_surfaceVel = Vector3.zero;

            if (_amplitudes == null) return false;

            Vector2 pos = new Vector2(i_worldPos.x, i_worldPos.z);
            float mytime = OceanRenderer.Instance.CurrentTime;
            float windAngle = _windDirectionAngle;
            float minWaveLength = i_minSpatialLength / 2f;

            for (int j = 0; j < _amplitudes.Length; j++)
            {
                if (_amplitudes[j] <= 0.001f) continue;
                if (_wavelengths[j] < minWaveLength) continue;

                float C = ComputeWaveSpeed(_wavelengths[j]);

                // direction
                Vector2 D = new Vector2(Mathf.Cos((windAngle + _angleDegs[j]) * Mathf.Deg2Rad), Mathf.Sin((windAngle + _angleDegs[j]) * Mathf.Deg2Rad));
                // wave number
                float k = 2f * Mathf.PI / _wavelengths[j];

                float x = Vector2.Dot(D, pos);
                float t = k * (x + C * mytime) + _phases[j];
                float disp = -_spectrum._chop * k * C * Mathf.Cos(t);
                o_surfaceVel += _amplitudes[j] * new Vector3(
                    D.x * disp,
                    -k * C * Mathf.Sin(t),
                    D.y * disp
                    );
            }

            return true;
        }

        public bool SampleHeight(ref Vector3 i_worldPos, float i_minSpatialLength, out float o_height)
        {
            o_height = 0f;

            Vector3 posFlatland = i_worldPos;
            posFlatland.y = OceanRenderer.Instance.Root.position.y;

            Vector3 undisplacedPos;
            if (!ComputeUndisplacedPosition(ref posFlatland, i_minSpatialLength, out undisplacedPos))
                return false;

            Vector3 disp;
            if (!SampleDisplacement(ref undisplacedPos, i_minSpatialLength, out disp))
                return false;

            o_height = posFlatland.y + disp.y;

            return true;
        }

        public bool ComputeUndisplacedPosition(ref Vector3 i_worldPos, float i_minSpatialLength, out Vector3 o_undisplacedWorldPos)
        {
            // FPI - guess should converge to location that displaces to the target position
            Vector3 guess = i_worldPos;
            // 2 iterations was enough to get very close when chop = 1, added 2 more which should be
            // sufficient for most applications. for high chop values or really stormy conditions there may
            // be some error here. one could also terminate iteration based on the size of the error, this is
            // worth trying but is left as future work for now.
            Vector3 disp;
            for (int i = 0; i < 4 && SampleDisplacement(ref guess, i_minSpatialLength, out disp); i++)
            {
                Vector3 error = guess + disp - i_worldPos;
                guess.x -= error.x;
                guess.z -= error.z;
            }

            o_undisplacedWorldPos = guess;
            o_undisplacedWorldPos.y = OceanRenderer.Instance.SeaLevel;

            return true;
        }

        // Compute normal to a surface with a parameterization - equation 14 here: http://mathworld.wolfram.com/NormalVector.html
        public bool SampleNormal(ref Vector3 i_undisplacedWorldPos, float i_minSpatialLength, out Vector3 o_normal)
        {
            o_normal = Vector3.zero;

            if (_amplitudes == null) return false;

            var pos = new Vector2(i_undisplacedWorldPos.x, i_undisplacedWorldPos.z);
            float mytime = OceanRenderer.Instance.CurrentTime;
            float windAngle = _windDirectionAngle;
            float minWaveLength = i_minSpatialLength / 2f;

            // base rate of change of our displacement function in x and z is unit
            var delfdelx = Vector3.right;
            var delfdelz = Vector3.forward;

            for (int j = 0; j < _amplitudes.Length; j++)
            {
                if (_amplitudes[j] <= 0.001f) continue;
                if (_wavelengths[j] < minWaveLength) continue;

                float C = ComputeWaveSpeed(_wavelengths[j]);

                // direction
                var D = new Vector2(Mathf.Cos((windAngle + _angleDegs[j]) * Mathf.Deg2Rad), Mathf.Sin((windAngle + _angleDegs[j]) * Mathf.Deg2Rad));
                // wave number
                float k = 2f * Mathf.PI / _wavelengths[j];

                float x = Vector2.Dot(D, pos);
                float t = k * (x + C * mytime) + _phases[j];
                float disp = k * -_spectrum._chop * Mathf.Cos(t);
                float dispx = D.x * disp;
                float dispz = D.y * disp;
                float dispy = -k * Mathf.Sin(t);

                delfdelx += _amplitudes[j] * new Vector3(D.x * dispx, D.x * dispy, D.y * dispx);
                delfdelz += _amplitudes[j] * new Vector3(D.x * dispz, D.y * dispy, D.y * dispz);
            }

            o_normal = Vector3.Cross(delfdelz, delfdelx).normalized;

            return true;
        }

        public bool SampleDisplacement(ref Vector3 i_worldPos, float i_minSpatialLength, out Vector3 o_displacement)
        {
            o_displacement = Vector3.zero;

            if (_amplitudes == null)
            {
                return false;
            }

            Vector2 pos = new Vector2(i_worldPos.x, i_worldPos.z);
            float mytime = OceanRenderer.Instance.CurrentTime;
            float windAngle = _windDirectionAngle;
            float minWavelength = i_minSpatialLength / 2f;

            for (int j = 0; j < _amplitudes.Length; j++)
            {
                if (_amplitudes[j] <= 0.001f) continue;
                if (_wavelengths[j] < minWavelength) continue;

                float C = ComputeWaveSpeed(_wavelengths[j]);

                // direction
                Vector2 D = new Vector2(Mathf.Cos((windAngle + _angleDegs[j]) * Mathf.Deg2Rad), Mathf.Sin((windAngle + _angleDegs[j]) * Mathf.Deg2Rad));
                // wave number
                float k = 2f * Mathf.PI / _wavelengths[j];

                float x = Vector2.Dot(D, pos);
                float t = k * (x + C * mytime) + _phases[j];
                float disp = -_spectrum._chop * Mathf.Sin(t);
                o_displacement += _amplitudes[j] * new Vector3(
                    D.x * disp,
                    Mathf.Cos(t),
                    D.y * disp
                    );
            }

            return true;
        }

        public int Query(int i_ownerHash, float i_minSpatialLength, Vector3[] i_queryPoints, Vector3[] o_resultDisps, Vector3[] o_resultNorms, Vector3[] o_resultVels)
        {
            if (o_resultDisps != null)
            {
                for (int i = 0; i < o_resultDisps.Length; i++)
                {
                    SampleDisplacement(ref i_queryPoints[i], i_minSpatialLength, out o_resultDisps[i]);
                }
            }

            if (o_resultNorms != null)
            {
                for (int i = 0; i < o_resultNorms.Length; i++)
                {
                    Vector3 undispPos;
                    if (ComputeUndisplacedPosition(ref i_queryPoints[i], i_minSpatialLength, out undispPos))
                    {
                        SampleNormal(ref undispPos, i_minSpatialLength, out o_resultNorms[i]);
                    }
                    else
                    {
                        o_resultNorms[i] = Vector3.up;
                    }
                }
            }

            return 0;
        }

        public int Query(int i_ownerHash, float i_minSpatialLength, Vector3[] i_queryPoints, float[] o_resultHeights, Vector3[] o_resultNorms, Vector3[] o_resultVels)
        {
            if (o_resultHeights != null)
            {
                for (int i = 0; i < o_resultHeights.Length; i++)
                {
                    SampleHeight(ref i_queryPoints[i], i_minSpatialLength, out o_resultHeights[i]);
                }
            }

            if (o_resultNorms != null)
            {
                for (int i = 0; i < o_resultNorms.Length; i++)
                {
                    Vector3 undispPos;
                    if (ComputeUndisplacedPosition(ref i_queryPoints[i], i_minSpatialLength, out undispPos))
                    {
                        SampleNormal(ref undispPos, i_minSpatialLength, out o_resultNorms[i]);
                    }
                    else
                    {
                        o_resultNorms[i] = Vector3.up;
                    }
                }
            }

            if (o_resultVels != null)
            {
                for (int i = 0; i < o_resultVels.Length; i++)
                {
                    GetSurfaceVelocity(ref i_queryPoints[i], i_minSpatialLength, out o_resultVels[i]);
                }
            }

            return 0;
        }

        public bool RetrieveSucceeded(int queryStatus)
        {
            return queryStatus == 0;
        }

        public void UpdateQueries()
        {
        }

        public void CleanUp()
        {
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(ShapeGerstnerBatched)), CanEditMultipleObjects]
    public class ShapeGerstnerBatchedEditor : ValidatedEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var gerstner = target as ShapeGerstnerBatched;

            GUI.enabled = !EditorApplication.isPlaying || !gerstner._evaluateSpectrumAtRuntime;
            if (GUILayout.Button("Generate wave data from spectrum"))
            {
                if (gerstner._spectrum == null)
                {
                    Debug.LogError("A wave spectrum must be assigned in order to generate wave data.", gerstner);
                }
                else
                {
                    gerstner.UpdateWaveData();
                }
            }
            GUI.enabled = true;
        }
    }

    public partial class ShapeGerstnerBatched : IValidated
    {
        public bool Validate(OceanRenderer ocean, ValidatedHelper.ShowMessage showMessage)
        {
            var isValid = true;

            // Renderer
            if (_mode == GerstnerMode.Geometry)
            {
                isValid = ValidatedHelper.ValidateRenderer(gameObject, "Crest/Inputs/Animated Waves/Gerstner", showMessage);
            }
            else if (_mode == GerstnerMode.Global && GetComponent<MeshRenderer>() != null)
            {
                showMessage
                (
                    "The MeshRenderer component will be ignored because the Mode is set to Global.",
                    ValidatedHelper.MessageType.Warning, this
                );
            }

            if (_mode == GerstnerMode.Global && GetComponent<MeshRenderer>() != null)
            {
                showMessage
                (
                    "The MeshRenderer component will be ignored because the Mode is set to Global.",
                    ValidatedHelper.MessageType.Warning, this
                );

                isValid = false;
            }

            if (_spectrum == null)
            {
                showMessage
                (
                    "There is no spectrum assigned meaning this Gerstner component won't generate any waves.",
                    ValidatedHelper.MessageType.Warning, this
                );

                isValid = false;
            }

            if (_componentsPerOctave == 0)
            {
                showMessage
                (
                    "Components Per Octave set to 0 meaning this Gerstner component won't generate any waves.",
                    ValidatedHelper.MessageType.Warning, this
                );

                isValid = false;
            }

            // TODO(TRC):Now workout why the ocean can be null... :(
            if(ocean != null)
            {
                if(ocean._simSettingsDepth == null)
                {
                    showMessage
                    (
                        $"The ocean doesn't have any depth sim settings, please add them and enable signed distance fields to support shoreline waves.",
                        ValidatedHelper.MessageType.Warning, ocean
                    );

                }
                else if(!ocean._simSettingsDepth._enableSignedDistanceFields)
                {
                    showMessage
                    (
                        $"Please enable signed distance fields on sim settings depth to support shoreline waves.",
                        ValidatedHelper.MessageType.Warning, ocean._simSettingsDepth
                    );
                }
            }

            {
                OceanDepthCache[] oceanDepthCaches = FindObjectsOfType<OceanDepthCache>();
                if (_sdfShorelines)
                {
                    foreach (OceanDepthCache oceanDepthCache in oceanDepthCaches)
                    {
                        if (!oceanDepthCache._generateSDF)
                        {
                            showMessage
                            (
                                $"The ocean depth cache {oceanDepthCache.name} isn't configured to generate a signed distance field, which means shoreline waves will not be supported by it.",
                                ValidatedHelper.MessageType.Warning, oceanDepthCache
                            );
                        }
                    }
                }
                else
                {
                    foreach (OceanDepthCache oceanDepthCache in oceanDepthCaches)
                    {
                        if (oceanDepthCache._generateSDF)
                        {
                            showMessage
                            (
                                $"The ocean depth cache {oceanDepthCache.name} is configured to generate a signed distance field which is extra generated texture data that won't be used as shorelines are disabled here.",
                                ValidatedHelper.MessageType.Warning, oceanDepthCache
                            );
                        }
                    }

                }
            }

            return isValid;
        }
    }
#endif
}
