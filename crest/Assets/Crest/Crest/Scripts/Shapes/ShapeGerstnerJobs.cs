﻿// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// A potential optimisation in the future would be to allocate scratch space in the job. this isn't supported yet in burst but will be
// https://forum.unity.com/threads/burst-dont-allow-me-to-create-a-nativearray.556105/

namespace Crest
{
    public static class ShapeGerstnerJobs
    {
        // General variables
        public static bool s_initialised = false;
        public static bool s_firstFrame = true;
        public static bool s_jobsRunning = false;

        const int MAX_WAVE_COMPONENTS = 512;

        // Wave data
        static NativeArray<float4> s_waveNumbers;
        static NativeArray<float4> s_amps;
        static NativeArray<float4> s_windDirX;
        static NativeArray<float4> s_windDirZ;
        static NativeArray<float4> s_phases;
        static NativeArray<float4> s_chopAmps;

        static int _waveVecCount = 0;
        static int _waveVecElemIndex = 0;

        const int MAX_QUERIES = 4096;

        // Query data for height samples
        static NativeArray<Vector3> s_localQueryPositions;
		static NativeArray<Matrix4x4> s_queryPositionsMatrixes;
		static int s_lastQueryIndexHeights = 0;
        static NativeArray<float> s_resultHeights;
        static JobHandle s_handleHeights;
        static Dictionary<int, int2> s_segmentRegistry = new Dictionary<int, int2>();

		static readonly float s_twoPi = Mathf.PI * 2f;

        /// <summary>
        /// Allocate storage. Should be called once - will assert if called while already initialised.
        /// </summary>
        public static void Init()
        {
            Debug.Assert(s_initialised == false);
            if (s_initialised)
                return;

            s_localQueryPositions = new NativeArray<Vector3>(MAX_QUERIES, Allocator.Persistent);
			s_queryPositionsMatrixes = new NativeArray<Matrix4x4>(MAX_QUERIES, Allocator.Persistent);
			s_resultHeights = new NativeArray<float>(MAX_QUERIES, Allocator.Persistent);

            s_waveNumbers = new NativeArray<float4>(MAX_WAVE_COMPONENTS / 4, Allocator.Persistent);
            s_amps = new NativeArray<float4>(MAX_WAVE_COMPONENTS / 4, Allocator.Persistent);
            s_windDirX = new NativeArray<float4>(MAX_WAVE_COMPONENTS / 4, Allocator.Persistent);
            s_windDirZ = new NativeArray<float4>(MAX_WAVE_COMPONENTS / 4, Allocator.Persistent);
            s_phases = new NativeArray<float4>(MAX_WAVE_COMPONENTS / 4, Allocator.Persistent);
            s_chopAmps = new NativeArray<float4>(MAX_WAVE_COMPONENTS / 4, Allocator.Persistent);

            s_segmentRegistry.Clear();
            s_lastQueryIndexHeights = 0;

            s_initialised = true;
        }

        static void NextElement()
        {
            _waveVecElemIndex = (_waveVecElemIndex + 1) % 4;
            if (_waveVecElemIndex == 0) _waveVecCount++;
        }

        static void SetArrayFloat4(NativeArray<float4> array, float value)
        {
            // Do the read/write dance as poking in the data directly is not possible
            float4 vec = array[_waveVecCount];
            vec[_waveVecElemIndex] = value;
            array[_waveVecCount] = vec;
        }

        /// <summary>
        /// Call this once before calling AddWaveData().
        /// </summary>
        public static void StartSettingWaveData()
        {
            _waveVecCount = 0;
            _waveVecElemIndex = 0;
        }

        /// <summary>
        /// Set the Gerstner wave data. Will reallocate data if the number of waves changes.
        /// </summary>
        /// <returns>True if all added, false if ran out of space in wave data buffer.</returns>
        public static bool AddWaveData(float[] wavelengths, float[] amps, float[] angles, float[] phases, float[] chopScales, float[] gravityScales, int compPerOctave)
        {
            // How many vectors required to accommodate length numFloats

			// TODO - should add an option to not add all the waves in case we want smoother reading of the wave info

            var numFloats = wavelengths.Length;
            var numVecs = numFloats / 4;
            if (numVecs * 4 < numFloats)
				numVecs++;

			for (var inputi = 0; inputi < numFloats; inputi++)
            {
                if (_waveVecCount >= MAX_WAVE_COMPONENTS / 4) return false;
                if (amps[inputi] <= 0.001f) continue;

                var k = s_twoPi / wavelengths[inputi];
                SetArrayFloat4(s_waveNumbers, k);
                SetArrayFloat4(s_amps, amps[inputi]);

                var octavei = inputi / compPerOctave;
                SetArrayFloat4(s_chopAmps, chopScales[octavei] * amps[inputi]);

                float angle = (OceanRenderer.Instance._windDirectionAngle + angles[inputi]) * Mathf.Deg2Rad;
                SetArrayFloat4(s_windDirX, Mathf.Cos(angle));
                SetArrayFloat4(s_windDirZ, Mathf.Sin(angle));

                var C = Mathf.Sqrt(9.81f * wavelengths[inputi] * gravityScales[octavei] / s_twoPi);
                SetArrayFloat4(s_phases, phases[inputi] + k * C * OceanRenderer.Instance.CurrentTime);

                NextElement();
            }

            return true;
        }

