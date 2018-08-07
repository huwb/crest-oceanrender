// ocean LOD data

// samplers and data associated with a LOD.
// _WD_Params: float4(world texel size, texture resolution, shape weight multiplier, 1 / texture resolution)
#define SHAPE_LOD_PARAMS(LODNUM) \
	uniform sampler2D _WD_Displacement_Sampler_##LODNUM; \
	uniform sampler2D _WD_OceanDepth_Sampler_##LODNUM; \
	uniform sampler2D _WD_Foam_Sampler_##LODNUM; \
	uniform float4 _WD_Params_##LODNUM; \
	uniform float3 _WD_Pos_Scale_##LODNUM; \
	uniform int _WD_LodIdx_##LODNUM;

// create two sets of LOD data. we always need only 2 textures - we're always lerping between two LOD levels
SHAPE_LOD_PARAMS( 0 )
SHAPE_LOD_PARAMS( 1 )

float2 WD_worldToUV(in float2 i_samplePos, in float2 i_centerPos, in float i_res, in float i_texelSize)
{
	return (i_samplePos - i_centerPos) / (i_texelSize * i_res) + 0.5;
}

float2 WD_uvToWorld(in float2 i_uv, in float2 i_centerPos, in float i_res, in float i_texelSize)
{
	return i_texelSize * i_res * (i_uv - 0.5) + i_centerPos;
}

#define DEPTH_BIAS 100.

// sample wave or terrain height, with smooth blend towards edges. computes normals and determinant and samples ocean depth.
// would equally apply to heights instead of displacements.
void SampleDisplacements(in sampler2D i_dispSampler, in float2 i_uv, in float wt, in float i_invRes, in float i_texelSize, inout float3 io_worldPos, inout float3 io_n)
{
	// TODO: find a more sensible place to put this.
	if (wt < 0.001)
		return;

	const float4 uv = float4(i_uv, 0., 0.);

	half4 s = tex2Dlod(i_dispSampler, uv);
	// get the vertex displacement
	half3 disp = s.xyz;
	// calculate the vertex normal
	float3 n; {
		float3 dd = float3(i_invRes, 0.0, i_texelSize);
		half3 disp_x = dd.zyy + tex2Dlod(i_dispSampler, uv + dd.xyyy).xyz;
		half3 disp_z = dd.yyz + tex2Dlod(i_dispSampler, uv + dd.yxyy).xyz;
		n = normalize(cross(disp_z - disp, disp_x - disp));
	}

	// weight the displacement and normal
	io_worldPos += wt * disp;
	io_n.xz += wt * n.xz;
}

void SampleFoam(in sampler2D i_foamSampler, in float2 i_uv, in float wt, inout half io_foam) {
	const float4 uv = float4(i_uv, 0., 0.);
	half4 s = tex2Dlod(i_foamSampler, uv);
	io_foam += wt * s.r;
}


// Geometry data
// x: A square is formed by 2 triangles in the mesh. Here x is square size
// yz: normalScrollSpeed0, normalScrollSpeed1
uniform float3 _GeomData;
uniform float3 _OceanCenterPosWorld;

float ComputeLodAlpha(float3 i_worldPos, float i_meshScaleAlpha)
{
	// taxicab distance from ocean center drives LOD transitions
	float2 offsetFromCenter = float2(abs(i_worldPos.x - _OceanCenterPosWorld.x), abs(i_worldPos.z - _OceanCenterPosWorld.z));
	float taxicab_norm = max(offsetFromCenter.x, offsetFromCenter.y);

	// interpolation factor to next lod (lower density / higher sampling period)
	float lodAlpha = taxicab_norm / _WD_Pos_Scale_0.z - 1.0;

	// lod alpha is remapped to ensure patches weld together properly. patches can vary significantly in shape (with
	// strips added and removed), and this variance depends on the base density of the mesh, as this defines the strip width.
	// using .15 as black and .85 as white should work for base mesh density as low as 16. TODO - make this automatic?
	const float BLACK_POINT = 0.15, WHITE_POINT = 0.85;
	lodAlpha = max((lodAlpha - BLACK_POINT) / (WHITE_POINT - BLACK_POINT), 0.);

	// blend out lod0 when viewpoint gains altitude
	lodAlpha = min(lodAlpha + i_meshScaleAlpha, 1.);

#if _DEBUGDISABLESMOOTHLOD_ON
	lodAlpha = 0.;
#endif

	return lodAlpha;
}

void SnapAndTransitionVertLayout(float i_meshScaleAlpha, inout float3 io_worldPos, out float o_lodAlpha)
{
	// see comments above on _GeomData
	const float SQUARE_SIZE_2 = 2.0*_GeomData.x, SQUARE_SIZE_4 = 4.0*_GeomData.x;

	// snap the verts to the grid
	// The snap size should be twice the original size to keep the shape of the eight triangles (otherwise the edge layout changes).
	io_worldPos.xz -= frac(unity_ObjectToWorld._m03_m23 / SQUARE_SIZE_2) * SQUARE_SIZE_2; // caution - sign of frac might change in non-hlsl shaders

	// compute lod transition alpha
	o_lodAlpha = ComputeLodAlpha(io_worldPos, i_meshScaleAlpha);

	// now smoothly transition vert layouts between lod levels - move interior verts inwards towards center
	float2 m = frac(io_worldPos.xz / SQUARE_SIZE_4); // this always returns positive
	float2 offset = m - 0.5;
	// check if vert is within one square from the center point which the verts move towards
	const float minRadius = 0.26; //0.26 is 0.25 plus a small "epsilon" - should solve numerical issues
	if (abs(offset.x) < minRadius) io_worldPos.x += offset.x * o_lodAlpha * SQUARE_SIZE_4;
	if (abs(offset.y) < minRadius) io_worldPos.z += offset.y * o_lodAlpha * SQUARE_SIZE_4;
}
