// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

// IMPORTANT - this mirrors the constant with the same name in ShapeGerstnerBatched.cs, both must be updated together!
#define BATCH_SIZE 32

#define PI 3.141593

// Caution - this exploded on vulkan due to a collision with 'CrestPerObject' cbuffer in OceanInput
CBUFFER_START(GerstnerUniforms)
half _Weight;
half _AttenuationInShallows;
uint _NumWaveVecs;

half4 _TwoPiOverWavelengths[BATCH_SIZE / 4];
half4 _Amplitudes[BATCH_SIZE / 4];
half4 _WaveDirX[BATCH_SIZE / 4];
half4 _WaveDirZ[BATCH_SIZE / 4];
half4 _Phases[BATCH_SIZE / 4];
half4 _ChopAmps[BATCH_SIZE / 4];

float4 _TargetPointData;

half _ShorelineTwoPiOverWavelengthNear;
half _ShorelineTwoPiOverWavelengthFar;
float _ShorelineLerpDistance;
half _ShorelineTwoPiOverWavePeriod;
half _ShorelineAmplitude;
half _ShorelineChop;
uint _ShorelineWavePeriodSeparation;

CBUFFER_END

half4 ComputeGerstner(float2 worldPosXZ, float3 uv_slice, half depth)
{
	float2 displacementNormalized = 0.0;

	// Preferred wave directions
#if CREST_DIRECT_TOWARDS_POINT_INTERNAL
	float2 offset = worldPosXZ - _TargetPointData.xy;
	float preferDist = length(offset);
	float preferWt = smoothstep(_TargetPointData.w, _TargetPointData.z, preferDist);
	half2 preferredDir = preferWt * offset / preferDist;
	half4 preferredDirX = preferredDir.x;
	half4 preferredDirZ = preferredDir.y;
#endif

	half3 result = (half3)0.0;

	// attenuate waves based on ocean depth. if depth is greater than 0.5*wavelength, water is considered Deep and wave is
	// unaffected. if depth is less than this, wave velocity decreases. waves will then bunch up and grow in amplitude and
	// eventually break. i model "Deep" water, but then simply ramp down waves in non-deep water with a linear multiplier.
	// http://hyperphysics.phy-astr.gsu.edu/hbase/Waves/watwav2.html
	// http://hyperphysics.phy-astr.gsu.edu/hbase/watwav.html#c1
	// optimisation - do this outside the loop below - take the median wavelength for depth weighting, intead of computing
	// per component. computing per component makes little difference to the end result
	half depth_wt = saturate(depth * _TwoPiOverWavelengths[_NumWaveVecs / 2].x / PI);
	half4 wt = _AttenuationInShallows * depth_wt + (1.0 - _AttenuationInShallows);

	// gerstner computation is vectorized - processes 4 wave components at once
	for (uint vi = 0; vi < _NumWaveVecs; vi++)
	{
		// direction
		half4 Dx = _WaveDirX[vi];
		half4 Dz = _WaveDirZ[vi];

		// Peferred wave direction
#if CREST_DIRECT_TOWARDS_POINT_INTERNAL
		wt *= max((1.0 + Dx * preferredDirX + Dz * preferredDirZ) / 2.0, 0.1);
#endif

		// wave number
		half4 k = _TwoPiOverWavelengths[vi];
		// spatial location
		float4 x = Dx * worldPosXZ.x + Dz * worldPosXZ.y;
		half4 angle = k * x + _Phases[vi];

		// dx and dz could be baked into _ChopAmps
		half4 disp = _ChopAmps[vi] * sin(angle);
		half4 resultx = disp * Dx;
		half4 resultz = disp * Dz;

		half4 resulty = _Amplitudes[vi] * cos(angle);

		// sum the vector results
		result.x += dot(resultx, wt);
		result.y += dot(resulty, wt);
		result.z += dot(resultz, wt);

		half4 sssFactor = min(1.0, _TwoPiOverWavelengths[vi]);
		displacementNormalized.x += dot(resultx * sssFactor, wt);
		displacementNormalized.y += dot(resultz * sssFactor, wt);
	}

	half sss = length(displacementNormalized);

	return _Weight * half4(result, sss);
}

