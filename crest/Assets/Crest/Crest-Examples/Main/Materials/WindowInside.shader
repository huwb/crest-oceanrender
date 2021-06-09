Shader "Crest/Material/WindowInside"
{
	Properties
	{
		_Albedo ("Albedo", Color) = (1,1,1,1)
		_SurfaceNormal ("Normal", 2D) = "white" {}
		_Glossiness ("Smoothness", Range(0,1)) = 0.5
		_Metallic ("Metallic", Range(0,1)) = 0.0
	}
	SubShader
	{
		// TODO(TRC):Now how do we express all the contraints that this needs?
		// - The part of the queue it should go into
		// - The name of the grabpass
		// - All the pragmas that are required
		// - Can we get this to work with shadergraph materials?
		// Is there a way to programmatically apply this to any transparent
		// surface? Can we inject our own shader pass? That would be the ideal
		// thing to do (from UnderwaterEffectFilter.cs).
		Tags { "Queue" = "Geometry+511" "RenderType"="Transparent" }

		GrabPass {
			Name "CrestOceanGrabPass"
		}

		Pass
		{
			Name "CrestWaterTransparency"
			ZWrite Off // TODO: Make this an option? Prevents particles from being culled behind it.
			Blend Off

			CGPROGRAM

			#include "../../../Crest/Shaders/UnderwaterWindowShaderPass.hlsl"

			#pragma vertex Vert
			#pragma fragment Frag

			#pragma shader_feature _SUBSURFACESCATTERING_ON
			#pragma shader_feature _SUBSURFACESHALLOWCOLOUR_ON
			#pragma shader_feature _TRANSPARENCY_ON
			#pragma shader_feature _CAUSTICS_ON
			#pragma shader_feature _SHADOWS_ON
			#pragma shader_feature _COMPILESHADERWITHDEBUGINFO_ON
			#pragma shader_feature _MENISCUS_ON

			#pragma multi_compile __ _FULL_SCREEN_EFFECT
			#pragma multi_compile __ _DEBUG_VIEW_OCEAN_MASK

			#if _COMPILESHADERWITHDEBUGINFO_ON
			#pragma enable_d3d11_debug_symbols
			#endif

			#pragma enable_d3d11_debug_symbols


			ENDCG
		}

		CGPROGRAM
		// Physically based Standard lighting model, and enable shadows on all light types
		#pragma surface surf Standard fullforwardshadows alpha:blend

		// Use shader model 3.0 target, to get nicer looking lighting
		#pragma target 3.0

		sampler2D _SurfaceNormal;

		struct Input
		{
			float2 uv_SurfaceNormal;
		};

		half _Glossiness;
		half _Metallic;
		fixed4 _Albedo;

		// Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
		// See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.
		// #pragma instancing_options assumeuniformscaling
		UNITY_INSTANCING_BUFFER_START(Props)
			// put more per-instance properties here
		UNITY_INSTANCING_BUFFER_END(Props)

		void surf (Input IN, inout SurfaceOutputStandard o)
		{
			// Albedo comes from a texture tinted by color
			fixed4 c = fixed4(0.7607843, 0.7607843, 0.7607843, 0.2745098);//_Albedo;
			o.Albedo = c.rgb;
			// Metallic and smoothness come from slider variables
			o.Metallic = .7519999;
			o.Smoothness = 1;
			o.Normal = tex2D (_SurfaceNormal, IN.uv_SurfaceNormal * 3);
			o.Alpha = c.a;
		}
		ENDCG
	}
	FallBack "Diffuse"
}
