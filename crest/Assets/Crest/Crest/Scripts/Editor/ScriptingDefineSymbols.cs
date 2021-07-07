// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;

namespace Crest
{
    /// <summary>
    /// Adds and removes Crest's scripting define symbols when this file is added or removed respectively.
    /// </summary>
    public class ScriptingDefineSymbols : UnityEditor.AssetModificationProcessor
    {
        /// <summary>
        /// Symbols that will be added to the editor.
        /// </summary>
        static readonly string[] Symbols = new[]
        {
            "CREST_OCEAN",
        };

// NOTE: All of the above symbols must be checked here like so: !SYMBOL_1 || !SYMBOL_2
#if !CREST_OCEAN
        [InitializeOnLoadMethod]
        static void OnProjectLoadedInEditor()
        {
            AddDefineSymbols();
        }
#endif

        static AssetDeleteResult OnWillDeleteAsset(string path, RemoveAssetOptions options)
        {
            // Only remove symbols if this file is deleted.
            if (Path.GetFullPath(path) == GetCurrentFileName())
            {
                RemoveDefineSymbols();
            }

            return AssetDeleteResult.DidNotDelete;
        }

        /// <summary>
        /// Add scripting define symbols.
        /// </summary>
        static void AddDefineSymbols()
        {
            // We remove our symbols from the list first to prevent duplicates - just to be safe.
            var symbols = string.Join(";", GetDefineSymbolsList().Except(Symbols).Concat(Symbols));
            PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, symbols);
        }

        /// <summary>
        /// Remove scripting define symbols.
        /// </summary>
        static void RemoveDefineSymbols()
        {
            var symbols = string.Join(";", GetDefineSymbolsList().Except(Symbols));
            PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, symbols);
        }

        /// <summary>
        /// Get scripting define symbols as a list.
        /// </summary>
        static List<string> GetDefineSymbolsList()
        {
            return PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup)
                .Split(';').ToList();
        }

        /// <summary>
        /// Get the file name of the current script file.
        /// </summary>
        static string GetCurrentFileName([System.Runtime.CompilerServices.CallerFilePath] string fileName = null)
        {
            return fileName;
        }
    }
}
