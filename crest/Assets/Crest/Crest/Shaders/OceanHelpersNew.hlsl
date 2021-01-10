// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

// Ocean LOD data - data, samplers and functions associated with LODs

#ifndef CREST_OCEAN_HELPERS_H
#define CREST_OCEAN_HELPERS_H

float2 WorldToUV(in float2 i_samplePos, in CascadeParams i_cascadeParams)
{
	return (i_samplePos - i_cascadeParams._posSnapped) / (i_cascadeParams._texelWidth * i_cascadeParams._textureRes) + 0.5;
}

float3 WorldToUV(in float2 i_samplePos, in CascadeParams i_cascadeParams, in float i_sliceIndex)
{
	float2 uv = (i_samplePos - i_cascadeParams._posSnapped) / (i_cascadeParams._texelWidth * i_cascadeParams._textureRes) + 0.5;
	return float3(uv, i_sliceIndex);
}

float2 UVToWorld(in float2 i_uv, in float i_sliceIndex, in CascadeParams i_cascadeParams)
{
	const float texelSize = i_cascadeParams._texelWidth;
	const float res = i_cascadeParams._textureRes;
	return texelSize * res * (i_uv - 0.5) + i_cascadeParams._posSnapped;
}

// Convert compute shader id to uv texture coordinates
float2 IDtoUV(in float2 i_id, in float i_width, in float i_height)
{
	return (i_id + 0.5) / float2(i_width, i_height);
}

// Sampling functions
void SampleDisplacements(in Texture2DArray i_dispSampler, in float3 i_uv_slice, in float i_wt, inout float3 io_worldPos, inout half io_sss)
{
	const half4 data = i_dispSampler.SampleLevel(LODData_linear_clamp_sampler, i_uv_slice, 0.0);
	io_worldPos += i_wt * data.xyz;
	io_sss += i_wt * data.a;
}

void SampleDisplacementsNormals(in Texture2DArray i_dispSampler, in float3 i_uv_slice, in float i_wt, in float i_invRes, in float i_texelSize, inout float3 io_worldPos, inout half2 io_nxz, inout half io_sss)
{
	const half4 data = i_dispSampler.SampleLevel(LODData_linear_clamp_sampler, i_uv_slice, 0.0);
	io_sss += i_wt * data.a;
	const half3 disp = data.xyz;
	io_worldPos += i_wt * disp;

	float3 n; {
		float3 dd = float3(i_invRes, 0.0, i_texelSize);
		half3 disp_x = dd.zyy + i_dispSampler.SampleLevel(LODData_linear_clamp_sampler, i_uv_slice + float3(dd.xy, 0.0), dd.y).xyz;
		half3 disp_z = dd.yyz + i_dispSampler.SampleLevel(LODData_linear_clamp_sampler, i_uv_slice + float3(dd.yx, 0.0), dd.y).xyz;
		n = normalize(cross(disp_z - disp, disp_x - disp));
	}
	io_nxz += i_wt * n.xz;
}

void SampleClip(in Texture2DArray i_oceanClipSurfaceSampler, in float3 i_uv_slice, in float i_wt, inout half io_clipValue)
{
	io_clipValue += i_wt * (i_oceanClipSurfaceSampler.SampleLevel(LODData_linear_clamp_sampler, i_uv_slice, 0.0).x);
}

void SampleFoam(in Texture2DArray i_oceanFoamSampler, in float3 i_uv_slice, in float i_wt, inout half io_foam)
{
	io_foam += i_wt * i_oceanFoamSampler.SampleLevel(LODData_linear_clamp_sampler, i_uv_slice, 0.0).x;
}

void SampleFlow(in Texture2DArray i_oceanFlowSampler, in float3 i_uv_slice, in float i_wt, inout half2 io_flow)
{
	io_flow += i_wt * i_oceanFlowSampler.SampleLevel(LODData_linear_clamp_sampler, i_uv_slice, 0.0).xy;
}

void SampleSeaDepth(in Texture2DArray i_oceanDepthSampler, in float3 i_uv_slice, in float i_wt, inout half io_oceanDepth)
{
	io_oceanDepth += i_wt * (i_oceanDepthSampler.SampleLevel(LODData_linear_clamp_sampler, i_uv_slice, 0.0).x - CREST_OCEAN_DEPTH_BASELINE);
}

void SampleSeaDepth(in Texture2DArray i_oceanDepthSampler, in float3 i_uv_slice, in float i_wt, inout half io_oceanDepth, inout float io_seaLevelOffset )
{
	float2 data = i_oceanDepthSampler.SampleLevel( LODData_linear_clamp_sampler, i_uv_slice, 0.0 );
	io_oceanDepth += i_wt * (data.x - CREST_OCEAN_DEPTH_BASELINE);
	// Quirk - offsets stored as negative values, to help with blending in OceanDepthsCache.shader, see comment there
	io_seaLevelOffset += i_wt * -data.y;
}

