// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

#ifndef CREST_OCEAN_OCCLUDER_HELPERS_H
#define CREST_OCEAN_OCCLUDER_HELPERS_H

// @volatie:OceanOccluderMaskValues These MUST match the values in OceanOccluder.cs
#define OCCLUDER_MASK_OCCLUDE_ALL 0.0
#define OCCLUDER_MASK_CANCEL_OCCLUSION 1.0
#define OCCLUDER_MASK_OCCLUDE_WATER_BEHIND 2.0 // for transparent materials
#define OCCLUDER_MASK_OCCLUDE_WATER_IN_FRONT 3.0 // for transparent materials

// We put all of this functionality in a centralised place to ensure that the different possible defines are easier
// to keep consistent.

// TODO(TRC):Now @optimisation pack opaque information in the underwater mask texture, only enable
// parts of this based on which underwater features we have enabled.
sampler2D _CrestOceanOccluderMaskTexture;
sampler2D _CrestOceanOccluderMaskDepthTexture;

void DiscardOceanSurfaceFromOccluderMask(float2 uv, float z)
{
	// TODO(TRC):Now @optimisation pack opaque information in the underwater mask texture, only enable
	// parts of this based on which underwater features we have enabled.
	float occluderMask = tex2D(_CrestOceanOccluderMaskTexture, uv).x;
	float occluderDepth = tex2D(_CrestOceanOccluderMaskDepthTexture, uv).x;
	if(
		(occluderMask == OCCLUDER_MASK_OCCLUDE_ALL) ||
		(occluderMask == OCCLUDER_MASK_OCCLUDE_WATER_IN_FRONT && occluderDepth < z) ||
		(occluderMask == OCCLUDER_MASK_OCCLUDE_WATER_BEHIND && occluderDepth >= z)
	) {
		discard;
	}
}

void DiscardOceanMaskFromOccluderMask(float2 uv, float z)
{
	float occluderMask = tex2D(_CrestOceanOccluderMaskTexture, uv).x;
	float occluderDepth = tex2D(_CrestOceanOccluderMaskDepthTexture, uv).x;

	// TODO(TRC):Now @optimisation pack opaque information in the underwater mask texture, only enable
	// parts of this based on which underwater features we have enabled.
	if(occluderMask == OCCLUDER_MASK_OCCLUDE_ALL || (occluderMask == OCCLUDER_MASK_OCCLUDE_WATER_IN_FRONT && occluderDepth < z))
	{
		discard;
	}
}

float PostProcessHandleOccluderMask(float2 uv, float oceanMask, float oceanDepth01)
{
	const float occluderMask = tex2D(_CrestOceanOccluderMaskTexture, uv).x;
	const float occluderDepth01 = tex2D(_CrestOceanOccluderMaskDepthTexture, uv).x;
	{
		const bool disableWaterBehindOccluderMask = (occluderMask == OCCLUDER_MASK_OCCLUDE_WATER_BEHIND && (oceanDepth01 < occluderDepth01));
		oceanDepth01 = disableWaterBehindOccluderMask ? occluderDepth01 : oceanDepth01;
	}

	// TODO(TRC):Now @optimisation pack opaque information in the underwater mask texture, only enable
	// parts of this based on which underwater features we have enabled.
	if(occluderMask == OCCLUDER_MASK_OCCLUDE_ALL || occluderMask == OCCLUDER_MASK_OCCLUDE_WATER_IN_FRONT)
	{
		oceanMask = UNDERWATER_MASK_WATER_SURFACE_ABOVE;
	}
	else if(occluderMask == OCCLUDER_MASK_OCCLUDE_WATER_BEHIND)
	{
		// We want to disable underwater behind the occluder surface,
		// but therefore if we are already underwater, we need to ensure that we apply for to the appropriate pixels.
		oceanMask = UNDERWATER_MASK_WATER_SURFACE_BELOW;
	}
	return oceanMask;
}

#endif // CREST_OCEAN_OCCLUDER_HELPERS_H
