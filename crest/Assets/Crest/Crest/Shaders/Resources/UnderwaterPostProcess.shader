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

			half _DataSliceOffset;
			half3 _CrestAmbientLighting;

			#include "../OceanGlobals.hlsl"
			#include "../OceanInputsDriven.hlsl"
			#include "../OceanShaderData.hlsl"
			#include "../OceanHelpersNew.hlsl"
			#include "../OceanShaderHelpers.hlsl"
			#include "../OceanOccluderHelpers.hlsl"
			#include "../FullScreenTriangle.hlsl"

			#include "../OceanEmission.hlsl"
			#include "../UnderwaterHelpers.hlsl"

			float _OceanHeight;
			float4x4 _CrestInvViewProjection;
			float4x4 _CrestInvViewProjectionRight;
			float4 _CrestHorizonPosNormal;
			float4 _CrestHorizonPosNormalRight;

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
					const float4x4 InvViewProjection = unity_StereoEyeIndex == 0 ? _CrestInvViewProjection : _CrestInvViewProjectionRight;
#else
					const float4x4 InvViewProjection = _CrestInvViewProjection;
#endif
					const float4 pixelWS_H = mul(InvViewProjection, float4(pixelCS, 1.0, 1.0));
					const float3 pixelWS = pixelWS_H.xyz / pixelWS_H.w;
					output.viewWS = _WorldSpaceCameraPos - pixelWS;
				}

				return output;
			}

			UNITY_DECLARE_SCREENSPACE_TEXTURE(_CrestCameraColorTexture);
			UNITY_DECLARE_SCREENSPACE_TEXTURE(_CrestOceanMaskTexture);
			UNITY_DECLARE_SCREENSPACE_TEXTURE(_CrestOceanMaskDepthTexture);

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
					dot(input.uv - _CrestHorizonPosNormal.xy, _CrestHorizonPosNormal.zw) > 0.0 :
					dot(input.uv - _CrestHorizonPosNormalRight.xy, _CrestHorizonPosNormalRight.zw) > 0.0;
#else // CREST_HANDLE_XR
				const bool isBelowHorizon = dot(input.uv - _CrestHorizonPosNormal.xy, _CrestHorizonPosNormal.zw) > 0.0;
#endif // CREST_HANDLE_XR
#else // !_FULL_SCREEN_EFFECT
				const bool isBelowHorizon = true;
#endif // !_FULL_SCREEN_EFFECT

				const float2 uvScreenSpace = UnityStereoTransformScreenSpaceTex(input.uv);

				half3 sceneColour = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CrestCameraColorTexture, uvScreenSpace).rgb;

				float sceneZ01 = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CameraDepthTexture, uvScreenSpace).x;

				// We need to have a small amount of depth tolerance to handle the
				// fact that we can have general oceanMask filter which will be rendered in the scene
				// and have their depth in the regular depth buffer.
				const float oceanDepthTolerance = 0.000045;

				float oceanMask = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CrestOceanMaskTexture, uvScreenSpace).x;
				const float oceanDepth01 = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CrestOceanMaskDepthTexture, uvScreenSpace).x;
				bool isUnderwater = oceanMask == UNDERWATER_MASK_WATER_SURFACE_BELOW || (isBelowHorizon  && oceanMask != UNDERWATER_MASK_WATER_SURFACE_ABOVE);

				// If we have a view into underwater through a window, we need to make sure to only apply fog for the distance starting from behind it
				if(isUnderwater)
				{
					oceanMask = PostProcessHandleOccluderMask(uvScreenSpace, oceanMask, oceanDepth01);
					isUnderwater = oceanMask != UNDERWATER_MASK_WATER_SURFACE_ABOVE;
				}

				// Ocean surface check is used avoid drawing caustics on the water or masked surface
				bool disableCaustics = oceanMask != UNDERWATER_MASK_NO_MASK && (sceneZ01 <= (oceanDepth01 + oceanDepthTolerance));

				sceneZ01 = disableCaustics ? oceanDepth01 : sceneZ01;

				// TODO: Should this affect disableCaustics? Do we want caustics on windows?
				// Prevents ocean surface from being rendered behind windows when underwater.
				const float occluderDepth01 = tex2D(_CrestOceanOccluderMaskDepthTexture, uvScreenSpace).x;
				sceneZ01 = occluderDepth01 > sceneZ01 ? occluderDepth01 : sceneZ01;

				const float sceneZ = CrestLinearEyeDepth(sceneZ01);

				float wt = 1.0;

#if CREST_MENISCUS
				// Detect water to no water transitions which happen if oceanMask values on below pixels are less than this oceanMask
				if (oceanMask <= 1.0)
				{
					// Looks at pixels below this pixel and if there is a transition from above to below, darken the pixel
					// to emulate a meniscus effect. It does a few to get a thicker line than 1 pixel. The line it produces is
					// smooth on the top side and sharp at the bottom. It might be possible to detect where the edge is and do
					// a calculation to get it smooth both above and below, but might be more complex.
					float wt_mul = 0.9;
					float4 dy = float4(0.0, -1.0, -2.0, -3.0) / _ScreenParams.y;
					wt *= (UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CrestOceanMaskTexture, uvScreenSpace + dy.xy).x > oceanMask) ? wt_mul : 1.0;
					wt *= (UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CrestOceanMaskTexture, uvScreenSpace + dy.xz).x > oceanMask) ? wt_mul : 1.0;
					wt *= (UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CrestOceanMaskTexture, uvScreenSpace + dy.xw).x > oceanMask) ? wt_mul : 1.0;
				}
#endif // CREST_MENISCUS

#if _DEBUG_VIEW_OCEAN_MASK
				if(!disableCaustics)
				{
					return float4(sceneColour * float3(isUnderwater * 0.5, (1.0 - isUnderwater) * 0.5, 1.0), 1.0);
				}
				else
				{
					return float4(sceneColour * float3(oceanMask == UNDERWATER_MASK_WATER_SURFACE_ABOVE, oceanMask == UNDERWATER_MASK_WATER_SURFACE_BELOW, 0.0), 1.0);
				}
#else
				if (isUnderwater)
				{
					const half3 view = normalize(input.viewWS);
					sceneColour = ApplyUnderwaterEffect(
						_LD_TexArray_AnimatedWaves,
						_Normals,
						_WorldSpaceCameraPos,
						_CrestAmbientLighting,
						sceneColour,
						sceneZ,
						sceneZ,
						view,
						_DepthFogDensity,
						disableCaustics
					);
				}

				return half4(wt * sceneColour, 1.0);
#endif // _DEBUG_VIEW_OCEAN_MASK
			}
			ENDCG
		}
	}
}
