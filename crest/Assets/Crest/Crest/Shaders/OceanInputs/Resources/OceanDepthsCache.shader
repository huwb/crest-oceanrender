// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

// Draw cached depths into current frame ocean depth data
Shader "Crest/Inputs/Depth/Cached Depths"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
	}

	SubShader
	{
		Pass
		{
			// Min blending to take the min of all depths. Similar in spirit to zbuffer'd visibility when viewing from top down.
			// To confuse matters further, ocean depth is now more like 'sea floor altitude' - a height above a deep water value,
			// so values are increasing in Y and we need to take the MAX of all depths.
			BlendOp Min
			ColorMask RG

			CGPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag
		
			#include "UnityCG.cginc"
			#include "../../OceanGlobals.hlsl"

			sampler2D _MainTex;

			CBUFFER_START(CrestPerOceanInput)
			float4 _MainTex_ST;
			CBUFFER_END

			struct Attributes
			{
				float3 positionOS : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct Varyings
			{
				float4 position : SV_POSITION;
				float3 uv_worldY : TEXCOORD0;
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				output.position = UnityObjectToClipPos(input.positionOS);
				output.uv_worldY.xy = TRANSFORM_TEX(input.uv, _MainTex);
				output.uv_worldY.z = mul(unity_ObjectToWorld, float4(input.positionOS, 1.0)).y;
				return output;
			}

			float2 Frag(Varyings input) : SV_Target
			{
				float cachedDepth = tex2D(_MainTex, input.uv_worldY.xy).x;
				float seaLevelOffset = input.uv_worldY.z - _OceanCenterPosWorld.y;
				// Hack: Write -seaLevelOffset, as BlendOp is set to Min above. This assumes then that
				// offsets are only ever above sea level, not below.
				return float2(cachedDepth, -seaLevelOffset);
			}
			ENDCG
		}
	}
}
