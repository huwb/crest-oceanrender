// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using System.Collections.Generic;
using UnityEngine;

namespace Crest
{
    // This interface exists to allow UnderwaterPostProcess utils to interact with
    // the per-camera data that is needed by both the HDRP and built-in implementation
    // of underwater post-processing, but done using different classes.
    public interface IUnderwaterPostProcessPerCameraData
    {
        List<OceanOccluder> OceanOccluderMasksToRender { get; }
        bool enabled { get; }
        void RegisterOceanOccluder(OceanOccluder _oceanOccluder);
    }
}
