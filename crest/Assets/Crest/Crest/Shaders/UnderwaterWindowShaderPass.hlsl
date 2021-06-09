// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

#include "UnityCG.cginc"
#include "Lighting.cginc"

struct Attributes
{
	float4 positionOS : POSITION;
	float2 uv         : TEXCOORD0;
};

struct Varyings {
	float4 positionCS : POSITION;
	float4 screenPos  : TEXCOORD0;
	float3 worldPos     : TEXCOORD1;
};

Varyings Vert (Attributes input)
{
	Varyings output;
	output.positionCS = UnityObjectToClipPos(input.positionOS);
	output.screenPos = ComputeScreenPos(output.positionCS);
	output.worldPos = mul(unity_ObjectToWorld, input.positionOS);
	return output;
}

half _DataSliceOffset;
half3 _CrestAmbientLighting;
#include "OceanConstants.hlsl"
#include "OceanInputsDriven.hlsl"
#include "OceanGlobals.hlsl"
#include "OceanShaderData.hlsl"
#include "OceanShaderHelpers.hlsl"
#include "OceanHelpersNew.hlsl"
#include "OceanEmission.hlsl"

float4 _CrestHorizonPosNormal;
sampler2D _CrestOceanMaskTexture;
sampler2D _CrestOceanMaskDepthTexture;

#include "UnderwaterHelpers.hlsl"

// Unity Defined Samplers
sampler2D _GrabTexture;
float4 _GrabTexture_TexelSize;


void CrestApplyUnderwaterFog (in float2 uvScreenSpace, in float3 viewWS, in float surfaceZ01, inout fixed4 sceneColour)
{
	float oceanMask     = tex2D(_CrestOceanMaskTexture, uvScreenSpace).x;
	float sceneZ01      = tex2D(_CameraDepthTexture, uvScreenSpace).x;
	float oceanSceneZ01 = tex2D(_CrestOceanMaskDepthTexture, uvScreenSpace).x;
	bool isOceanSurface = false;

	// If the ocean mask contains ocean behind this surface, then we know that the pixel
	// behind us is a surface pixel (so we don't render caustics)
	if(oceanSceneZ01 > sceneZ01)
	{
		sceneZ01 = oceanSceneZ01;
		isOceanSurface = true;
	}

	const bool isBelowHorizon = dot(uvScreenSpace - _CrestHorizonPosNormal.xy, _CrestHorizonPosNormal.zw) > 0.0;
	bool isUnderwater = oceanMask == UNDERWATER_MASK_WATER_SURFACE_BELOW || (isBelowHorizon && oceanMask != UNDERWATER_MASK_WATER_SURFACE_ABOVE);

	float sceneDepth = LinearEyeDepth(sceneZ01);
	float fogDistance = sceneDepth - LinearEyeDepth(surfaceZ01);

	// TODO(TRC):Now Figure out how to get the to the right value if this is occluded by another transparency.
	if(isUnderwater)
	{
		half3 view = normalize(viewWS);
		sceneColour.xyz = ApplyUnderwaterEffect(
			_LD_TexArray_AnimatedWaves,
			_Normals,
			_WorldSpaceCameraPos,
			_CrestAmbientLighting,
			sceneColour.xyz,
			sceneDepth,
			fogDistance,
			view,
			_DepthFogDensity,
			isOceanSurface
		);
	}
}

half4 Frag( Varyings input ) : COLOR
{
	float2 uvScreenSpace = input.screenPos.xy / input.screenPos.w;
	half4 color = tex2D(_GrabTexture, uvScreenSpace);
	CrestApplyUnderwaterFog(uvScreenSpace, _WorldSpaceCameraPos - input.worldPos, input.positionCS.z, color);
	return color;
}
