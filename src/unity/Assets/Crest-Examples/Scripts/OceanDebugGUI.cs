﻿using Crest;
using UnityEngine;

public class OceanDebugGUI : MonoBehaviour
{
    public bool _showSimTargets = true;
    public bool _guiVisible = true;
    public string _oceanMaterialAsset = "Assets/Crest/Shaders/Materials/Ocean.mat";
    public bool _showSimTargetsAlpha = false;
    static float _leftPanelWidth = 180f;
    ShapeGerstnerBatched[] gerstners;

    public static bool OverGUI( Vector2 screenPosition )
    {
        return screenPosition.x < _leftPanelWidth;
    }

    private void Start()
    {
        gerstners = FindObjectsOfType<ShapeGerstnerBatched>();
        // i am getting the array in the reverse order compared to the hierarchy which bugs me. sort them based on sibling index,
        // which helps if the gerstners are on sibling GOs.
        System.Array.Sort(gerstners, (a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.G))
        {
            ToggleGUI();
        }
        if (Input.GetKeyDown(KeyCode.F))
        {
            OceanRenderer.Instance._freezeTime = !OceanRenderer.Instance._freezeTime;
        }
    }

    void OnGUI()
    {
        Color bkp = GUI.color;

        if (_guiVisible)
        {
            GUI.skin.toggle.normal.textColor = Color.white;
            GUI.skin.label.normal.textColor = Color.white;

            float x = 5f, y = 0f;
            float w = _leftPanelWidth - 2f * x, h = 25f;

            GUI.color = Color.black * 0.7f;
            GUI.DrawTexture(new Rect(0, 0, w + 2f * x, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            RenderWireFrame._wireFrame = GUI.Toggle(new Rect(x, y, w, h), RenderWireFrame._wireFrame, "Wireframe"); y += h;

            OceanRenderer.Instance._freezeTime = GUI.Toggle(new Rect(x, y, w, h), OceanRenderer.Instance._freezeTime, "Freeze waves (F)"); y += h;

            GUI.Label(new Rect(x, y, w, h), "Gerstner weight(s)"); y += h;
            foreach (var gerstner in gerstners)
            {
                gerstner._weight = GUI.HorizontalSlider(new Rect(x, y, w, h), gerstner._weight, 0f, 1f); y += h;
            }

            _showSimTargets = GUI.Toggle(new Rect(x, y, w, h), _showSimTargets, "Show shape data"); y += h;
            if(_showSimTargets)
            {
                _showSimTargetsAlpha = GUI.Toggle(new Rect(x, y, w, h), _showSimTargetsAlpha, "Show shape alpha"); y += h;
            }
            WaveDataCam._shapeCombinePass = GUI.Toggle(new Rect(x, y, w, h), WaveDataCam._shapeCombinePass, "Shape combine pass"); y += h;

            int min = int.MaxValue, max = -1;
            bool readbackShape = true;
            foreach( var wdc in OceanRenderer.Instance.Builder._shapeWDCs)
            {
                min = Mathf.Min(min, wdc.CollData.CollReadbackRequestsQueued);
                max = Mathf.Max(max, wdc.CollData.CollReadbackRequestsQueued);
                readbackShape = readbackShape && wdc._readbackShapeForCollision;
            }
            if (readbackShape != GUI.Toggle(new Rect(x, y, w, h), readbackShape, "Readback coll data"))
            {
                foreach (var wdc in OceanRenderer.Instance.Builder._shapeWDCs)
                {
                    wdc._readbackShapeForCollision = !readbackShape;
                }
            }
            y += h;

            // generates garbage
            GUI.Label(new Rect(x, y, w, h), string.Format("Coll Queue Lengths: [{0}, {1}]", min, max)); y += h;

            if (OceanRenderer.Instance.CachedCpuOceanQueries)
            {
                var cache = OceanRenderer.Instance.CollisionProvider as CollProviderCache;
                // generates garbage
                GUI.Label(new Rect(x, y, w, h), string.Format("Cache hits: {0}/{1}", cache.CacheHits, cache.CacheChecks)); y += h;
            }

            if (GUI.Button(new Rect(x, y, w, h), "Hide GUI (G)"))
            {
                ToggleGUI();
            }
            y += h;

#if UNITY_EDITOR
            if (GUI.Button(new Rect(x, y, w, h), "Select Ocean Mat"))
            {
                UnityEditor.Selection.activeObject = UnityEditor.AssetDatabase.LoadMainAssetAtPath(_oceanMaterialAsset);
            }
            y += h;
#endif
        }

        // draw source textures to screen
        if ( _showSimTargets )
        {
            DrawShapeTargets();
        }

        GUI.color = bkp;
    }

    void DrawShapeTargets()
    {
        int ind = 0;
        foreach (var cam in OceanRenderer.Instance.Builder._shapeCameras)
        {
            if (!cam) continue;

            RenderTexture shape = cam.targetTexture;

            if (shape == null) continue;

            float b = 7f;
            float h = Screen.height / (float)OceanRenderer.Instance.Builder._shapeCameras.Length;
            float w = h + b;
            float x = Screen.width - w;
            float y = ind * h;

            GUI.color = Color.black * 0.7f;
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(x + b, y + b / 2f, h - b, h - b), shape, ScaleMode.ScaleAndCrop, _showSimTargetsAlpha);

            ind++;
        }
        ind = 0;
        foreach (WaveDataCam wdc in OceanRenderer.Instance.Builder._shapeWDCs)
        {
            if (!wdc) continue;

            RenderTexture shape = wdc._rtFoam;

            if (shape == null) continue;

            float b = 7f;
            float h = Screen.height / (float)OceanRenderer.Instance.Builder._shapeWDCs.Length;
            float w = h + b;
            float x = Screen.width - (w + w);
            float y = ind * h;

            GUI.color = Color.black * 0.7f;
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(x + b, y + b / 2f, h - b, h - b), shape, ScaleMode.ScaleAndCrop, false);

            ind++;
        }
    }

    void ToggleGUI()
    {
        _guiVisible = !_guiVisible;
    }
}