        /// <summary>
        /// Call this after calling AddWaveData().
        /// </summary>
        public static void FinishAddingWaveData()
        {
            // Zero out trailing entries in the last vec
            while (_waveVecElemIndex != 0)
            {
                SetArrayFloat4(s_waveNumbers, 0f);
                SetArrayFloat4(s_amps, 0f);
                SetArrayFloat4(s_windDirX, 0f);
                SetArrayFloat4(s_windDirZ, 0f);
                SetArrayFloat4(s_phases, 0f);
                SetArrayFloat4(s_chopAmps, 1f);

                NextElement();
            }
        }

        /// <summary>
        /// Dispose storage
        /// </summary>
        public static void Cleanup()
        {
            s_initialised = false;

            s_handleHeights.Complete();

            s_waveNumbers.Dispose();
            s_amps.Dispose();
            s_windDirX.Dispose();
            s_windDirZ.Dispose();
            s_phases.Dispose();
            s_chopAmps.Dispose();

            s_localQueryPositions.Dispose();
			s_queryPositionsMatrixes.Dispose();
			s_resultHeights.Dispose();
        }

        /// <summary>
        /// Updates the query positions (creates space for them the first time). If the query count doesn't match a new set of query
        /// position data will be created. This will force any running jobs to complete. The jobs will be kicked off in LateUpdate,
        /// so this should be called before the kick-off, such as from Update.
        /// </summary>
        /// <returns>True if successful.</returns>
        public static bool UpdateQueryPoints(int guid, Vector3[] queryPoints)
        {
            // Call this in case the user has not called it.
            CompleteJobs();

            // Get segment
            var segmentRetrieved = false;
            int2 querySegment;
            if (s_segmentRegistry.TryGetValue(guid, out querySegment))
            {
                // make sure segment size matches our query count
                var segmentSize = querySegment[1] - querySegment[0];
                if (segmentSize == queryPoints.Length)
                {
                    // All good
                    segmentRetrieved = true;
                }
                else
                {
                    // Query count does not match segment - remove it. The segment will be recreated below.
                    s_segmentRegistry.Remove(guid);
                }
            }

            // If no segment was retrieved, add one if there is space
            if (!segmentRetrieved)
            {
                if (s_lastQueryIndexHeights + queryPoints.Length > MAX_QUERIES)
                {
                    Debug.LogError("Out of query data space. Try calling Compact() to reorganise query segments.");
                    return false;
                }

                querySegment = new int2(s_lastQueryIndexHeights, s_lastQueryIndexHeights + queryPoints.Length);
                s_segmentRegistry.Add(guid, querySegment);
                s_lastQueryIndexHeights += queryPoints.Length;
            }

            // Save off the query data
            for (var i = querySegment.x; i < querySegment.y; i++)
            {
                s_localQueryPositions[i] = queryPoints[i - querySegment.x];
				s_queryPositionsMatrixes[i] = new Matrix4x4();
			}

            return true;
        }

