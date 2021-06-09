// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

Shader "Crest/Underwater/Ocean Occluder Mask"
{
	SubShader
	{
		Pass
		{
			// We always disable culling when rendering ocean mask, as we only
			// use it for underwater rendering features.
			// TODO(TRC):Now (can we make this toggleable?) (The calling should
			// match the culling setting of the source shader?)
			Cull Back
			CGPROGRAM

			#pragma vertex Vert
			#pragma fragment Frag
			// for VFACE
			#pragma target 3.0

			#include "UnityCG.cginc"
			#include "../OceanConstants.hlsl"

			struct Attributes
			{
				float4 vertex : POSITION;
			};

			struct Varyings
			{
				float4 vertex : SV_POSITION;
				float4 grabPos : TEXCOORD0;
			};

			float _OceanOccluderType;
			sampler2D _UnderwaterMaskTex;

			Varyings Vert (Attributes input)
			{
				Varyings output;
				output.vertex = UnityObjectToClipPos(input.vertex);
				output.grabPos = ComputeGrabScreenPos(output.vertex);
				return output;
			}

			fixed4 Frag (const Varyings input) : SV_Target
			{

				return (half4) _OceanOccluderType;
			}
			ENDCG
		}
	}
}
