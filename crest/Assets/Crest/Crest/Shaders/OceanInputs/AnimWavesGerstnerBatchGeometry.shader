// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

// Renders gerstner waves from geometry. Allows localised wave areas. Can fade waves based on UVs - fades to 0
// as U or V approach 0 or 1, with configurable feather width. Can also take weight from vertex colour (red channel).
Shader "Crest/Inputs/Animated Waves/Gerstner Batch Geometry"
{
	Properties
	{
		[Toggle] _WeightFromVertexColourRed("Weight from vertex colour (red channel)", Float) = 0
		[Toggle] _FeatherAtUVExtents("Feather at UV extents", Float) = 0
		_FeatherWidth("Feather width", Range(0.001, 0.5)) = 0.1
	}

	SubShader
	{
		Pass
		{
			Blend One One
			ZWrite Off
			ZTest Always
			Cull Off

			CGPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag
			#pragma multi_compile_local __ CREST_DIRECT_TOWARDS_POINT_INTERNAL
			#pragma shader_feature_local _WEIGHTFROMVERTEXCOLOURRED_ON
			#pragma shader_feature_local _FEATHERATUVEXTENTS_ON

			#include "UnityCG.cginc"

			#include "../OceanGlobals.hlsl"
			#include "../OceanInputsDriven.hlsl"
			#include "../OceanHelpersNew.hlsl"

			#include "GerstnerShared.hlsl"

			CBUFFER_START(GerstnerPerMaterial)
			half _FeatherWidth;
			float3 _DisplacementAtInputPosition;
			CBUFFER_END

			struct Attributes
			{
				float3 positionOS : POSITION;
				float2 uv : TEXCOORD0;
#if _WEIGHTFROMVERTEXCOLOURRED_ON
				float3 colour : COLOR0;
#endif
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float4 worldPosXZ_uv : TEXCOORD0;
				float4 uv_slice_wt : TEXCOORD1;
#if _WEIGHTFROMVERTEXCOLOURRED_ON
				float weight : TEXCOORD2;
#endif
			};

			Varyings Vert(Attributes input)
			{
				Varyings o;

				float3 worldPos = mul(unity_ObjectToWorld, float4(input.positionOS, 1.0)).xyz;
				// Correct for displacement
				worldPos.xz -= _DisplacementAtInputPosition.xz;

				o.positionCS = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));

				o.worldPosXZ_uv.xy = worldPos.xz;
				o.worldPosXZ_uv.zw = input.uv;

				o.uv_slice_wt.xyz = WorldToUV(o.worldPosXZ_uv.xy, _LD_Pos_Scale[_LD_SliceIndex], _LD_Params[_LD_SliceIndex], _LD_SliceIndex);
				o.uv_slice_wt.w = 1.0;

#if _WEIGHTFROMVERTEXCOLOURRED_ON
				o.weight = input.colour.x;
#endif

				return o;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				float wt = 1.0;

#if _WEIGHTFROMVERTEXCOLOURRED_ON
				wt *= input.weight;
#endif

#if _FEATHERATUVEXTENTS_ON
				float2 offset = abs(input.worldPosXZ_uv.zw - 0.5);
				float r_l1 = max(offset.x, offset.y);
				wt *= saturate(1.0 - (r_l1 - (0.5 - _FeatherWidth)) / _FeatherWidth);
#endif
				// sample ocean depth (this render target should 1:1 match depth texture, so UVs are trivial)
				const half depth = _LD_TexArray_SeaFloorDepth.Sample(LODData_linear_clamp_sampler, input.uv_slice_wt.xyz).x;
				return wt * ComputeGerstner(input.worldPosXZ_uv.xy, input.uv_slice_wt.xyz, depth);
			}
			ENDCG
		}
	}
}