		/// <summary>
		/// Updates the query positions and the boat matrix (creates space for them the first time). If the query count doesn't match a new set of query
		/// position data will be created. This will force any running jobs to complete. The jobs will be kicked off in LateUpdate,
		/// so this should be called before the kick-off, such as from Update.
		/// </summary>
		/// <returns>True if successful.</returns>
		public static bool UpdateQueryPoints(int guid, Matrix4x4 currentBoatMatrix, Vector3[] localQueryPoints)
		{
			// Call this in case the user has not called it.
			CompleteJobs();

			// Get segment
			var segmentRetrieved = false;
			int2 querySegment;
			if(s_segmentRegistry.TryGetValue(guid, out querySegment))
			{
				// make sure segment size matches our query count
				var segmentSize = querySegment[1] - querySegment[0];
				if(segmentSize == localQueryPoints.Length)
				{
					// All good
					segmentRetrieved = true;
				}
				else
				{
					// Query count does not match segment - remove it. The segment will be recreated below.
					s_segmentRegistry.Remove(guid);
				}
			}

			// If no segment was retrieved, add one if there is space
			if(!segmentRetrieved)
			{
				if(s_lastQueryIndexHeights + localQueryPoints.Length > MAX_QUERIES)
				{
					Debug.LogError("Out of query data space. Try calling Compact() to reorganise query segments.");
					return false;
				}

				querySegment = new int2(s_lastQueryIndexHeights, s_lastQueryIndexHeights + localQueryPoints.Length);
				s_segmentRegistry.Add(guid, querySegment);
				s_lastQueryIndexHeights += localQueryPoints.Length;
			}

			// Save off the query data
			// This could be changed to read the remote native array as a reference and reduce the if statement over this
			NativeArray<Vector3>.Copy(localQueryPoints, 0, s_localQueryPositions, querySegment.x, querySegment.y - querySegment.x);

			NativeArray<Matrix4x4> boatMatrixes = new NativeArray<Matrix4x4>(1, Allocator.Temp);

			for(var i = querySegment.x; i < querySegment.y; i++)
				s_queryPositionsMatrixes[i] = currentBoatMatrix;

			return true;
		}

		/// <summary>
		/// Signal that the query storage for a particular guid is no longer required. This will leave air bubbles in the buffer -
		/// call CompactQueryStorage() to reorganise.
		/// </summary>
		public static void RemoveQueryPoints(int guid)
        {
            if (s_segmentRegistry.ContainsKey(guid))
            {
                s_segmentRegistry.Remove(guid);
            }
        }

        /// <summary>
        /// Change segment IDs to make them contiguous. This will invalidate any jobs and job results!
        /// </summary>
        public static void CompactQueryStorage()
        {
            // Make sure jobs are not running
            CompleteJobs();

            // A bit sneaky but just clear the registry. Will force segments to recreate which achieves the desired effect.
            s_segmentRegistry.Clear();
            s_lastQueryIndexHeights = 0;
        }

        /// <summary>
        /// Retrieve result data from jobs.
        /// </summary>
        /// <returns>True if data returned, false if failed</returns>
        public static bool RetrieveResultHeights(int guid, ref float[] outHeights)
        {
            var segment = new int2(0, 0);
            if (!s_segmentRegistry.TryGetValue(guid, out segment))
            {
                return false;
            }

            s_resultHeights.Slice(segment.x, segment.y - segment.x).CopyTo(outHeights);
            return true;
        }

        /// <summary>
        /// Run the jobs
        /// </summary>
        /// <returns>True if jobs kicked off, false if jobs already running.</returns>
        public static bool ScheduleJobs()
        {
            if (s_jobsRunning)
            {
                return false;
            }

            if (s_lastQueryIndexHeights == 0)
            {
                // Nothing to do
                return true;
            }

            s_jobsRunning = true;

			var heightJob = new HeightJob()
			{
				_waveNumbers = s_waveNumbers,
				_amps = s_amps,
				_windDirX = s_windDirX,
				_windDirZ = s_windDirZ,
				_phases = s_phases,
				_chopAmps = s_chopAmps,
				_numWaveVecs = _waveVecCount,
				_localQueryPositions = s_localQueryPositions,
				_boatMatrixes = s_queryPositionsMatrixes,
                _computeSegment = new int2(0, s_localQueryPositions.Length),
                _time = OceanRenderer.Instance.CurrentTime,
                _outHeights = s_resultHeights,
                _seaLevel = OceanRenderer.Instance.SeaLevel,
            };

            s_handleHeights = heightJob.Schedule(s_lastQueryIndexHeights, 32);

            JobHandle.ScheduleBatchedJobs();

            s_firstFrame = false;

            return true;
        }

        /// <summary>
        /// Ensure that jobs are completed. Blocks until complete.
        /// </summary>
        public static void CompleteJobs()
        {
            if (!s_firstFrame && s_jobsRunning)
            {
                s_handleHeights.Complete();
                s_jobsRunning = false;
            }
        }

        /// <summary>
        /// This returns the vertical component of the wave displacement at a position.
        /// </summary>
        [BurstCompile]
        public struct VerticalDisplacementJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float4> _waveNumbers;
            [ReadOnly]
            public NativeArray<float4> _amps;
            [ReadOnly]
            public NativeArray<float4> _windDirX;
            [ReadOnly]
            public NativeArray<float4> _windDirZ;
            [ReadOnly]
            public NativeArray<float4> _phases;
            [ReadOnly]
            public NativeArray<float4> _chopAmps;
            [ReadOnly]
            public int _numWaveVecs;

            [ReadOnly]
            public NativeArray<float2> _queryPositions;

            [WriteOnly]
            public NativeArray<float> _outHeights;

