// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

#ifndef CREST_CONSTANTS_H
#define CREST_CONSTANTS_H

// NOTE: these MUST match the values in PropertyWrapper.cs
#define THREAD_GROUP_SIZE_X 8
#define THREAD_GROUP_SIZE_Y 8

// NOTE: This must match the value in LodDataMgr.cs, as it is used to allow the
// C# code to check if any parameters are within the MAX_LOD_COUNT limits
#define MAX_LOD_COUNT 15

// How light is attenuated deep in water
#define DEPTH_OUTSCATTER_CONSTANT 0.25

// Bias ocean floor depth so that default (0) values in texture are not interpreted as shallow and generating foam everywhere
#define CREST_OCEAN_DEPTH_BASELINE 1000.0

// Soft shadows is red, hard shadows is green.
#define CREST_SHADOW_INDEX_SOFT 0
#define CREST_SHADOW_INDEX_HARD 1

// @volatile: UnderwaterMaskValues These MUST match the values in UnderwaterPostProcessUtils.cs

// Background
#define UNDERWATER_MASK_NO_MASK 1.0
// Water rendered from above
#define UNDERWATER_MASK_WATER_SURFACE_ABOVE 0.0
// Water rendered from below
#define UNDERWATER_MASK_WATER_SURFACE_BELOW 2.0
// Underwater effect applied from a distance (Only works for geometry not in the depth buffer)
#define UNDERWATER_MASK_WINDOW 3.0

#if defined(UNITY_SINGLE_PASS_STEREO) || defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
#define CREST_HANDLE_XR 1
#else
#define CREST_HANDLE_XR 0
#endif

#endif // CREST_CONSTANTS_H