half4 ComputeShorelineGerstner(float2 worldPosXZ, float3 uv_slice, half2 depth_distance, half2 directionToShore)
{
	half depth = depth_distance.x;
	half distanceToShore = depth_distance.y;
	const float lerpDistance = _ShorelineLerpDistance;
	float directionalStrength = 1.0 - clamp(distanceToShore / lerpDistance, 0.0, 1.0);
	float2 displacementNormalized = 0.0;
	half3 result = (half3)0.0;

	if(depth > 0.0)
	{
		// We lerp between a "near" and "far" wavelength - ish
		// Just based on experimentation this is a visually-pleasing counter-weight to the
		// fact that the "length" is done based on the square-root of the distance (this means
		// larger waves further-away from the shore-which is what we want) - by having a "shorter"
		// "far" wave-length - we can keep waves more-compressed closer to the shore.
		// This is a big hack and should probably be replaced with something that actually operates
		// on real-world values.
		const float twoPiOverWavelengthNear = _ShorelineTwoPiOverWavelengthNear;
		const float twoPiOverWavelengthFar = _ShorelineTwoPiOverWavelengthFar;
		const float twoPiOverWavelength = lerp(twoPiOverWavelengthFar, twoPiOverWavelengthNear, sqrt(directionalStrength));

		const float twoPiOverPeriod = _ShorelineTwoPiOverWavePeriod;

		// We increase the wave amplitude slightly as depth decreases - have tried doing
		// this based on distance to the shoreline as well - but I think this produces betteer results.
		const float amplitude = _ShorelineAmplitude/(1.0+sqrt(depth));
		// Chop increases as depth increases
		const float chopAmplitude = _ShorelineChop/(1.0+sqrt(depth));

		float angleDistance = distanceToShore;
		float breakupDampner = 1.0;
		// An attempt to add-noise or otherwise break-up the visual makeup of the shoreline waves.
		// {
		// 	float worldSpaceHeuristic = (worldPosXZ.x + worldPosXZ.y);
		// 	float lerpFun = (sin(worldSpaceHeuristic * 0.1) + 1.0) * 0.5;
		// 	if(lerpFun < 0.5)
		// 	{
		// 		angleDistance += 4.0;
		// 		breakupDampner = lerp(0, 1, lerpFun - 0.1);
		// 	}
		// }

		// The wave-angle is calculated using the square root of the distance to the shoreline in order
		// to make waves further-from the shoreline spread further-apart. However we slightlly counteract this
		// using the lerping above. A bit odd.
		const float angle = (twoPiOverWavelength * sqrt(angleDistance)) + (_CrestTime * twoPiOverPeriod);
		result.y = amplitude * cos(angle);

		// We can make it so that waves come in multiples of a given period :)
		// TODO(TRC): Implement lerping to make it less discontinuous and make it so that noise can a factor here.
		// (eg - have overlappng and different wave phases at different parts of the wavefront).
		const int wavePeriodSeparation = _ShorelineWavePeriodSeparation;

		const float pi = 3.14;
		if(floor((angle + (pi + 0.5)) / ( 2.0 * pi)) % wavePeriodSeparation != 0)
		{
			breakupDampner = 0.0;
		}

		// We tip the top of the waves forwards slightly the closer to the shoreline we are
		// to simulate the drag the bottom of the waves experience compared-with the top.
		result.xz = chopAmplitude * directionToShore * sin(angle) * result.y;


		// We intentially slightly increase the height of the bottom of the wave as the depth
		// decreases in order to prevent it from intersecting with the terrain - a hack but it kind-of
		// works. :)
		result.y += lerp(0, ((amplitude + 0.1) / (1.0 + depth)), sqrt((1.0 - result.y) * 0.5));


		// Dampen waves really-close to the shoreline so that the "naturally" fade away instead of intersecting with the terrain
		const float boundarySafeDistance = 0.1; // distance within which shore-lines should be stifled
		const float boundaryLerpDampenLength = 6.0; // distance over which we should start dampening shoreline waves
		result.xyz = lerp(0, result.xyz, saturate((distanceToShore / boundaryLerpDampenLength) - boundarySafeDistance));
		result.xyz *= breakupDampner;
	}
	return _Weight * half4(result, 0.0) * directionalStrength;
}
