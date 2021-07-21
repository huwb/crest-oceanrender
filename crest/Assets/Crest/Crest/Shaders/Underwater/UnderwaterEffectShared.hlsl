// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

#ifndef CREST_UNDERWATER_EFFECT_SHARED_INCLUDED
#define CREST_UNDERWATER_EFFECT_SHARED_INCLUDED

half3 _AmbientLighting;
float4x4 _InvViewProjection;
float4x4 _InvViewProjectionRight;
float4 _HorizonPosNormal;
float4 _HorizonPosNormalRight;
half _DataSliceOffset;

float4 DebugRenderOceanMask(const bool isOceanSurface, const bool isUnderwater, const float mask, const float3 sceneColour)
{
	if (isOceanSurface)
	{
		return float4(sceneColour * float3(mask == UNDERWATER_MASK_WATER_SURFACE_ABOVE, mask == UNDERWATER_MASK_WATER_SURFACE_BELOW, 0.0), 1.0);
	}
	else
	{
		return float4(sceneColour * float3(isUnderwater * 0.5, (1.0 - isUnderwater) * 0.5, 1.0), 1.0);
	}
}

float3 ComputeWorldSpaceView(const float2 uv)
{
	const float2 pixelCS = uv * 2.0 - float2(1.0, 1.0);
	#if CREST_HANDLE_XR
		const float4x4 InvViewProjection = unity_StereoEyeIndex == 0 ? _InvViewProjection : _InvViewProjectionRight;
	#else
		const float4x4 InvViewProjection = _InvViewProjection;
	#endif
	const float4 pixelWS_H = mul(InvViewProjection, float4(pixelCS, 1.0, 1.0));
	const float3 pixelWS = pixelWS_H.xyz / pixelWS_H.w;
	return _WorldSpaceCameraPos - pixelWS;
}

float MeniscusSampleOceanMask(const float2 uvScreenSpace, const float2 dy, const half offset)
{
	float2 uv = uvScreenSpace + dy * offset;
	return UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CrestOceanMaskTexture, uv).r;
}

half ComputeMeniscusWeight(const float2 uvScreenSpace, const float mask, const float4 horizonPositionNormal, const float sceneZ)
{
	float wt = 1.0;
#if CREST_MENISCUS
#if !_FULL_SCREEN_EFFECT
	// Render meniscus by checking mask in opposite direction of surface normal.
	// If the sample is different than the current mask, apply meniscus.
	// Skip the meniscus beyond one unit to prevent numerous artefacts.
	if (mask != UNDERWATER_MASK_NO_MASK && sceneZ < 1.0)
	{
		float wt_mul = 0.9;
		// Adding the mask value will flip the UV when mask is below surface.
		// Apply the horizon normal so it works with any orientation.
		float2 dy = (float2(-1.0 + mask, -1.0 + mask) / _ScreenParams.y) * -horizonPositionNormal.zw;
		wt *= (MeniscusSampleOceanMask(uvScreenSpace, dy, 1.0) != mask) ? wt_mul : 1.0;
		wt *= (MeniscusSampleOceanMask(uvScreenSpace, dy, 2.0) != mask) ? wt_mul : 1.0;
		wt *= (MeniscusSampleOceanMask(uvScreenSpace, dy, 3.0) != mask) ? wt_mul : 1.0;
	}
#endif // _FULL_SCREEN_EFFECT
#endif // CREST_MENISCUS
	return wt;
}

void GetHorizonData(const float2 uv, out float4 horizonPositionNormal, out bool isBelowHorizon)
{
#if !_FULL_SCREEN_EFFECT
	// The horizon line is the intersection between the far plane and the ocean plane. The pos and normal of this
	// intersection line is passed in.
#if CREST_HANDLE_XR
	horizonPositionNormal = unity_StereoEyeIndex == 0 ? _HorizonPosNormal : _HorizonPosNormalRight;
#else // CREST_HANDLE_XR
	horizonPositionNormal = _HorizonPosNormal;
#endif // CREST_HANDLE_XR
	isBelowHorizon = dot(uv - horizonPositionNormal.xy, horizonPositionNormal.zw) > 0.0;
#else // !_FULL_SCREEN_EFFECT
	horizonPositionNormal = 0;
	isBelowHorizon = true;
#endif // !_FULL_SCREEN_EFFECT
}

void GetOceanSurfaceAndUnderwaterData(
	const float rawOceanDepth,
	const float mask,
	const bool isBelowHorizon,
	inout float rawDepth,
	inout bool isOceanSurface,
	inout bool isUnderwater,
	inout float sceneZ,
	const float oceanDepthTolerance
)
{
	isOceanSurface = mask != UNDERWATER_MASK_NO_MASK && (rawDepth < rawOceanDepth + oceanDepthTolerance);
	isUnderwater = mask == UNDERWATER_MASK_WATER_SURFACE_BELOW || (isBelowHorizon && mask != UNDERWATER_MASK_WATER_SURFACE_ABOVE);
	// Merge ocean depth with scene depth.
	rawDepth = isOceanSurface ? rawOceanDepth : rawDepth;
	sceneZ = CrestLinearEyeDepth(rawDepth);
}

#ifdef CREST_OCEAN_EMISSION_INCLUDED
half3 ApplyUnderwaterEffect(
	const half3 ambientLighting,
	const float3 scenePos,
	half3 sceneColour,
	const half3 lightCol,
	const float3 lightDir,
	const float rawDepth,
	const float sceneZ,
	const half3 view,
	const bool isOceanSurface
)
{
	half3 scatterCol = 0.0;
	int sliceIndex = clamp(_DataSliceOffset, 0, _SliceCount - 2);
	{
		float3 dummy;
		half sss = 0.0;
		// Offset slice so that we dont get high freq detail. But never use last lod as this has crossfading.
		const float3 uv_slice = WorldToUV(_WorldSpaceCameraPos.xz, _CrestCascadeData[sliceIndex], sliceIndex);
		SampleDisplacements(_LD_TexArray_AnimatedWaves, uv_slice, 1.0, dummy, sss);

		// depth and shadow are computed in ScatterColour when underwater==true, using the LOD1 texture.
		const float depth = 0.0;
		const half shadow = 1.0;
		{
			const float meshScaleLerp = _CrestPerCascadeInstanceData[sliceIndex]._meshScaleLerp;
			const float baseCascadeScale = _CrestCascadeData[0]._scale;
			scatterCol = ScatterColour(ambientLighting, depth, _WorldSpaceCameraPos, lightDir, view, shadow, true, true, lightCol, sss, meshScaleLerp, baseCascadeScale, _CrestCascadeData[sliceIndex]);
		}
	}

#if _CAUSTICS_ON
	if (rawDepth != 0.0 && !isOceanSurface)
	{
		ApplyCaustics(scenePos, lightDir, sceneZ, _Normals, true, sceneColour, _CrestCascadeData[sliceIndex], _CrestCascadeData[sliceIndex + 1]);
	}
#endif // _CAUSTICS_ON

	return lerp(sceneColour, scatterCol, saturate(1.0 - exp(-_DepthFogDensity.xyz * sceneZ)));
}
#endif // CREST_OCEAN_EMISSION_INCLUDED

#endif // CREST_UNDERWATER_EFFECT_SHARED_INCLUDED
