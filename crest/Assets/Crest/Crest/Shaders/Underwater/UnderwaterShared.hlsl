// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

#ifndef CREST_UNDERWATER_SHARED_INCLUDED
#define CREST_UNDERWATER_SHARED_INCLUDED

#define CREST_MAX_UPDOWN_AMOUNT 0.8

float IntersectRayWithWaterSurface(const float seaLevel, const float3 pos, const float3 dir, in const CascadeParams cascadeData)
{
	// Find intersection of the near plane and the water surface at this vert using FPI. See here for info about
	// FPI http://www.huwbowles.com/fpi-gdc-2016/

	// get point at sea level
	float2 sampleXZ = pos.xz - dir.xz * (pos.y - seaLevel) / dir.y;
	float3 disp;
	//for (int i = 0; i < 6; i++)
	{
		// Sample displacement textures, add results to current world pos / normal / foam
		disp = float3(sampleXZ.x, seaLevel, sampleXZ.y);
		half sss = 0.;
		const float3 uv = WorldToUV(sampleXZ, cascadeData, _LD_SliceIndex);
		SampleDisplacements(_LD_TexArray_AnimatedWaves, uv, 1.0, disp, sss);
		float3 nearestPointOnRay = pos + dir * dot(disp - pos, dir);
		const float2 error = disp.xz - nearestPointOnRay.xz;
		sampleXZ -= error;
	}
	{
		// Sample displacement textures, add results to current world pos / normal / foam
		disp = float3(sampleXZ.x, seaLevel, sampleXZ.y);
		half sss = 0.;
		const float3 uv = WorldToUV(sampleXZ, cascadeData, _LD_SliceIndex);
		SampleDisplacements(_LD_TexArray_AnimatedWaves, uv, 1.0, disp, sss);
		float3 nearestPointOnRay = pos + dir * dot(disp - pos, dir);
		const float2 error = disp.xz - nearestPointOnRay.xz;
		sampleXZ -= error;
	}
	{
		// Sample displacement textures, add results to current world pos / normal / foam
		disp = float3(sampleXZ.x, seaLevel, sampleXZ.y);
		half sss = 0.;
		const float3 uv = WorldToUV(sampleXZ, cascadeData, _LD_SliceIndex);
		SampleDisplacements(_LD_TexArray_AnimatedWaves, uv, 1.0, disp, sss);
		float3 nearestPointOnRay = pos + dir * dot(disp - pos, dir);
		const float2 error = disp.xz - nearestPointOnRay.xz;
		sampleXZ -= error;
	}
	{
		// Sample displacement textures, add results to current world pos / normal / foam
		disp = float3(sampleXZ.x, seaLevel, sampleXZ.y);
		half sss = 0.;
		const float3 uv = WorldToUV(sampleXZ, cascadeData, _LD_SliceIndex);
		SampleDisplacements(_LD_TexArray_AnimatedWaves, uv, 1.0, disp, sss);
		float3 nearestPointOnRay = pos + dir * dot(disp - pos, dir);
		const float2 error = disp.xz - nearestPointOnRay.xz;
		sampleXZ -= error;
	}
	{
		// Sample displacement textures, add results to current world pos / normal / foam
		disp = float3(sampleXZ.x, seaLevel, sampleXZ.y);
		half sss = 0.;
		const float3 uv = WorldToUV(sampleXZ, cascadeData, _LD_SliceIndex);
		SampleDisplacements(_LD_TexArray_AnimatedWaves, uv, 1.0, disp, sss);
		float3 nearestPointOnRay = pos + dir * dot(disp - pos, dir);
		const float2 error = disp.xz - nearestPointOnRay.xz;
		sampleXZ -= error;
	}
	{
		// Sample displacement textures, add results to current world pos / normal / foam
		disp = float3(sampleXZ.x, seaLevel, sampleXZ.y);
		half sss = 0.;
		const float3 uv = WorldToUV(sampleXZ, cascadeData, _LD_SliceIndex);
		SampleDisplacements(_LD_TexArray_AnimatedWaves, uv, 1.0, disp, sss);
		float3 nearestPointOnRay = pos + dir * dot(disp - pos, dir);
		const float2 error = disp.xz - nearestPointOnRay.xz;
		sampleXZ -= error;
	}

	return dot(disp - pos, dir);
}

#endif // CREST_UNDERWATER_SHARED_INCLUDED
