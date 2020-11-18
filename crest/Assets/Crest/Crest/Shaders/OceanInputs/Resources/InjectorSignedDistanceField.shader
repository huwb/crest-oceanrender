// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

// Renders signed distance field data into the depth cache without setting a
// depth, this allows a region to have waves empty-of oriented waves whilst
// having waves oriented towards it. Eg. inside the cove of a bay.
Shader "Crest/Inputs/Depth/Inject Signed Distance Field From Geometry"
{
	SubShader
	{
		Pass
		{
			CGPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag

			#include "UnityCG.cginc"

			#include "../../OceanGlobals.hlsl"

			struct Attributes
			{
				float3 positionOS : POSITION;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 worldPosXZ : TEXCOORD1;
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				output.positionCS = UnityObjectToClipPos(input.positionOS);

				float3 worldPos = mul(unity_ObjectToWorld, float4(input.positionOS, 1.0));
				output.worldPosXZ = worldPos.xz;
				return output;
			}

			float2 Frag(Varyings input) : SV_Target
			{
				float2 position = input.worldPosXZ;
				return float2(position.x, position.y);
			}
			ENDCG
		}
	}
}
