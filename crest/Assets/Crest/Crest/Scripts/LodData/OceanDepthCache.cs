// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

// This is the original version that uses an auxillary camera and works with Unity's GPU terrain - issue 152.

using System;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Crest
{
    /// <summary>
    /// Renders terrain height / ocean depth once into a render target to cache this off and avoid rendering it every frame.
    /// This should be used for static geometry, dynamic objects should be tagged with the Render Ocean Depth component.
    /// </summary>
    [ExecuteAlways]
    public partial class OceanDepthCache : MonoBehaviour
    {
        public enum OceanDepthCacheType
        {
            Realtime,
            Baked,
        }

        public enum OceanDepthCacheRefreshMode
        {
            OnStart,
            OnDemand,
        }

        [Tooltip("Realtime = cache will be dynamic in accordance to refresh mode, Baked = cache will use the provided texture."), SerializeField]
        OceanDepthCacheType _type = OceanDepthCacheType.Realtime;
        public OceanDepthCacheType Type => _type;

        [Tooltip("Ignored if baked. On Start = cache will populate in Start(), On Demand = call PopulateCache() manually via scripting."), SerializeField]
        OceanDepthCacheRefreshMode _refreshMode = OceanDepthCacheRefreshMode.OnStart;
        public OceanDepthCacheRefreshMode RefreshMode => _refreshMode;

        [Tooltip("Renderers in scene to render into this depth cache. When provided this saves the code from doing an expensive FindObjectsOfType() call. If one or more renderers are specified, the layer setting is ignored."), SerializeField]
        Renderer[] _geometryToRenderIntoCache = new Renderer[0];

        [Tooltip("The layers to render into the depth cache. This is ignored if geometry instances are specified in the Geometry To Render Into Cache field.")]
        public string[] _layerNames = new string[0];

        [Tooltip("The resolution of the cached depth - lower will be more efficient.")]
        public int _resolution = 512;

        // A big hill will still want to write its height into the depth texture
        [Tooltip("The 'near plane' for the depth cache camera (top down)."), SerializeField]
        float _cameraMaxTerrainHeight = 100f;

        [Tooltip("Will render into the cache every frame. Intended for debugging, will generate garbage."), SerializeField]
#pragma warning disable 414
        bool _forceAlwaysUpdateDebug = false;
#pragma warning restore 414

        [Tooltip("Hides the depth cache camera, for cleanliness. Disable to make it visible in the Hierarchy."), SerializeField]
        bool _hideDepthCacheCam = true;

        [Tooltip("Baked depth cache. Baking button available in play mode."), SerializeField]
#pragma warning disable 649
        Texture2D _savedCache;
#pragma warning restore 649
        public Texture2D SavedCache => _savedCache;

        [Tooltip("Check for any terrains that have the 'Draw Instanced' option enabled. Such instanced terrains will not populate into the depth cache and therefore will not contribute to shorelines and shallow water. This option must be disabled on the terrain when the depth cache is populated (but can be enabled afterwards)."), SerializeField]
#pragma warning disable 414
        bool _checkTerrainDrawInstancedOption = true;
#pragma warning restore 414

#pragma warning disable 414
        [Tooltip("Editor only: run validation checks on Start() to check for issues."), SerializeField]
        bool _runValidationOnStart = true;
#pragma warning restore 414

        [Header("Signed Distance Field (Experimental)")]
        [Tooltip("Generate a signed distance field (experimental)"), SerializeField]
        internal bool _generateSDF = false;

        [PredicatedField("_generateSDF")]
        [Tooltip("How many additional Jump Flood Algorithm rounds to use - (over the standard log2(Resolution)"), SerializeField]
        // additional rounds of jump flood can help reduce innacuracies from JFA, see paper for details.
        int _additionalJumpFloodRounds = 7;

        RenderTexture _depthCacheTexture;
        public RenderTexture CacheTexture => _depthCacheTexture;

        GameObject _drawDepthCacheQuad;
        Camera _depthCacheCamera;

        private readonly int sp_jumpSize = Shader.PropertyToID("_jumpSize");
        private readonly int sp_textureDimension = Shader.PropertyToID("_textureDimension");
        private readonly int sp_projectionToWorld = Shader.PropertyToID("_projectionToWorld");
        private readonly int sp_InitTexture = Shader.PropertyToID("_InitTexture");
        private readonly int sp_FromTexture = Shader.PropertyToID("_FromTexture");
        private readonly int sp_ToTexture = Shader.PropertyToID("_ToTexture");

        void Start()
        {
#if UNITY_EDITOR
            if (EditorApplication.isPlaying && _runValidationOnStart)
            {
                Validate(OceanRenderer.Instance, ValidatedHelper.DebugLog);
            }
#endif

            if (_type == OceanDepthCacheType.Baked)
            {
                DrawCacheQuad(ref _drawDepthCacheQuad, "DepthCache_", _type, _savedCache);
            }
            else if (_type == OceanDepthCacheType.Realtime && _refreshMode == OceanDepthCacheRefreshMode.OnStart)
            {
                PopulateCache();
            }
        }

#if UNITY_EDITOR
        void Update()
        {
            if (_forceAlwaysUpdateDebug)
            {
                PopulateCache();
            }
        }
#endif

        public void PopulateCache()
        {
            if (_type == OceanDepthCacheType.Baked)
                return;

            var layerMask = 0;
            var errorShown = false;
            foreach (var layer in _layerNames)
            {
                if (string.IsNullOrEmpty(layer))
                {
                    Debug.LogError("OceanDepthCache: An empty layer name was provided. Please provide a valid layer name. Click this message to highlight the cache in question.", this);
                    errorShown = true;
                    continue;
                }

                int layerIdx = LayerMask.NameToLayer(layer);
                if (layerIdx == -1)
                {
                    Debug.LogError("OceanDepthCache: Invalid layer specified: \"" + layer +
                        "\". Please add this layer to the project by putting the name in an empty layer slot in Edit/Project Settings/Tags and Layers. Click this message to highlight the cache in question.", this);

                    errorShown = true;
                }
                else
                {
                    layerMask = layerMask | (1 << layerIdx);
                }
            }

            if (layerMask == 0)
            {
                if (!errorShown)
                {
                    Debug.LogError("No valid layers for populating depth cache, aborting. Click this message to highlight the cache in question.", this);
                }

                return;
            }

#if UNITY_EDITOR
            if (_type == OceanDepthCacheType.Realtime && _checkTerrainDrawInstancedOption)
            {
                // This issue only affects the built-in render pipeline. Issue 158: https://github.com/crest-ocean/crest/issues/158

                var terrains = FindObjectsOfType<Terrain>();
                foreach (var terrain in terrains)
                {
                    var mask = (int)Mathf.Pow(2f, terrain.gameObject.layer);

                    if ((mask & layerMask) == 0) continue;

                    if (terrain.drawInstanced)
                    {
                        Debug.LogError($"Terrain {terrain.gameObject.name} has 'Draw Instanced' enabled. This terrain will not populate into the depth cache and therefore will not contribute to shorelines and shallow water. This option must be disabled on the terrain when the depth cache is populated (but can be enabled afterwards).", terrain);
                    }
                }
            }
#endif

            if (_depthCacheTexture == null)
            {
                RenderTextureFormat fmt;
                if (_generateSDF)
                {
                    fmt = RenderTextureFormat.RGHalf;
                }
                else
                {
#if UNITY_EDITOR_WIN
                    fmt = RenderTextureFormat.DefaultHDR;
#else
                    fmt = RenderTextureFormat.RHalf;
#endif
                }

                Debug.Assert(SystemInfo.SupportsRenderTextureFormat(fmt), "The graphics device does not support the render texture format " + fmt.ToString());
                _depthCacheTexture = new RenderTexture(_resolution, _resolution, 0);
                _depthCacheTexture.name = gameObject.name + "_oceanDepth";
                _depthCacheTexture.format = fmt;
                _depthCacheTexture.useMipMap = false;
                _depthCacheTexture.anisoLevel = 0;
                _depthCacheTexture.enableRandomWrite = _generateSDF;
                _depthCacheTexture.Create();
            }

            if (_depthCacheCamera == null)
            {
                _depthCacheCamera = GenerateCacheCamera(
                    layerMask,
                    _generateSDF ? "DepthSdfCam" : "DepthCacheCam",
                    _cameraMaxTerrainHeight,
                    transform,
                    _depthCacheTexture,
                    _hideDepthCacheCam
                );
            }


            // Make sure this global is set - I found this was necessary to set it here. However this can cause glitchiness in editor
            // as it messes with this global vector, so only do it if not in edit mode
#if UNITY_EDITOR
            if (EditorApplication.isPlaying)
#endif
            {
                // Shader needs sea level to determine water depth
                var centerPoint = Vector3.zero;
                if (OceanRenderer.Instance != null)
                {
                    centerPoint.y = OceanRenderer.Instance.Root.position.y;
                }
                else
                {
                    centerPoint.y = transform.position.y;
                }

                Shader.SetGlobalVector("_OceanCenterPosWorld", centerPoint);
            }

            _depthCacheCamera.RenderWithShader(Shader.Find("Crest/Inputs/Depth/Ocean Depth From Geometry"), null);

            if (_generateSDF)
            {
                RenderTextureFormat fmt = RenderTextureFormat.RGHalf;
                RenderTexture voronoiPingPongTexture0 = new RenderTexture(_resolution, _resolution, 0);
                voronoiPingPongTexture0.name = gameObject.name + "_voronoiPingPong0";
                voronoiPingPongTexture0.format = fmt;
                voronoiPingPongTexture0.useMipMap = false;
                voronoiPingPongTexture0.anisoLevel = 0;
                voronoiPingPongTexture0.enableRandomWrite = true;
                voronoiPingPongTexture0.Create();

                RenderTexture voronoiPingPongTexture1 = new RenderTexture(_resolution, _resolution, 0);
                voronoiPingPongTexture1.name = gameObject.name + "_voronoiPingPong1";
                voronoiPingPongTexture1.format = fmt;
                voronoiPingPongTexture1.useMipMap = false;
                voronoiPingPongTexture1.anisoLevel = 0;
                voronoiPingPongTexture1.enableRandomWrite = true;
                voronoiPingPongTexture1.Create();

                using (CommandBuffer jumpFloodCommandBuffer = new CommandBuffer())
                {
                    var cameraToWorldMatrix = _depthCacheCamera.cameraToWorldMatrix;
                    var projectionMatrix = _depthCacheCamera.projectionMatrix;
                    var projectionToWorldMatrix = cameraToWorldMatrix * projectionMatrix.inverse;
                    uint textureDimension = (uint)voronoiPingPongTexture0.width;
                    {
                        ComputeShader initJumpFloodShader = ComputeShaderHelpers.LoadShader("SdfInitJumpFlood");
                        int initJumpFloodKernel = initJumpFloodShader.FindKernel("SdfInitJumpFlood");
                        jumpFloodCommandBuffer.SetComputeTextureParam(initJumpFloodShader, initJumpFloodKernel, sp_FromTexture, _depthCacheTexture);
                        jumpFloodCommandBuffer.SetComputeTextureParam(initJumpFloodShader, initJumpFloodKernel, sp_ToTexture, voronoiPingPongTexture0);
                        jumpFloodCommandBuffer.SetComputeIntParam(initJumpFloodShader, sp_textureDimension, (int)textureDimension);
                        jumpFloodCommandBuffer.SetComputeMatrixParam(initJumpFloodShader, sp_projectionToWorld, projectionToWorldMatrix);
                        jumpFloodCommandBuffer.DispatchCompute(
                            initJumpFloodShader,
                            initJumpFloodKernel,
                            _depthCacheTexture.width / 8,
                            _depthCacheTexture.height / 8,
                            1
                        );
                    }
                    ComputeShader jumpFloodShader = ComputeShaderHelpers.LoadShader("SdfJumpFlood");
                    int jumpFloodKernel = jumpFloodShader.FindKernel("SdfJumpFlood");

                    ComputeShader sdfGradientShader = ComputeShaderHelpers.LoadShader("SdfApply");
                    int sdfKernel = sdfGradientShader.FindKernel("SdfApply");


                    jumpFloodCommandBuffer.name = "Jump Flood";
                    for (uint jumpSize = (uint)textureDimension / 2; jumpSize > 0; jumpSize /= 2)
                    {
                        ApplyJumpFlood(
                            jumpFloodCommandBuffer, jumpFloodShader, jumpFloodKernel,
                            sp_jumpSize, jumpSize,
                            sp_textureDimension, textureDimension,
                            sp_projectionToWorld, projectionToWorldMatrix,
                            sp_FromTexture, voronoiPingPongTexture0,
                            sp_ToTexture, voronoiPingPongTexture1
                        );
                        LodDataMgr.Swap(ref voronoiPingPongTexture1, ref voronoiPingPongTexture0);
                    }

                    for (uint roundNum = 0; roundNum < _additionalJumpFloodRounds; roundNum++)
                    {
                        uint jumpSize = (uint)1 << (int)roundNum;
                        ApplyJumpFlood(
                            jumpFloodCommandBuffer, jumpFloodShader, jumpFloodKernel,
                            sp_jumpSize, jumpSize,
                            sp_textureDimension, textureDimension,
                            sp_projectionToWorld, projectionToWorldMatrix,
                            sp_FromTexture, voronoiPingPongTexture0,
                            sp_ToTexture, voronoiPingPongTexture1
                        );
                        LodDataMgr.Swap(ref voronoiPingPongTexture1, ref voronoiPingPongTexture0);
                    }

                    jumpFloodCommandBuffer.SetComputeTextureParam(sdfGradientShader, sdfKernel, sp_FromTexture, voronoiPingPongTexture0);
                    jumpFloodCommandBuffer.SetComputeTextureParam(sdfGradientShader, sdfKernel, sp_ToTexture, _depthCacheTexture);
                    jumpFloodCommandBuffer.SetComputeIntParam(sdfGradientShader, sp_textureDimension, (int)textureDimension);
                    jumpFloodCommandBuffer.SetComputeMatrixParam(sdfGradientShader, sp_projectionToWorld, projectionToWorldMatrix);
                    jumpFloodCommandBuffer.DispatchCompute(
                        sdfGradientShader,
                        sdfKernel,
                        voronoiPingPongTexture0.width / 8,
                        voronoiPingPongTexture0.height / 8,
                        1
                    );
                    Graphics.ExecuteCommandBuffer(jumpFloodCommandBuffer);
                }
                DrawCacheQuad(ref _drawDepthCacheQuad, "SDFCache_", OceanDepthCacheType.Baked, _type == OceanDepthCacheType.Baked ? (Texture)_savedCache : _depthCacheTexture);

                voronoiPingPongTexture0.DiscardContents();
                voronoiPingPongTexture1.DiscardContents();
            }
            else
            {
                DrawCacheQuad(ref _drawDepthCacheQuad, "DepthCache_", _type, _type == OceanDepthCacheType.Baked ? (Texture)_savedCache : _depthCacheTexture);
            }
        }

        private static void ApplyJumpFlood(
            CommandBuffer jumpFloodCommandBuffer,
            ComputeShader jumpFloodShader,
            int kernel,
            int sp_jumpSize, uint jumpSize,
            int sp_textureDimension, uint textureDimension,
            int sp_projectionToWorld, Matrix4x4 projectionToWorld,
            int sp_FromTexture, RenderTexture fromTexture,
            int sp_ToTexture, RenderTexture toTexture
        )
        {
            jumpFloodCommandBuffer.SetComputeIntParam(jumpFloodShader, sp_jumpSize, (int)jumpSize);
            jumpFloodCommandBuffer.SetComputeIntParam(jumpFloodShader, sp_textureDimension, (int)textureDimension);
            jumpFloodCommandBuffer.SetComputeMatrixParam(jumpFloodShader, sp_projectionToWorld, projectionToWorld);
            jumpFloodCommandBuffer.SetComputeTextureParam(jumpFloodShader, kernel, sp_FromTexture, fromTexture);
            jumpFloodCommandBuffer.SetComputeTextureParam(jumpFloodShader, kernel, sp_ToTexture, toTexture);
            jumpFloodCommandBuffer.DispatchCompute(
                jumpFloodShader,
                kernel,
                fromTexture.width / 8,
                fromTexture.height / 8,
                1
            );
        }

        private static Camera GenerateCacheCamera(
            int layerMask,
            string cameraName,
            float cameraMaxTerrainHeight,
            Transform transform,
            RenderTexture cacheTexture,
            bool hideDepthCacheCam
        )
        {
            Camera camDepthCache = new GameObject(cameraName).AddComponent<Camera>();
            camDepthCache.gameObject.hideFlags = hideDepthCacheCam ? HideFlags.HideAndDontSave : HideFlags.DontSave;
            camDepthCache.transform.position = transform.position + Vector3.up * cameraMaxTerrainHeight;
            camDepthCache.transform.parent = transform;
            camDepthCache.transform.localEulerAngles = 90f * Vector3.right;
            camDepthCache.orthographic = true;
            camDepthCache.orthographicSize = Mathf.Max(transform.lossyScale.x / 2f, transform.lossyScale.z / 2f);
            camDepthCache.targetTexture = cacheTexture;
            camDepthCache.cullingMask = layerMask;
            camDepthCache.clearFlags = CameraClearFlags.SolidColor;
            // Clear to 'very deep'
            camDepthCache.backgroundColor = Color.white * 1000f;
            camDepthCache.enabled = false;
            camDepthCache.allowMSAA = false;
            // Stops behaviour from changing in VR. I tried disabling XR before/after camera render but it makes the editor
            // go bonkers with split windows.
            camDepthCache.cameraType = CameraType.Reflection;
            // I'd prefer to destroy the cam object, but I found sometimes (on first start of editor) it will fail to render.
            camDepthCache.gameObject.SetActive(false);
            return camDepthCache;
        }

        void DrawCacheQuad(ref GameObject drawCacheQuad, string name, OceanDepthCacheType type, Texture texture)
        {
            if (drawCacheQuad != null)
            {
                return;
            }
            drawCacheQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            drawCacheQuad.hideFlags = HideFlags.DontSave;
#if UNITY_EDITOR
            DestroyImmediate(drawCacheQuad.GetComponent<Collider>());
#else
            Destroy(drawCacheQuad.GetComponent<Collider>());
#endif
            drawCacheQuad.name = "DepthCache_" + gameObject.name;
            drawCacheQuad.transform.SetParent(transform, false);
            drawCacheQuad.transform.localEulerAngles = 90f * Vector3.right;
            drawCacheQuad.AddComponent<RegisterSeaFloorDepthInput>()._assignOceanDepthMaterial = false;
            var qr = drawCacheQuad.GetComponent<Renderer>();
            qr.sharedMaterial = new Material(Shader.Find(LodDataMgrSeaFloorDepth.ShaderName));
            if (_type == OceanDepthCacheType.Baked)
            {
                qr.sharedMaterial.mainTexture = texture;
            }
            else
            {
                qr.sharedMaterial.mainTexture = texture;
            }
            qr.enabled = false;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0f, 0f, 0.5f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(1f, 0f, 1f));

            if (_type == OceanDepthCacheType.Realtime)
            {
                Gizmos.color = new Color(1f, 1f, 1f, 0.2f);
                Gizmos.DrawCube(Vector3.up * _cameraMaxTerrainHeight / transform.lossyScale.y, new Vector3(1f, 0f, 1f));
            }
        }
#endif
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(OceanDepthCache))]
    public class OceanDepthCacheEditor : ValidatedEditor
    {
        readonly string[] _propertiesToExclude = new string[] { "m_Script", "_type", "_refreshMode", "_savedCache", "_geometryToRenderIntoCache", "_layerNames", "_resolution", "_cameraMaxTerrainHeight", "_forceAlwaysUpdateDebug", "_checkTerrainDrawInstancedOption", "_refreshEveryFrameInEditMode" };

        public override void OnInspectorGUI()
        {
            // We won't just use default inspector because we want to show some of the params conditionally based on cache type

            // First show standard 'Script' field
            GUI.enabled = false;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            GUI.enabled = true;

            // Next expose cache type and refresh mode

            var typeProp = serializedObject.FindProperty("_type");
            EditorGUILayout.PropertyField(typeProp);

            var cacheType = (OceanDepthCache.OceanDepthCacheType)typeProp.intValue;

            if (cacheType == OceanDepthCache.OceanDepthCacheType.Realtime)
            {
                // Only expose the following if real-time cache type
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_refreshMode"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_geometryToRenderIntoCache"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_layerNames"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_resolution"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_cameraMaxTerrainHeight"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_forceAlwaysUpdateDebug"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_checkTerrainDrawInstancedOption"));
            }
            else
            {
                // Only expose saved cache if non-real-time
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_savedCache"));
            }

            // Draw rest of inspector fields
            DrawPropertiesExcluding(serializedObject, _propertiesToExclude);

            // Apply inspector changes
            serializedObject.ApplyModifiedProperties();

            var playing = EditorApplication.isPlaying;

            var dc = target as OceanDepthCache;
            var isOnDemand = cacheType == OceanDepthCache.OceanDepthCacheType.Realtime &&
                dc.RefreshMode == OceanDepthCache.OceanDepthCacheRefreshMode.OnDemand;
            var isBakeable = cacheType == OceanDepthCache.OceanDepthCacheType.Realtime &&
                (!isOnDemand || dc.CacheTexture != null);

            if ((!playing || isOnDemand) && dc.Type != OceanDepthCache.OceanDepthCacheType.Baked && GUILayout.Button("Populate cache"))
            {
                dc.PopulateCache();
            }

            if (isBakeable && GUILayout.Button("Save cache to file"))
            {
                var rt = dc.CacheTexture;
                RenderTexture.active = rt;
                Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAHalf, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                RenderTexture.active = null;

                byte[] bytes;
                bytes = tex.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat);

                string path = dc.SavedCache ?
                    AssetDatabase.GetAssetPath(dc.SavedCache) : $"Assets/OceanDepthCache_{Guid.NewGuid()}.exr";
                System.IO.File.WriteAllBytes(path, bytes);
                AssetDatabase.ImportAsset(path);

                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                ti.textureType = TextureImporterType.SingleChannel;
                ti.sRGBTexture = false;
                ti.alphaSource = TextureImporterAlphaSource.None;
                ti.alphaIsTransparency = false;
                ti.SaveAndReimport();

                Debug.Log("Cache saved to " + path, AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path));
            }

            ShowValidationMessages();
        }
    }

    public partial class OceanDepthCache : IValidated
    {
        public bool Validate(OceanRenderer ocean, ValidatedHelper.ShowMessage showMessage)
        {
            var isValid = true;

            if (_type == OceanDepthCacheType.Baked)
            {
                if (_savedCache == null)
                {
                    showMessage
                    (
                        "Depth cache type is 'Saved Cache' but no saved cache data is provided.",
                        ValidatedHelper.MessageType.Error, this
                    );

                    isValid = false;
                }
            }
            else
            {
                if (_geometryToRenderIntoCache.Length == 0 && _layerNames.Length == 0)
                {
                    showMessage
                    (
                        "No layers specified for rendering into depth cache, and no geometries manually provided.",
                        ValidatedHelper.MessageType.Error, this
                    );

                    isValid = false;
                }

                if (_forceAlwaysUpdateDebug)
                {
                    showMessage
                    (
                        $"<i>Force Always Update Debug</i> option is enabled on depth cache <i>{gameObject.name}</i>, which means it will render every frame instead of running from the cache.",
                        ValidatedHelper.MessageType.Warning, this
                    );

                    isValid = false;
                }

                foreach (var layerName in _layerNames)
                {
                    if (string.IsNullOrEmpty(layerName))
                    {
                        showMessage
                        (
                            "An empty layer name was provided. Please provide a valid layer name.",
                            ValidatedHelper.MessageType.Error, this
                        );

                        isValid = false;
                        continue;
                    }

                    var layer = LayerMask.NameToLayer(layerName);
                    if (layer == -1)
                    {
                        showMessage
                        (
                            $"Invalid layer specified for objects/geometry providing the ocean depth: <i>{layerName}</i>. Please add this layer to the project by putting the name in an empty layer slot in <i>Edit/Project Settings/Tags and Layers</i>?",
                            ValidatedHelper.MessageType.Error, this
                        );

                        isValid = false;
                    }
                }

                if (_resolution < 4)
                {
                    showMessage
                    (
                        $"Cache resolution {_resolution} is very low. Is this intentional?",
                        ValidatedHelper.MessageType.Error, this
                    );

                    isValid = false;
                }

                // We used to test if nothing is present that would render into the cache, but these could probably come from other scenes, and AssignLayer means
                // objects can be tagged up at run-time.
            }

            if (transform.lossyScale.magnitude < 5f)
            {
                showMessage
                (
                    "Ocean depth cache transform scale is small and will capture a small area of the world. The scale sets the size of the area that will be cached, and this cache is set to render a very small area.",
                    ValidatedHelper.MessageType.Warning, this
                );

                isValid = false;
            }

            if (transform.lossyScale.y < 0.001f || transform.localScale.y < 0.01f)
            {
                showMessage
                (
                    $"Ocean depth cache scale Y should be set to 1.0. Its current scale in the hierarchy is {transform.lossyScale.y}.",
                    ValidatedHelper.MessageType.Error, this
                );

                isValid = false;
            }

            if (ocean != null && ocean.Root != null && Mathf.Abs(transform.position.y - ocean.Root.position.y) > 0.00001f)
            {
                showMessage
                (
                    "It is recommended that the cache is placed at the same height (y component of position) as the ocean, i.e. at the sea level. If the cache is created before the ocean is present, the cache height will inform the sea level.",
                    ValidatedHelper.MessageType.Warning, this
                );

                isValid = false;
            }

            // Check that there are no renderers in descendants.
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length > (Application.isPlaying ? 1 : 0))
            {
                Renderer quadRenderer = _drawDepthCacheQuad ? _drawDepthCacheQuad.GetComponent<Renderer>() : null;

                foreach (var renderer in renderers)
                {
                    if (ReferenceEquals(renderer, quadRenderer)) continue;

                    showMessage
                    (
                        "It is not expected that a depth cache object has a Renderer component in its hierarchy." +
                        "The cache is typically attached to an empty GameObject. Please refer to the example content.",
                        ValidatedHelper.MessageType.Warning, renderer
                    );

                    // Reporting only one renderer at a time will be enough to avoid overwhelming user and UI.
                    break;
                }

                isValid = false;
            }

            return isValid;
        }
    }
#endif
}
