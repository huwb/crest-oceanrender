// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using UnityEngine;

namespace Crest
{
    [CreateAssetMenu(fileName = "SimSettingsSeaFloorDepth", menuName = "Crest/Sea Floor Depth Settings", order = 10000)]
    public class SimSettingsSeaFloorDepth : SimSettingsBase
    {
        [Tooltip("Allow multiple sea levels. This allows multiple bodies of water at different altitudes from the global sea level (set by the altitude of the Ocean GameObject.")]
        public bool _allowMultipleSeaLevels = true;
    }
}