void SampleShadow(in Texture2DArray i_oceanShadowSampler, in float3 i_uv_slice, in float i_wt, inout half2 io_shadow)
{
	io_shadow += i_wt * i_oceanShadowSampler.SampleLevel(LODData_linear_clamp_sampler, i_uv_slice, 0.0).xy;
}

void PosToSliceIndices
(
	const float2 worldXZ,
	const float minSlice,
	const float oceanScale0,
	out float slice0,
	out float slice1,
	out float lodAlpha
)
{
	const float2 offsetFromCenter = abs(worldXZ - _OceanCenterPosWorld.xz);
	const float taxicab = max(offsetFromCenter.x, offsetFromCenter.y);
	const float radius0 = oceanScale0;
	const float sliceNumber = clamp(log2(max(taxicab / radius0, 1.0)), minSlice, _SliceCount - 1.0);

	lodAlpha = frac(sliceNumber);
	slice0 = floor(sliceNumber);
	slice1 = slice0 + 1.0;

	// lod alpha is remapped to ensure patches weld together properly. patches can vary significantly in shape (with
	// strips added and removed), and this variance depends on the base density of the mesh, as this defines the strip width.
	// using .15 as black and .85 as white should work for base mesh density as low as 16.
	const float BLACK_POINT = 0.15, WHITE_POINT = 0.85;
	lodAlpha = saturate((lodAlpha - BLACK_POINT) / (WHITE_POINT - BLACK_POINT));

	if (slice0 == 0.0)
	{
		// blend out lod0 when viewpoint gains altitude. we're using the global _MeshScaleLerp so check for LOD0 is necessary
		lodAlpha = min(lodAlpha + _MeshScaleLerp, 1.0);
	}
}

#define SampleLod(i_lodTextureArray, i_uv_slice) (i_lodTextureArray.SampleLevel(LODData_linear_clamp_sampler, i_uv_slice, 0.0))
#define SampleLodLevel(i_lodTextureArray, i_uv_slice, mips) (i_lodTextureArray.SampleLevel(LODData_linear_clamp_sampler, i_uv_slice, mips))

// Perform iteration to invert the displacement vector field - find position that displaces to query position.
float3 InvertDisplacement
(
	in const Texture2DArray i_oceanData,
	in CascadeParams i_cascadeParams,
	in uint i_sliceIndex,
	in const float3 i_positionWS,
	in const uint i_iterations
)
{
	float3 invertedDisplacedPosition = i_positionWS;
	for (uint i = 0; i < i_iterations; i++)
	{
		const float3 uv_slice = WorldToUV(invertedDisplacedPosition.xz, i_cascadeParams, i_sliceIndex);
		const float3 displacement = i_oceanData.SampleLevel(LODData_linear_clamp_sampler, uv_slice, 0.0).xyz;
		const float3 error = (invertedDisplacedPosition + displacement) - i_positionWS;
		invertedDisplacedPosition -= error;
	}

	return invertedDisplacedPosition;
}

// Clips using ocean surface clip data
void ApplyOceanClipSurface(in const float3 io_positionWS, in const float i_lodAlpha)
{
	// Sample shape textures - always lerp between 2 scales, so sample two textures
	// Sample weights. params.z allows shape to be faded out (used on last lod to support pop-less scale transitions)
	const float2 worldXZ = io_positionWS.xz;
	float wt_smallerLod = (1. - i_lodAlpha) * _CrestCascadeData[_LD_SliceIndex]._weight;
	float wt_biggerLod = (1. - wt_smallerLod) * _CrestCascadeData[_LD_SliceIndex + 1]._weight;

	// Sample clip surface data
	half clipValue = 0.0;
	if (wt_smallerLod > 0.001)
	{
		const float3 uv = WorldToUV(worldXZ, _CrestCascadeData[_LD_SliceIndex], _LD_SliceIndex);
		SampleClip(_LD_TexArray_ClipSurface, uv, wt_smallerLod, clipValue);
	}
	if (wt_biggerLod > 0.001)
	{
		const uint si = _LD_SliceIndex + 1;
		const float3 uv = WorldToUV(worldXZ, _CrestCascadeData[si], si);
		SampleClip(_LD_TexArray_ClipSurface, uv, wt_biggerLod, clipValue);
	}

	// Add 0.5 bias for LOD blending and texel resolution correction. This will help to tighten and smooth clipped edges
	clip(-clipValue + 0.5);
}

#endif // CREST_OCEAN_HELPERS_H
