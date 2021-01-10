// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

Shader "Crest/Underwater Meniscus"
{
	Properties
	{
		_MeniscusWidth("Meniscus Width", Range(0.0, 100.0)) = 1.0
	}

	SubShader
	{
		Tags{ "LightMode" = "ForwardBase" "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }

		Pass
		{
			// Could turn this off, and it would allow the ocean surface to render through it
			ZWrite Off
			//Blend SrcAlpha OneMinusSrcAlpha
			Blend DstColor Zero

			CGPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag

			#pragma multi_compile_instancing

			#include "UnityCG.cginc"
			#include "Lighting.cginc"

			#include "../OceanGlobals.hlsl"
			#include "../OceanInputsDriven.hlsl"
			#include "../OceanHelpersNew.hlsl"
			#include "UnderwaterShared.hlsl"

			#define MAX_OFFSET 5.0

			CBUFFER_START(UnderwaterAdditional)
			float _MeniscusWidth;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv : TEXCOORD0;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
				half4 foam_screenPos : TEXCOORD1;
				half4 grabPos : TEXCOORD2;
				float3 worldPos : TEXCOORD3;

				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings Vert(Attributes input)
			{
				Varyings o;

				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_OUTPUT(Varyings, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				// view coordinate frame for camera
				const float3 right = unity_CameraToWorld._11_21_31;
				const float3 up = unity_CameraToWorld._12_22_32;
				const float3 forward = unity_CameraToWorld._13_23_33;

				const float3 nearPlaneCenter = _WorldSpaceCameraPos + forward * _ProjectionParams.y * 1.001;
				// Spread verts across the near plane.
				const float aspect = _ScreenParams.x / _ScreenParams.y;
				o.worldPos = nearPlaneCenter
					+ 2.6 * unity_CameraInvProjection._m11 * aspect * right * input.positionOS.x * _ProjectionParams.y
					+ up * input.positionOS.z * _ProjectionParams.y;

				if (abs(forward.y) < CREST_MAX_UPDOWN_AMOUNT)
				{
					float seaLevel = _OceanCenterPosWorld.y;
					{
						float3 uv_slice = WorldToUV(_WorldSpaceCameraPos.xz, _CrestCascadeData[_LD_SliceIndex], 0.0);
						float waterDepth = 0.0;
						SampleSeaDepth(_LD_TexArray_SeaFloorDepth, uv_slice, 1.0, waterDepth, seaLevel);
					}

					o.worldPos += min(IntersectRayWithWaterSurface(seaLevel, o.worldPos, up, _CrestCascadeData[_LD_SliceIndex]), MAX_OFFSET) * up;

					const float offset = 0.001 * _ProjectionParams.y * _MeniscusWidth;
					if (input.positionOS.z > 0.49)
					{
						o.worldPos += offset * up;
					}
					else
					{
						o.worldPos -= offset * up;
					}
				}
				else
				{
					// kill completely if looking up/down
					o.worldPos *= 0.0;
				}

				o.positionCS = mul(UNITY_MATRIX_VP, float4(o.worldPos, 1.0));
				o.positionCS.z = o.positionCS.w;

				o.foam_screenPos.yzw = ComputeScreenPos(o.positionCS).xyw;
				o.foam_screenPos.x = 0.0;
				o.grabPos = ComputeGrabScreenPos(o.positionCS);

				o.uv = input.uv;

				return o;
			}
			
			half4 Frag(Varyings input) : SV_Target
			{
				const half3 col = 1.3*half3(0.37, 0.4, 0.5);
				float alpha = abs(input.uv.y - 0.5);
				alpha = pow(smoothstep(0.5, 0.0, alpha), 0.5);
				return half4(lerp((half3)1.0, col, alpha), alpha);
			}
			ENDCG
		}
	}
}
