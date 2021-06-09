// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

Shader "Crest/Ocean"
{
	Properties
	{
		[Header(Normals)]
		// Strength of the final surface normal (includes both wave normal and normal map)
		_NormalsStrengthOverall( "Overall Normal Strength", Range( 0.0, 1.0 ) ) = 1.0
		// Whether to add normal detail from a texture. Can be used to add visual detail to the water surface
		[Toggle] _ApplyNormalMapping("Use Normal Map", Float) = 1
		// Normal map texture (should be set to Normals type in the properties)
		[NoScaleOffset] _Normals("Normal Map", 2D) = "bump" {}
		// Scale of normal map texture
		_NormalsScale("Normal Map Scale", Range(0.01, 200.0)) = 40.0
		// Strength of normal map influence
		_NormalsStrength("Normal Map Strength", Range(0.01, 2.0)) = 0.36

		// Base light scattering settings which give water colour
		[Header(Scattering)]
		// Base colour when looking straight down into water
		_Diffuse("Scatter Colour Base", Color) = (0.0, 0.0124, 0.566, 1.0)
		// Base colour when looking into water at shallow/grazing angle
		_DiffuseGrazing("Scatter Colour Grazing", Color) = (0.184, 0.393, 0.519, 1)
		// Changes colour in shadow. Requires 'Create Shadow Data' enabled on OceanRenderer script.
		[Toggle] _Shadows("Shadowing", Float) = 0
		// Base colour in shadow
		_DiffuseShadow("Scatter Colour Shadow", Color) = (0.0, 0.356, 0.565, 1.0)

		[Header(Shallow Scattering)]
		// Enable light scattering in shallow water
		[Toggle] _SubSurfaceShallowColour("Enable", Float) = 1
		// Colour in shallow water
		_SubSurfaceShallowCol("Scatter Colour Shallow", Color) = (0.552, 1.0, 1.0, 1.0)
		// Max depth that is considered 'shallow'
		_SubSurfaceDepthMax("Scatter Colour Shallow Depth Max", Range(0.01, 50.0)) = 10.0
		// Fall off of shallow scattering
		_SubSurfaceDepthPower("Scatter Colour Shallow Depth Falloff", Range(0.01, 10.0)) = 2.5
		// Shallow water colour in shadow (see comment on Shadowing param above)
		_SubSurfaceShallowColShadow("Scatter Colour Shallow Shadow", Color) = (0.144, 0.226, 0.212, 1)

		[Header(Subsurface Scattering)]
		// Whether to to emulate light scattering through the water volume
		[Toggle] _SubSurfaceScattering("Enable", Float) = 1
		// Colour tint for primary light contribution
		_SubSurfaceColour("SSS Tint", Color) = (0.0, 0.48, 0.36)
		// Amount of primary light contribution that always comes in
		_SubSurfaceBase("SSS Intensity Base", Range(0.0, 4.0)) = 1.0
		// Primary light contribution in direction of light to emulate light passing through waves
		_SubSurfaceSun("SSS Intensity Sun", Range(0.0, 10.0)) = 4.5
		// Fall-off for primary light scattering to affect directionality
		_SubSurfaceSunFallOff("SSS Sun Falloff", Range(1.0, 16.0)) = 5.0

		// Reflection properites
		[Header(Reflection Environment)]
		// Strength of specular lighting response
		_Specular("Specular", Range(0.0, 1.0)) = 1.0
		// Controls blurriness of reflection
		_Roughness("Roughness", Range(0.0, 1.0)) = 0.0
		// Controls harshness of Fresnel behaviour
		_FresnelPower("Fresnel Power", Range(1.0, 20.0)) = 5.0
		// Index of refraction of air. Can be increased to almost 1.333 to increase visibility up through water surface.
		_RefractiveIndexOfAir("Refractive Index of Air", Range(1.0, 2.0)) = 1.0
		// Index of refraction of water. Typically left at 1.333.
		_RefractiveIndexOfWater("Refractive Index of Water", Range(1.0, 2.0)) = 1.333
		// Dynamically rendered 'reflection plane' style reflections. Requires OceanPlanarReflection script added to main camera.
		[Toggle] _PlanarReflections("Planar Reflections", Float) = 0
		// How much the water normal affects the planar reflection
		_PlanarReflectionNormalsStrength("Planar Reflections Distortion", Float) = 1
		// Multiplier to adjust how intense the reflection is
		_PlanarReflectionIntensity("Planar Reflection Intensity", Range(0.0, 1.0)) = 1.0
		// Whether to use an overridden reflection cubemap (provided in the next property)
		[Toggle] _OverrideReflectionCubemap("Override Reflection Cubemap", Float) = 0
		// Custom environment map to reflect
		[NoScaleOffset] _ReflectionCubemapOverride("Reflection Cubemap Override", CUBE) = "" {}

		[Header(Procedural Skybox)]
		// Enable a simple procedural skybox, not suitable for realistic reflections, but can be useful to give control over reflection colour
		// especially in stylized/non realistic applications
		[Toggle] _ProceduralSky("Enable", Float) = 0
		// Base sky colour
		[HDR] _SkyBase("Base", Color) = (1.0, 1.0, 1.0, 1.0)
		// Colour in sun direction
		[HDR] _SkyTowardsSun("Towards Sun", Color) = (1.0, 1.0, 1.0, 1.0)
		// Direction fall off
		_SkyDirectionality("Directionality", Range(0.0, 0.99)) = 1.0
		// Colour away from sun direction
		[HDR] _SkyAwayFromSun("Away From Sun", Color) = (1.0, 1.0, 1.0, 1.0)

		[Header(Add Directional Light)]
		// Add specular highlights from the the primary light.
		[Toggle] _ComputeDirectionalLight("Enable", Float) = 1
		// Specular highlight intensity.
		_DirectionalLightBoost("Boost", Range(0.0, 512.0)) = 7.0
		// Falloff of the specular highlights from source to camera.
		_DirectionalLightFallOff("Falloff", Range(1.0, 4096.0)) = 275.0
		// Helps to spread out specular highlight in mid-to-background.
		[Toggle] _DirectionalLightVaryRoughness("Vary Falloff Over Distance", Float) = 0
		// Definition of far distance.
		_DirectionalLightFarDistance("Far Distance", Float) = 137.0
		// Same as "Falloff" except only up to "Far Distance".
		_DirectionalLightFallOffFar("Falloff At Far Distance", Range(1.0, 4096.0)) = 42.0

		[Header(Foam)]
		// Enable foam layer on ocean surface
		[Toggle] _Foam("Enable", Float) = 1
		// Foam texture
		[NoScaleOffset] _FoamTexture("Foam", 2D) = "white" {}
		// Foam texture scale
		_FoamScale("Foam Scale", Range(0.01, 50.0)) = 10.0
		// Controls how gradual the transition is from full foam to no foam
		_WaveFoamFeather("Foam Feather", Range(0.001, 1.0)) = 0.4
		// Scale intensity of lighting
		_WaveFoamLightScale("Foam Light Scale", Range(0.0, 2.0)) = 1.35
		// Colour tint for whitecaps / foam on water surface
		_FoamWhiteColor("Foam Tint", Color) = (1.0, 1.0, 1.0, 1.0)
		// Proximity to sea floor where foam starts to get generated
		_ShorelineFoamMinDepth("Shoreline Foam Min Depth", Range(0.01, 5.0)) = 0.27

		[Header(Foam 3D Lighting)]
		// Generates normals for the foam based on foam values/texture and use it for foam lighting
		[Toggle] _Foam3DLighting("Enable", Float) = 1
		// Strength of the generated normals
		_WaveFoamNormalStrength("Foam Normal Strength", Range(0.0, 30.0)) = 3.5
		// Acts like a gloss parameter for specular response
		_WaveFoamSpecularFallOff("Specular Falloff", Range(1.0, 512.0)) = 293.0
		// Strength of specular response
		_WaveFoamSpecularBoost("Specular Boost", Range(0.0, 16.0)) = 0.15

		[Header(Foam Bubbles)]
		// Colour tint bubble foam underneath water surface
		_FoamBubbleColor("Foam Bubbles Color", Color) = (0.64, 0.83, 0.82, 1.0)
		// Parallax for underwater bubbles to give feeling of volume
		_FoamBubbleParallax("Foam Bubbles Parallax", Range(0.0, 0.5)) = 0.14
		// How much underwater bubble foam is generated
		_WaveFoamBubblesCoverage("Foam Bubbles Coverage", Range(0.0, 5.0)) = 1.68

		[Header(Transparency)]
		// Whether light can pass through the water surface
		[Toggle] _Transparency("Enable", Float) = 1
		// Scattering coefficient within water volume, per channel
		_DepthFogDensity("Depth Fog Density", Vector) = (0.33, 0.23, 0.37, 1.0)
		// How strongly light is refracted when passing through water surface
		_RefractionStrength("Refraction Strength", Range(0.0, 2.0)) = 0.1

		[Header(Caustics)]
		// Approximate rays being focused/defocused on underwater surfaces
		[Toggle] _Caustics("Enable", Float) = 1
		// Caustics texture
		[NoScaleOffset] _CausticsTexture("Caustics", 2D) = "black" {}
		// Caustics texture scale
		_CausticsTextureScale("Caustics Scale", Range(0.0, 25.0)) = 5.0
		// The 'mid' value of the caustics texture, around which the caustic texture values are scaled
		_CausticsTextureAverage("Caustics Texture Grey Point", Range(0.0, 1.0)) = 0.07
		// Scaling / intensity
		_CausticsStrength("Caustics Strength", Range(0.0, 10.0)) = 3.2
		// The depth at which the caustics are in focus
		_CausticsFocalDepth("Caustics Focal Depth", Range(0.0, 250.0)) = 2.0
		// The range of depths over which the caustics are in focus
		_CausticsDepthOfField("Caustics Depth of Field", Range(0.01, 1000.0)) = 0.33
		// How much the caustics texture is distorted
		_CausticsDistortionStrength("Caustics Distortion Strength", Range(0.0, 0.25)) = 0.16
		// The scale of the distortion pattern used to distort the caustics
		_CausticsDistortionScale("Caustics Distortion Scale", Range(0.01, 50.0)) = 25.0

		// To use the underwater effect the UnderWaterCurtainGeom and UnderWaterMeniscus prefabs must be parented to the camera.
		[Header(Underwater)]
		// Whether the underwater effect is being used. This enables code that shades the surface correctly from underneath.
		[Toggle] _Underwater("Enable", Float) = 0
		// Ordinarily set this to Back to cull back faces, but set to Off to make sure both sides of the surface draw if the
		// underwater effect is being used.
		[Enum(CullMode)] _CullMode("Cull Mode", Int) = 2

		[Header(Flow)]
		// Flow is horizontal motion in water as demonstrated in the 'whirlpool' example scene. 'Create Flow Sim' must be
		// enabled on the OceanRenderer to generate flow data.
		[Toggle] _Flow("Enable", Float) = 0

		[Header(Clip Surface)]
		// Discards ocean surface pixels. Requires 'Create Clip Surface Data' enabled on OceanRenderer script.
		[Toggle] _ClipSurface("Enable", Float) = 0
		// Clips purely based on water depth
		[Toggle] _ClipUnderTerrain("Clip Below Terrain (Requires depth cache)", Float) = 0

		[Header(Rendering)]
		// What projection modes will this material support? Choosing perspective or orthographic is an optimisation.
		[KeywordEnum(Both, Perspective, Orthographic)] _Projection("Projection Support", Float) = 0.0

		[Header(Debug Options)]
		// Build shader with debug info which allows stepping through the code in a GPU debugger. I typically use RenderDoc or
		// PIX for Windows (requires DX12 API to be selected).
		[Toggle] _CompileShaderWithDebugInfo("Compile Shader With Debug Info (D3D11)", Float) = 0
		[Toggle] _DebugDisableShapeTextures("Debug Disable Shape Textures", Float) = 0
		[Toggle] _DebugVisualiseShapeSample("Debug Visualise Shape Sample", Float) = 0
		[Toggle] _DebugVisualiseFlow("Debug Visualise Flow", Float) = 0
		[Toggle] _DebugDisableSmoothLOD("Debug Disable Smooth LOD", Float) = 0
	}

	SubShader
	{
		Tags
		{
			// Tell Unity we're going to render water in forward manner and we're going to do lighting and it will set
			// the appropriate uniforms.
			"LightMode"="ForwardBase"
			// Unity treats anything after Geometry+500 as transparent, and will render it in a forward manner and copy
			// out the gbuffer data and do post processing before running it. Discussion of this in issue #53.
			"Queue"="Geometry+510"
			"IgnoreProjector"="True"
			"RenderType"="Opaque"
			"DisableBatching"="True"
		}

		GrabPass
		{
			"_BackgroundTexture"
		}

		Pass
		{
			// Culling user defined - can be inverted for under water
			Cull [_CullMode]

			CGPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag
			// for VFACE
			#pragma target 3.0
			#pragma multi_compile_fog
			#pragma multi_compile_instancing

			#pragma shader_feature_local _APPLYNORMALMAPPING_ON
			#pragma shader_feature_local _COMPUTEDIRECTIONALLIGHT_ON
			#pragma shader_feature_local _DIRECTIONALLIGHTVARYROUGHNESS_ON
			#pragma shader_feature_local _SUBSURFACESCATTERING_ON
			#pragma shader_feature_local _SUBSURFACESHALLOWCOLOUR_ON
			#pragma shader_feature_local _TRANSPARENCY_ON
			#pragma shader_feature_local _CAUSTICS_ON
			#pragma shader_feature_local _FOAM_ON
			#pragma shader_feature_local _FOAM3DLIGHTING_ON
			#pragma shader_feature_local _PLANARREFLECTIONS_ON
			#pragma shader_feature_local _OVERRIDEREFLECTIONCUBEMAP_ON

			#pragma shader_feature_local _PROCEDURALSKY_ON
			#pragma shader_feature_local _UNDERWATER_ON
			#pragma shader_feature_local _FLOW_ON
			#pragma shader_feature_local _SHADOWS_ON
			#pragma shader_feature_local _CLIPSURFACE_ON
			#pragma shader_feature_local _CLIPUNDERTERRAIN_ON

			#pragma shader_feature_local _ _PROJECTION_PERSPECTIVE _PROJECTION_ORTHOGRAPHIC

			#pragma shader_feature_local _DEBUGDISABLESHAPETEXTURES_ON
			#pragma shader_feature_local _DEBUGVISUALISESHAPESAMPLE_ON
			#pragma shader_feature_local _DEBUGVISUALISEFLOW_ON
			#pragma shader_feature_local _DEBUGDISABLESMOOTHLOD_ON
			#pragma shader_feature_local _COMPILESHADERWITHDEBUGINFO_ON

			#if _COMPILESHADERWITHDEBUGINFO_ON
			#pragma enable_d3d11_debug_symbols
			#endif

			#include "UnityCG.cginc"
			#include "Lighting.cginc"

			#include "OceanGlobals.hlsl"
			#include "OceanInputsDriven.hlsl"
			#include "OceanShaderData.hlsl"
			#include "OceanHelpersNew.hlsl"
			#include "OceanVertHelpers.hlsl"
			#include "OceanShaderHelpers.hlsl"
			#include "OceanLightingHelpers.hlsl"
			#include "OceanOccluderHelpers.hlsl"

			#include "OceanEmission.hlsl"
			#include "OceanNormalMapping.hlsl"
			#include "OceanReflection.hlsl"
			#include "OceanFoam.hlsl"

			struct Attributes
			{
				// The old unity macros require this name and type.
				float4 vertex : POSITION;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				half4 flow_shadow : TEXCOORD1;
				half4 foam_screenPosXYW : TEXCOORD4;
				float4 lodAlpha_worldXZUndisplaced_oceanDepth : TEXCOORD5;
				float3 worldPos : TEXCOORD7;
				#if _DEBUGVISUALISESHAPESAMPLE_ON
				half3 debugtint : TEXCOORD8;
				#endif
				half4 grabPos : TEXCOORD9;

				UNITY_FOG_COORDS(3)

				UNITY_VERTEX_OUTPUT_STEREO
			};

			// Argument name is v because some macros like COMPUTE_EYEDEPTH require it.
			Varyings Vert(Attributes v)
			{
				Varyings o;

				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_OUTPUT(Varyings, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				const CascadeParams cascadeData0 = _CrestCascadeData[_LD_SliceIndex];
				const CascadeParams cascadeData1 = _CrestCascadeData[_LD_SliceIndex + 1];
				const PerCascadeInstanceData instanceData = _CrestPerCascadeInstanceData[_LD_SliceIndex];

				// Move to world space
				o.worldPos = mul(UNITY_MATRIX_M, float4(v.vertex.xyz, 1.0));

				// Vertex snapping and lod transition
				float lodAlpha;
				const float meshScaleLerp = instanceData._meshScaleLerp;
				const float gridSize = instanceData._geoGridWidth;
				SnapAndTransitionVertLayout(meshScaleLerp, cascadeData0, gridSize, o.worldPos, lodAlpha);

				{
					// Scale up by small "epsilon" to solve numerical issues. Expand slightly about tile center.
					// :OceanGridPrecisionErrors
					const float2 tileCenterXZ = UNITY_MATRIX_M._m03_m23;
					const float2 cameraPositionXZ = abs(_WorldSpaceCameraPos.xz);
					// Scale "epsilon" by distance from zero. There is an issue where overlaps can cause SV_IsFrontFace
					// to be flipped (needs to be investigated). Gaps look bad from above surface, and overlaps look bad
					// from below surface. We want to close gaps without introducing overlaps. A fixed "epsilon" will
					// either not solve gaps at large distances or introduce too many overlaps at small distances. Even
					// with scaling, there are still unsolvable overlaps underwater (especially at large distances).
					// 100,000 (0.00001) is the maximum position before Unity warns the user of precision issues.
					o.worldPos.xz = lerp(tileCenterXZ, o.worldPos.xz, lerp(1.0, 1.01, max(cameraPositionXZ.x, cameraPositionXZ.y) * 0.00001));
				}

				o.lodAlpha_worldXZUndisplaced_oceanDepth.x = lodAlpha;
				o.lodAlpha_worldXZUndisplaced_oceanDepth.yz = o.worldPos.xz;

				// sample shape textures - always lerp between 2 LOD scales, so sample two textures
				o.flow_shadow = half4(0., 0., 0., 0.);
				o.foam_screenPosXYW.x = 0.;

				o.lodAlpha_worldXZUndisplaced_oceanDepth.w = CREST_OCEAN_DEPTH_BASELINE;
				// Sample shape textures - always lerp between 2 LOD scales, so sample two textures

				// Calculate sample weights. params.z allows shape to be faded out (used on last lod to support pop-less scale transitions)
				const float wt_smallerLod = (1. - lodAlpha) * cascadeData0._weight;
				const float wt_biggerLod = (1. - wt_smallerLod) * cascadeData1._weight;
				// Sample displacement textures, add results to current world pos / normal / foam
				const float2 positionWS_XZ_before = o.worldPos.xz;

				// Data that needs to be sampled at the undisplaced position
				if (wt_smallerLod > 0.001)
				{
					const float3 uv_slice_smallerLod = WorldToUV(positionWS_XZ_before, cascadeData0, _LD_SliceIndex);

					#if !_DEBUGDISABLESHAPETEXTURES_ON
					SampleDisplacements(_LD_TexArray_AnimatedWaves, uv_slice_smallerLod, wt_smallerLod, o.worldPos);
					#endif

					#if _FOAM_ON
					SampleFoam(_LD_TexArray_Foam, uv_slice_smallerLod, wt_smallerLod, o.foam_screenPosXYW.x);
					#endif

					#if _FLOW_ON
					SampleFlow(_LD_TexArray_Flow, uv_slice_smallerLod, wt_smallerLod, o.flow_shadow.xy);
					#endif
				}
				if (wt_biggerLod > 0.001)
				{
					const float3 uv_slice_biggerLod = WorldToUV(positionWS_XZ_before, cascadeData1, _LD_SliceIndex + 1);

					#if !_DEBUGDISABLESHAPETEXTURES_ON
					SampleDisplacements(_LD_TexArray_AnimatedWaves, uv_slice_biggerLod, wt_biggerLod, o.worldPos);
					#endif

					#if _FOAM_ON
					SampleFoam(_LD_TexArray_Foam, uv_slice_biggerLod, wt_biggerLod, o.foam_screenPosXYW.x);
					#endif

					#if _FLOW_ON
					SampleFlow(_LD_TexArray_Flow, uv_slice_biggerLod, wt_biggerLod, o.flow_shadow.xy);
					#endif
				}

				// Data that needs to be sampled at the displaced position
				if (wt_smallerLod > 0.0001)
				{
					const float3 uv_slice_smallerLodDisp = WorldToUV(o.worldPos.xz, cascadeData0, _LD_SliceIndex);

					#if _SUBSURFACESHALLOWCOLOUR_ON
					// The minimum sampling weight is lower (0.0001) than others to fix shallow water colour popping.
					SampleSeaDepth(_LD_TexArray_SeaFloorDepth, uv_slice_smallerLodDisp, wt_smallerLod, o.lodAlpha_worldXZUndisplaced_oceanDepth.w);
					#endif

					#if _SHADOWS_ON
					if (wt_smallerLod > 0.001)
					{
						SampleShadow(_LD_TexArray_Shadow, uv_slice_smallerLodDisp, wt_smallerLod, o.flow_shadow.zw);
					}
					#endif
				}
				if (wt_biggerLod > 0.0001)
				{
					const float3 uv_slice_biggerLodDisp = WorldToUV(o.worldPos.xz, cascadeData1, _LD_SliceIndex + 1);

					#if _SUBSURFACESHALLOWCOLOUR_ON
					// The minimum sampling weight is lower (0.0001) than others to fix shallow water colour popping.
					SampleSeaDepth(_LD_TexArray_SeaFloorDepth, uv_slice_biggerLodDisp, wt_biggerLod, o.lodAlpha_worldXZUndisplaced_oceanDepth.w);
					#endif

					#if _SHADOWS_ON
					if (wt_biggerLod > 0.001)
					{
						SampleShadow(_LD_TexArray_Shadow, uv_slice_biggerLodDisp, wt_biggerLod, o.flow_shadow.zw);
					}
					#endif
				}

				// Foam can saturate
				o.foam_screenPosXYW.x = saturate(o.foam_screenPosXYW.x);

				// debug tinting to see which shape textures are used
				#if _DEBUGVISUALISESHAPESAMPLE_ON
				#define TINT_COUNT (uint)7
				half3 tintCols[TINT_COUNT]; tintCols[0] = half3(1., 0., 0.); tintCols[1] = half3(1., 1., 0.); tintCols[2] = half3(1., 0., 1.); tintCols[3] = half3(0., 1., 1.); tintCols[4] = half3(0., 0., 1.); tintCols[5] = half3(1., 0., 1.); tintCols[6] = half3(.5, .5, 1.);
				o.debugtint = wt_smallerLod * tintCols[_LD_SliceIndex % TINT_COUNT] + wt_biggerLod * tintCols[(_LD_SliceIndex + 1) % TINT_COUNT];
				#endif

				// view-projection
				o.positionCS = mul(UNITY_MATRIX_VP, float4(o.worldPos, 1.));

				UNITY_TRANSFER_FOG(o, o.positionCS);

				// unfortunate hoop jumping - this is inputs for refraction. depending on whether HDR is on or off, the grabbed scene
				// colours may or may not come from the backbuffer, which means they may or may not be flipped in y. use these macros
				// to get the right results, every time.
				o.grabPos = ComputeGrabScreenPos(o.positionCS);
				o.foam_screenPosXYW.yzw = ComputeScreenPos(o.positionCS).xyw;
				return o;
			}

			half4 Frag(const Varyings input, const bool i_isFrontFace : SV_IsFrontFace) : SV_Target
			{
				// We need this when sampling a screenspace texture.
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				const CascadeParams cascadeData0 = _CrestCascadeData[_LD_SliceIndex];
				const CascadeParams cascadeData1 = _CrestCascadeData[_LD_SliceIndex + 1];
				const PerCascadeInstanceData instanceData = _CrestPerCascadeInstanceData[_LD_SliceIndex];

				#if _UNDERWATER_ON
				const bool underwater = IsUnderwater(i_isFrontFace, _ForceUnderwater);
				#else
				const bool underwater = false;
				#endif

				const float lodAlpha = input.lodAlpha_worldXZUndisplaced_oceanDepth.x;
				const float wt_smallerLod = (1.0 - lodAlpha) * cascadeData0._weight;
				const float wt_biggerLod = (1.0 - wt_smallerLod) * cascadeData1._weight;

				#if _CLIPSURFACE_ON
				// Clip surface
				half clipVal = 0.0;
				if (wt_smallerLod > 0.001)
				{
					const float3 uv_slice_smallerLod = WorldToUV(input.worldPos.xz, cascadeData0, _LD_SliceIndex);
					SampleClip(_LD_TexArray_ClipSurface, uv_slice_smallerLod, wt_smallerLod, clipVal);
				}
				if (wt_biggerLod > 0.001)
				{
					const float3 uv_slice_biggerLod = WorldToUV(input.worldPos.xz, cascadeData1, _LD_SliceIndex + 1);
					SampleClip(_LD_TexArray_ClipSurface, uv_slice_biggerLod, wt_biggerLod, clipVal);
				}
				clipVal = lerp(_CrestClipByDefault, clipVal, wt_smallerLod + wt_biggerLod);
				// Add 0.5 bias for LOD blending and texel resolution correction. This will help to tighten and smooth clipped edges
				clip(-clipVal + 0.5);
				#endif

				#if _CLIPUNDERTERRAIN_ON
				clip(input.lodAlpha_worldXZUndisplaced_oceanDepth.w + 2.0);
				#endif

				half3 view = normalize(_WorldSpaceCameraPos - input.worldPos);

				// water surface depth, and underlying scene opaque surface depth
				float pixelZ = CrestLinearEyeDepth(input.positionCS.z);
				half3 screenPos = input.foam_screenPosXYW.yzw;
				half2 uvDepth = screenPos.xy / screenPos.z;
				{
					DiscardOceanSurfaceFromOccluderMask(uvDepth, input.positionCS.z);
				}
				// Raw depth is logarithmic for perspective, and linear (0-1) for orthographic.
				float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uvDepth).x;
				float sceneZ = CrestLinearEyeDepth(rawDepth);

				float3 lightDir = WorldSpaceLightDir(input.worldPos);
				// Soft shadow, hard shadow
				fixed2 shadow = (fixed2)1.0
				#if _SHADOWS_ON
					- input.flow_shadow.zw
				#endif
					;

				// Normal - geom + normal mapping. Subsurface scattering.
				float3 dummy = 0.;
				float3 n_pixel = float3(0.0, 1.0, 0.0);
				half sss = 0.;
				if (wt_smallerLod > 0.001)
				{
					const float3 uv_slice_smallerLod = WorldToUV(input.lodAlpha_worldXZUndisplaced_oceanDepth.yz, _CrestCascadeData[_LD_SliceIndex], _LD_SliceIndex);
					SampleDisplacementsNormals(_LD_TexArray_AnimatedWaves, uv_slice_smallerLod, wt_smallerLod, _CrestCascadeData[_LD_SliceIndex]._oneOverTextureRes, cascadeData0._texelWidth, dummy, n_pixel.xz, sss);
				}
				if (wt_biggerLod > 0.001)
				{
					const uint si = _LD_SliceIndex + 1;
					const float3 uv_slice_biggerLod = WorldToUV(input.lodAlpha_worldXZUndisplaced_oceanDepth.yz, _CrestCascadeData[si], si);
					SampleDisplacementsNormals(_LD_TexArray_AnimatedWaves, uv_slice_biggerLod, wt_biggerLod, cascadeData1._oneOverTextureRes, cascadeData1._texelWidth, dummy, n_pixel.xz, sss);
				}

				#if _APPLYNORMALMAPPING_ON
				#if _FLOW_ON
				ApplyNormalMapsWithFlow(input.lodAlpha_worldXZUndisplaced_oceanDepth.yz, input.flow_shadow.xy, lodAlpha, cascadeData0, instanceData, n_pixel);
				#else
				n_pixel.xz += SampleNormalMaps(input.lodAlpha_worldXZUndisplaced_oceanDepth.yz, lodAlpha, cascadeData0, instanceData);
				#endif
				#endif

				// Finalise normal
				n_pixel.xz *= _NormalsStrengthOverall;
				n_pixel = normalize( n_pixel );
				if (underwater) n_pixel = -n_pixel;

				// Foam - underwater bubbles and whitefoam
				half3 bubbleCol = (half3)0.;
				#if _FOAM_ON
				half4 whiteFoamCol;
				#if !_FLOW_ON
				ComputeFoam(input.foam_screenPosXYW.x, input.lodAlpha_worldXZUndisplaced_oceanDepth.yz, input.worldPos.xz, n_pixel, pixelZ, sceneZ, view, lightDir, shadow.y, lodAlpha, bubbleCol, whiteFoamCol, cascadeData0, cascadeData1);
				#else
				ComputeFoamWithFlow(input.flow_shadow.xy, input.foam_screenPosXYW.x, input.lodAlpha_worldXZUndisplaced_oceanDepth.yz, input.worldPos.xz, n_pixel, pixelZ, sceneZ, view, lightDir, shadow.y, lodAlpha, bubbleCol, whiteFoamCol, cascadeData0, cascadeData1);
				#endif // _FLOW_ON
				#endif // _FOAM_ON

				// Compute color of ocean - in-scattered light + refracted scene
				const float baseCascadeScale = _CrestCascadeData[0]._scale;
				const float meshScaleLerp = instanceData._meshScaleLerp;
				half3 scatterCol = ScatterColour(AmbientLight(), input.lodAlpha_worldXZUndisplaced_oceanDepth.w, _WorldSpaceCameraPos, lightDir, view, shadow.x, underwater, true, sss, meshScaleLerp, baseCascadeScale, cascadeData0);
				half3 col = OceanEmission(view, n_pixel, lightDir, input.grabPos, pixelZ, uvDepth, sceneZ, bubbleCol, _Normals, underwater, scatterCol, cascadeData0, cascadeData1);

				// Light that reflects off water surface

				// Soften reflection at intersections with objects/surfaces
				#if _TRANSPARENCY_ON
				float reflAlpha = saturate((sceneZ - pixelZ) / 0.2);
				#else
				// This addresses the problem where screenspace depth doesnt work in VR, and so neither will this. In VR people currently
				// disable transparency, so this will always be 1.0.
				float reflAlpha = 1.0;
				#endif

				#if _UNDERWATER_ON
				if (underwater)
				{
					ApplyReflectionUnderwater(view, n_pixel, lightDir, shadow.y, input.foam_screenPosXYW.yzzw, scatterCol, reflAlpha, col);
				}
				else
				#endif
				{
					ApplyReflectionSky(view, n_pixel, lightDir, shadow.y, input.foam_screenPosXYW.yzzw, pixelZ, reflAlpha, col);
				}

				// Override final result with white foam - bubbles on surface
				#if _FOAM_ON
				col = lerp(col, whiteFoamCol.rgb, whiteFoamCol.a);
				#endif

				// Fog
				if (!underwater)
				{
					// Above water - do atmospheric fog. If you are using a third party sky package such as Azure, replace this with their stuff!
					UNITY_APPLY_FOG(input.fogCoord, col);
				}
				#if _DEBUGVISUALISESHAPESAMPLE_ON
				col = lerp(col.rgb, input.debugtint, 0.5);
				#endif
				#if _DEBUGVISUALISEFLOW_ON
				#if _FLOW_ON
				col.rg = lerp(col.rg, input.flow_shadow.xy, 0.5);
				#endif
				#endif

				return half4(col, 1.);
			}

			ENDCG
		}
	}

	// If the above doesn't work then error.
	FallBack "Hidden/InternalErrorShader"
}
