// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)


half3 ApplyUnderwaterEffect(
	in Texture2DArray i_dispSampler,
	in sampler2D i_normalsSampler,
	in const float3 i_cameraPos,
	in const half3 i_ambientLighting,
	half3 i_sceneColour,
	in const float i_sceneZ,
	in const float i_fogDistance,
	in const half3 i_view,
	in const half4 i_depthFogDensity,
	in const bool i_isOceanSurface
) {
	const float3 lightDir = _WorldSpaceLightPos0.xyz;

	half3 scatterCol = 0.0;
	int sliceIndex = clamp(_DataSliceOffset, 0, _SliceCount - 2);
	{
		float3 dummy;
		half sss = 0.0;
		const float3 uv_slice = WorldToUV(i_cameraPos.xz, _CrestCascadeData[sliceIndex], sliceIndex);
		SampleDisplacements(i_dispSampler, uv_slice, 1.0, dummy, sss);

		// depth and shadow are computed in ScatterColour when underwater==true, using the LOD1 texture.
		const float depth = 0.0;
		const half shadow = 1.0;

		{
			const float meshScaleLerp = _CrestPerCascadeInstanceData[sliceIndex]._meshScaleLerp;
			const float baseCascadeScale = _CrestCascadeData[0]._scale;
			scatterCol = ScatterColour(i_ambientLighting, depth, i_cameraPos, lightDir, i_view, shadow, true, true, sss, meshScaleLerp, baseCascadeScale, _CrestCascadeData[sliceIndex]);
		}
	}

#if _CAUSTICS_ON
	if (i_sceneZ != 0.0 && !i_isOceanSurface)
	{
		ApplyCaustics(i_view, lightDir, i_sceneZ, i_normalsSampler, true, i_sceneColour, _CrestCascadeData[sliceIndex], _CrestCascadeData[sliceIndex + 1]);
	}
#endif // _CAUSTICS_ON

	return lerp(i_sceneColour, scatterCol, saturate(1.0 - exp(-i_depthFogDensity.xyz * i_fogDistance)));
}
