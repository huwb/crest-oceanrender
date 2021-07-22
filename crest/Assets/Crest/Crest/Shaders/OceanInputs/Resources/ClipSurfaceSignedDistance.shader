// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

// Renders a signed distance shape into the clip surface texture.

Shader "Crest/Inputs/Clip Surface/Signed Distance"
{
	Properties
	{
		[KeywordEnum(Sphere, Box)] _Shape("Shape", Float) = 0.0
	}

	SubShader
	{
		ZWrite Off
		ColorMask R
		BlendOp Max

		Pass
		{
			Cull Back

			CGPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag

			#pragma shader_feature_local _ _SHAPE_SPHERE _SHAPE_BOX

			#include "UnityCG.cginc"
			#include "../../OceanGlobals.hlsl"
			#include "../../OceanInputsDriven.hlsl"
			#include "../../OceanVertHelpers.hlsl"
			#include "../../OceanHelpersNew.hlsl"
			#include "../../OceanHelpersDriven.hlsl"

			CBUFFER_START(CrestPerOceanInput)
			uint _DisplacementSamplingIterations;
			float4x4 _SignedDistanceShapeMatrix;
			CBUFFER_END

			struct Attributes
			{
				float3 positionOS : POSITION;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
			};

			float signedDistanceSphere(float3 position)
			{
				// Apply matrix and get distance from center.
				return length(mul(_SignedDistanceShapeMatrix, float4(position, 1.0)).xyz);
			}

			float signedDistanceBox(float3 position)
			{
				// Apply matrix and restrict to one quadrant of a box.
				position = abs(mul(_SignedDistanceShapeMatrix, float4(position, 1.0)));
				// Get furthest distance from center.
				return max(position.x, max(position.y, position.z));
			}

			Varyings Vert(Attributes input)
			{
				Varyings output;

				output.positionCS = UnityObjectToClipPos(input.positionOS);
				output.positionWS = mul(unity_ObjectToWorld, float4(input.positionOS, 1.0));

				return output;
			}

			float4 Frag(Varyings input) : SV_Target
			{
				float3 positionWS = input.positionWS;

				float3 surfacePositionWS = SampleOceanDataDisplacedToWorldPosition
				(
					_LD_TexArray_AnimatedWaves,
					positionWS,
					_DisplacementSamplingIterations
				);

				// Move to sea level.
				surfacePositionWS.y += _OceanCenterPosWorld.y;

				// We only need the height as clip surface is sampled at the displaced position in the ocean shader.
				positionWS.y += surfacePositionWS.y;

#if _SHAPE_BOX
				float signedDistance = signedDistanceBox(positionWS);
#else
				float signedDistance = signedDistanceSphere(positionWS);
#endif

				return float4(1.0 - signedDistance, 0.0, 0.0, 1.0);
			}
			ENDCG
		}
	}
}
