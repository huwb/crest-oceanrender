// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

// Adds Gerstner waves everywhere. Must be given batch prepared by ShapeGerstnerBatched.cs.
Shader "Hidden/Crest/Inputs/Animated Waves/Gerstner Batch Global"
{
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
			#pragma multi_compile_local __ CREST_SDF_SHORELINES

			#include "UnityCG.cginc"

			#include "../../OceanGlobals.hlsl"
			#include "../../OceanInputsDriven.hlsl"
			#include "../../OceanHelpersNew.hlsl"

			#include "../GerstnerShared.hlsl"

			struct Attributes
			{
				float3 positionOS : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 worldPosXZ : TEXCOORD0;
				float3 uv_slice : TEXCOORD1;
			};

			Varyings Vert(Attributes input)
			{
				Varyings o;

				o.positionCS = float4(input.positionOS.xy, 0.0, 0.5);

#if UNITY_UV_STARTS_AT_TOP // https://docs.unity3d.com/Manual/SL-PlatformDifferences.html
				o.positionCS.y = -o.positionCS.y;
#endif

				const float2 worldXZ = UVToWorld(input.uv, _LD_SliceIndex, _LD_Pos_Scale[_LD_SliceIndex], _LD_Params[_LD_SliceIndex]);
				o.worldPosXZ = worldXZ;
				o.uv_slice = float3(input.uv, _LD_SliceIndex);

				return o;
			}


			void Quantize(float x, float dx, out float x0, out float alpha)
			{
				alpha = frac(x / dx);
				x0 = x - alpha * dx;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				// sample ocean depth (this render target should 1:1 match depth texture, so UVs are trivial)
				half2 depth_distance = _LD_TexArray_SeaFloorDepth.Sample(LODData_linear_clamp_sampler, input.uv_slice).xy;


				float4 result = ComputeGerstner(input.worldPosXZ, input.uv_slice, depth_distance.x);

#if CREST_SDF_SHORELINES
				// Calculate gradient by offsetting samples and normalising the resultant vector
				// https://github.com/electricsquare/raymarching-workshop#diffuse-term
				// Another option would be to store the gradient directtly in the channels of the SeaFloorDepth texture
				// - this would have been more accurate, but results in extra bandwidth usage and clashes with data that
				// we will need to store for local water-bodies. eps_zero was picked as an offset that worked best after
				// trial and error. If it's too small - you won't be able to pickup the actual gradient - if it's too
				// large, there will be a bias and rapid changes in the gradient can cause self-intersection if it
				// occurs close to the shoreline.
				float2 eps_zero = float2(0.00005, 0.0);
				half sdf1 = _LD_TexArray_SeaFloorDepth.Sample(LODData_linear_clamp_sampler, input.uv_slice + eps_zero.xyy).y;
				half sdf2 = _LD_TexArray_SeaFloorDepth.Sample(LODData_linear_clamp_sampler, input.uv_slice + eps_zero.yxy).y;
				half2 directionToShore = depth_distance.y - half2(sdf1, sdf2);
				{
					// Safely normalise directionToShore
					// See: https://github.com/Unity-Technologies/Graphics/blob/fbec6739bb9d7b115beb0c06161bd99f6f05d390/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl#L1156
					const float floatMin = 1.175494351e-38; // Minimum normalized positive floating-point number
					float safeOneOverDistance = rsqrt(max(floatMin, dot(directionToShore, directionToShore)));
					directionToShore = directionToShore * safeOneOverDistance;
				}
				result += ComputeShorelineGerstner(input.worldPosXZ, input.uv_slice, depth_distance, directionToShore);
#endif

				return result;
			}
			ENDCG
		}
	}
}