            [ReadOnly]
            public float _time;
            [ReadOnly]
            public float _globalWindAngle;
            [ReadOnly]
            public int2 _computeSegment;
            [ReadOnly]
            public float _seaLevel;

            public void Execute(int index)
            {
                if (index >= _computeSegment.x && index < _computeSegment.y - _computeSegment.x)
                {
                    float resultHeight = 0f;

                    for (var iwavevec = 0; iwavevec < _numWaveVecs; iwavevec++)
                    {
                        // Wave direction
                        float4 Dx = _windDirX[iwavevec], Dz = _windDirZ[iwavevec];

                        // Wave number
                        float4 k = _waveNumbers[iwavevec];

                        // SIMD Dot product of wave direction with query pos
                        float4 x = Dx * _queryPositions[index].x + Dz * _queryPositions[index].y;

                        // Angle
                        float4 t = k * x + _phases[iwavevec];

                        resultHeight += math.csum(_amps[iwavevec] * math.cos(t));
                    }

                    _outHeights[index] = resultHeight + _seaLevel;
                }
            }
        }

		/// <summary>
		/// This inverts the displacement to get the true water height at a position.
		/// </summary>
		[BurstCompile]
		public struct HeightJob : IJobParallelFor
        {
			// TODO - Allow use to not use some waves in case we want less accuracy on the boat

            [ReadOnly]
            public NativeArray<float4> _waveNumbers;
            [ReadOnly]
            public NativeArray<float4> _amps;
            [ReadOnly]
            public NativeArray<float4> _windDirX;
            [ReadOnly]
            public NativeArray<float4> _windDirZ;
            [ReadOnly]
            public NativeArray<float4> _phases;
            [ReadOnly]
            public NativeArray<float4> _chopAmps;
            [ReadOnly]
            public int _numWaveVecs;

			[ReadOnly]
			public NativeArray<Matrix4x4> _boatMatrixes;
			[ReadOnly]
            public NativeArray<Vector3> _localQueryPositions;

            [WriteOnly]
            public NativeArray<float> _outHeights;

            [ReadOnly]
            public float _time;
            [ReadOnly]
            public int2 _computeSegment;
            [ReadOnly]
            public float _seaLevel;

            public void Execute(int index)
            {
				if (index >= _computeSegment.x && index < _computeSegment.y - _computeSegment.x)
                {
					float3 worldPos = _boatMatrixes[index].MultiplyPoint3x4(_localQueryPositions[index]);
					float2 queryPosition = new float2(worldPos.x, worldPos.z);

					// This could be even faster if i could allocate scratch space to store intermediate calculation results (not supported by burst yet)

					float2 undisplacedPos = queryPosition;

                    for (int iter = 0; iter < 4; iter++)
                    {
                        float2 displacement = ComputeDisplacementHoriz(undisplacedPos);

                        // Correct the undisplaced position - goal is to find the position that displaces to the query position
                        float2 error = undisplacedPos + displacement - queryPosition;
                        undisplacedPos -= error;
                    }

                    // Our height is now the vertical component of the displacement from the undisp pos
                    _outHeights[index] = ComputeDisplacementVert(undisplacedPos) + _seaLevel;
                }
            }

			float2 ComputeDisplacementHoriz(float2 queryPos)
			{
				float2 displacement = 0f;

				for(var iwavevec = 0; iwavevec < _numWaveVecs; iwavevec++)
				{
					// Wave direction
					float4 Dx = _windDirX[iwavevec], Dz = _windDirZ[iwavevec];

					// Wave number
					float4 k = _waveNumbers[iwavevec];
					
					// SIMD Dot product of wave direction with query pos
					float4 x = Dx * queryPos.x + Dz * queryPos.y;

					// Angle
					float4 t = k * x + _phases[iwavevec];

					// Add the four SIMD results
					float4 disp = -_chopAmps[iwavevec] * math.sin(t);
					displacement.x += math.csum(Dx * disp);
					displacement.y += math.csum(Dz * disp);
				}

				return displacement;
			}

			float ComputeDisplacementVert(float2 queryPos)
			{
				float height = 0f;

				for(var iwavevec = 0; iwavevec < _numWaveVecs; iwavevec++)
				{
					// Wave direction
					float4 Dx = _windDirX[iwavevec], Dz = _windDirZ[iwavevec];

					// Wave number
					float4 k = _waveNumbers[iwavevec];

					// SIMD Dot product of wave direction with query pos
					float4 x = Dx * queryPos.x + Dz * queryPos.y;

					// Angle
					float4 t = k * x + _phases[iwavevec];

					// Add the four SIMD results
					height += math.csum(_amps[iwavevec] * math.cos(t));
				}

				return height;
			}
		}
    }
}
