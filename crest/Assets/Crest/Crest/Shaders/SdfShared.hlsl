// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

// Setting this to 0.0 means that gometry at exactly the origin won't be handled
// super-gracefully - but it would only affect a single-pixel in the worst-case
// and would doubtfully be noticable anyway.
const float2 CREST_SDF_UNINITIALISED_POSITION = float2(1.#INF, 1.#INF);

bool IsUninitialisedPosition(in float2 position)
{
	return isinf(position.x);
}

// Convert compute shader id to uv texture coordinates
float2 IDtoUV(in float2 i_id, in float i_width, in float i_height)
{
	return (((i_id + 0.5) / float2(i_width, i_height)) - float2(0.5, 0.5)) * 2;
}

float2 IDtoWorld(in uint2 id, in float textureResolution, in float4x4 projectionToWorld)
{
	float2 uv = IDtoUV(id, textureResolution, textureResolution);
	return mul(projectionToWorld, float4(uv, 0, 1)).xz;
}
