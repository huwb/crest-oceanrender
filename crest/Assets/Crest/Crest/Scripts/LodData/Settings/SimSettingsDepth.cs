// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using UnityEngine;

namespace Crest
{
    [CreateAssetMenu(fileName = "SimSettingsDepth", menuName = "Crest/Depth Sim Settings", order = 10000)]
    public class SimSettingsDepth : SimSettingsBase
    {
        [Tooltip("Support signed distance field data generated from the depth caches")]
        public bool _enableSignedDistanceFields = false;
    }
}
