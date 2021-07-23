// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using UnityEngine;
using UnityEngine.Experimental.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Crest
{
    [CreateAssetMenu(fileName = "SimSettingsClipSurface", menuName = "Crest/Clip Surface Sim Settings", order = 10000)]
    [HelpURL(HELP_URL)]
    public class SimSettingsClipSurface : SimSettingsBase
    {
        /// <summary>
        /// The version of this asset. Can be used to migrate across versions. This value should
        /// only be changed when the editor upgrades the version.
        /// </summary>
        [SerializeField, HideInInspector]
#pragma warning disable 414
        int _version = 0;
#pragma warning restore 414

        public const string HELP_URL = Internal.Constants.HELP_URL_BASE_USER + "ocean-simulation.html" + Internal.Constants.HELP_URL_RP + "#clip-surface";

        // The clip values only really need 8bits (unless using signed distance).
        [Tooltip("The render texture format to use for the clip surface simulation. It should only be changed if you need more precision. See the documentation for information.")]
        public GraphicsFormat _renderTextureGraphicsFormat = GraphicsFormat.R8_UNorm;

        public override void AddToSettingsHash(ref int settingsHash)
        {
            base.AddToSettingsHash(ref settingsHash);
            Hashy.AddInt((int)_renderTextureGraphicsFormat, ref settingsHash);
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(SimSettingsClipSurface), true), CanEditMultipleObjects]
    class SimSettingsClipSurfaceEditor : SimSettingsBaseEditor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.Space();
            if (GUILayout.Button("Open Online Help Page"))
            {
                Application.OpenURL(SimSettingsClipSurface.HELP_URL);
            }
            EditorGUILayout.Space();

            base.OnInspectorGUI();
        }
    }
#endif
}
