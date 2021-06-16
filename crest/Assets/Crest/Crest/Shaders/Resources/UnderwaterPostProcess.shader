// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

Shader "Crest/Underwater/Post Process"
{
	Properties
	{
		[Header(Debug Options)]
		[Toggle] _CompileShaderWithDebugInfo("Compile Shader With Debug Info (D3D11)", Float) = 0
	}
	SubShader
	{
		// No culling or depth
		Cull Off ZWrite Off ZTest Always

		Pass
		{
			CGPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag

			#pragma multi_compile_instancing

			// Use multi_compile because these keywords are copied over from the ocean material. With shader_feature,
			// the keywords would be stripped from builds. Unused shader variants are stripped using a build processor.
			#pragma multi_compile_local __ _SUBSURFACESCATTERING_ON
			#pragma multi_compile_local __ _SUBSURFACESHALLOWCOLOUR_ON
			#pragma multi_compile_local __ _TRANSPARENCY_ON
			#pragma multi_compile_local __ _CAUSTICS_ON
			#pragma multi_compile_local __ _SHADOWS_ON
			#pragma multi_compile_local __ _COMPILESHADERWITHDEBUGINFO_ON

			#pragma multi_compile_local __ CREST_MENISCUS

			#pragma multi_compile_local __ _PROJECTION_PERSPECTIVE _PROJECTION_ORTHOGRAPHIC

			#pragma multi_compile_local __ _FULL_SCREEN_EFFECT
			#pragma multi_compile_local __ _DEBUG_VIEW_OCEAN_MASK

			#if _COMPILESHADERWITHDEBUGINFO_ON
			#pragma enable_d3d11_debug_symbols
			#endif

			#include "UnityCG.cginc"
			#include "Lighting.cginc"

			#include "../OceanGlobals.hlsl"
			#include "../OceanInputsDriven.hlsl"
			#include "../OceanShaderData.hlsl"
			#include "../OceanHelpersNew.hlsl"
			#include "../OceanShaderHelpers.hlsl"
			#include "../FullScreenTriangle.hlsl"

			half3 _AmbientLighting;

			#include "../OceanEmission.hlsl"

			float _OceanHeight;
			float4x4 _InverseView;
			float4x4 _InvViewProjection;
			float4x4 _InvViewProjectionRight;
			float4 _HorizonPosNormal;
			float4 _HorizonPosNormalRight;
			half _DataSliceOffset;

			float3 _ClippingPlanePosition;
			float3 _ClippingPlaneNormal;

			struct Attributes
			{
				uint id : SV_VertexID;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 viewWS : TEXCOORD1;
				float4 viewDir: TEXCOORD2;

				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings Vert (Attributes input)
			{
				Varyings output;

				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_OUTPUT(Varyings, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				output.positionCS = GetFullScreenTriangleVertexPosition(input.id);
				output.uv = GetFullScreenTriangleTexCoord(input.id);

				// Compute world space view vector
				{
					const float2 pixelCS = output.uv * 2 - float2(1.0, 1.0);
#if CREST_HANDLE_XR
					const float4x4 InvViewProjection = unity_StereoEyeIndex == 0 ? _InvViewProjection : _InvViewProjectionRight;
#else
					const float4x4 InvViewProjection = _InvViewProjection;
#endif
					output.viewDir = mul(InvViewProjection, float4(pixelCS, 1.0, 1.0));
					const float3 pixelWS = output.viewDir.xyz / output.viewDir.w;
					output.viewWS = _WorldSpaceCameraPos - pixelWS;
				}

				return output;
			}

			UNITY_DECLARE_SCREENSPACE_TEXTURE(_CrestCameraColorTexture);
			UNITY_DECLARE_SCREENSPACE_TEXTURE(_CrestOceanMaskTexture);
			UNITY_DECLARE_SCREENSPACE_TEXTURE(_CrestOceanMaskDepthTexture);

			half3 ApplyUnderwaterEffect(half3 sceneColour, const float sceneZ01, const float sceneZ, const half3 view, bool isOceanSurface)
			{
				const float3 lightDir = _WorldSpaceLightPos0.xyz;

				half3 scatterCol = 0.0;
				int sliceIndex = clamp(_DataSliceOffset, 0, _SliceCount - 2);
				{
					float3 dummy;
					half sss = 0.0;
					// Offset slice so that we dont get high freq detail. But never use last lod as this has crossfading.
					const float3 uv_slice = WorldToUV(_WorldSpaceCameraPos.xz, _CrestCascadeData[sliceIndex], sliceIndex);
					SampleDisplacements(_LD_TexArray_AnimatedWaves, uv_slice, 1.0, dummy, sss);

					// depth and shadow are computed in ScatterColour when underwater==true, using the LOD1 texture.
					const float depth = 0.0;
					const half shadow = 1.0;
					{
						const float meshScaleLerp = _CrestPerCascadeInstanceData[sliceIndex]._meshScaleLerp;
						const float baseCascadeScale = _CrestCascadeData[0]._scale;
						scatterCol = ScatterColour(_AmbientLighting, depth, _WorldSpaceCameraPos, lightDir, view, shadow, true, true, sss, meshScaleLerp, baseCascadeScale, _CrestCascadeData[sliceIndex]);
					}
				}

#if _CAUSTICS_ON
				if (sceneZ01 != 0.0 && !isOceanSurface)
				{
					ApplyCaustics(view, lightDir, sceneZ, _Normals, true, sceneColour, _CrestCascadeData[sliceIndex], _CrestCascadeData[sliceIndex + 1]);
				}
#endif // _CAUSTICS_ON

				return lerp(sceneColour, scatterCol, saturate(1.0 - exp(-_DepthFogDensity.xyz * sceneZ)));
			}

			fixed4 Frag (Varyings input) : SV_Target
			{
				// We need this when sampling a screenspace texture.
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				float3 viewWS;
				float farPlanePixelHeight;

#if !_FULL_SCREEN_EFFECT
				// The horizon line is the intersection between the far plane and the ocean plane. The pos and normal of this
				// intersection line is passed in.
#if CREST_HANDLE_XR
				const bool isBelowHorizon = unity_StereoEyeIndex == 0 ?
					dot(input.uv - _HorizonPosNormal.xy, _HorizonPosNormal.zw) > 0.0 :
					dot(input.uv - _HorizonPosNormalRight.xy, _HorizonPosNormalRight.zw) > 0.0;
#else // CREST_HANDLE_XR
				const bool isBelowHorizon = dot(input.uv - _HorizonPosNormal.xy, _HorizonPosNormal.zw) > 0.0;
#endif // CREST_HANDLE_XR
#else // !_FULL_SCREEN_EFFECT
				const bool isBelowHorizon = true;
#endif // !_FULL_SCREEN_EFFECT

				const float2 uvScreenSpace = UnityStereoTransformScreenSpaceTex(input.uv);

				half3 sceneColour = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CrestCameraColorTexture, uvScreenSpace).rgb;

				float sceneZ01 = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CameraDepthTexture, uvScreenSpace).x;

				float mask = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CrestOceanMaskTexture, uvScreenSpace).x;
				const float oceanDepth01 = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CrestOceanMaskDepthTexture, uvScreenSpace);
				bool isOceanSurface = mask != UNDERWATER_MASK_NO_MASK && (sceneZ01 < oceanDepth01);
				bool isUnderwater = mask == UNDERWATER_MASK_WATER_SURFACE_BELOW || (isBelowHorizon && mask != UNDERWATER_MASK_WATER_SURFACE_ABOVE);
				sceneZ01 = isOceanSurface ? oceanDepth01 : sceneZ01;
				float sceneZ = CrestLinearEyeDepth(sceneZ01);

				float wt = 1.0;

				// Clipping plane.
				{
					// Get distance to plane.
					// https://www.habrador.com/tutorials/math/4-plane-ray-intersection/
					float3 planePosition = _ClippingPlanePosition;
					float3 planeNormal = -normalize(_ClippingPlaneNormal);
					float3 viewNormal = -normalize(input.viewWS);
					float denominator = dot(viewNormal, planeNormal);
					float planePointDistance = dot(planePosition - _WorldSpaceCameraPos, planeNormal) / denominator;

					// Convert depth to distance.
					// https://forum.unity.com/threads/converting-depth-values-to-distances-from-z-buffer.921929/#post-6034625
					float3 viewPosition = (input.viewDir.xyz / input.viewDir.w) * Linear01Depth(sceneZ01);
					float3 worldPosition = mul (_InverseView, float4 (viewPosition, 1));
					float depthDistance = distance(_WorldSpaceCameraPos, worldPosition);

					if (denominator > 0.00001 && depthDistance < planePointDistance)
					{
						// Before the plane's front face.
						return half4(sceneColour, 1.0);
					}
					else if (denominator < 0 && depthDistance > planePointDistance)
					{
						// After the plane's back face.
						sceneZ = planePointDistance;
					}
				}

#if CREST_MENISCUS
				// Detect water to no water transitions which happen if mask values on below pixels are less than this mask
				if (mask <= 1.0)
				{
					// Looks at pixels below this pixel and if there is a transition from above to below, darken the pixel
					// to emulate a meniscus effect. It does a few to get a thicker line than 1 pixel. The line it produces is
					// smooth on the top side and sharp at the bottom. It might be possible to detect where the edge is and do
					// a calculation to get it smooth both above and below, but might be more complex.
					float wt_mul = 0.9;
					float4 dy = float4(0.0, -1.0, -2.0, -3.0) / _ScreenParams.y;
					wt *= (UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CrestOceanMaskTexture, uvScreenSpace + dy.xy).x > mask) ? wt_mul : 1.0;
					wt *= (UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CrestOceanMaskTexture, uvScreenSpace + dy.xz).x > mask) ? wt_mul : 1.0;
					wt *= (UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CrestOceanMaskTexture, uvScreenSpace + dy.xw).x > mask) ? wt_mul : 1.0;
				}
#endif // CREST_MENISCUS

#if _DEBUG_VIEW_OCEAN_MASK
				if (!isOceanSurface)
				{
					return float4(sceneColour * float3(isUnderwater * 0.5, (1.0 - isUnderwater) * 0.5, 1.0), 1.0);
				}
				else
				{
					return float4(sceneColour * float3(mask == UNDERWATER_MASK_WATER_SURFACE_ABOVE, mask == UNDERWATER_MASK_WATER_SURFACE_BELOW, 0.0), 1.0);
				}
#else
				if (isUnderwater)
				{
					const half3 view = normalize(input.viewWS);
					sceneColour = ApplyUnderwaterEffect(sceneColour, sceneZ01, sceneZ, view, false);
				}

				return half4(wt * sceneColour, 1.0);
#endif // _DEBUG_VIEW_OCEAN_MASK
			}
			ENDCG
		}
	}
}
