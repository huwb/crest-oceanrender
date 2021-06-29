// Crest Ocean System

// Copyright 2021 Wave Harmonic Ltd

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using UnityEngine;
using UnityEngine.Rendering;

namespace Crest
{
    public class FFTBaker
    {
        public static bool Bake(ShapeFFT fftWaves, int resolutionSpace, int resolutionTime, float period, float wavePatchWidth)
        {
            // TODO: assert wavePatchWidth is a power of 2
            // TODO: probably assert period and resolutions are powers of 2 as well. would not hurt..

            // create the staging texture wavePatchData that is written to then downloaded from the GPU
            var desc = new RenderTextureDescriptor
            {
                width = resolutionSpace,
                height = resolutionSpace,
                volumeDepth = 1,
                autoGenerateMips = false,
                colorFormat = RenderTextureFormat.ARGBFloat,
                enableRandomWrite = true,
                dimension = UnityEngine.Rendering.TextureDimension.Tex2D,
                depthBufferBits = 0,
                sRGB = false
            };
            var wavePatchData = new RenderTexture(desc);

            var buf = new CommandBuffer();

            // TODO
            var waveCombineShader = Resources.Load("some compute shader (see below)");

            for (int timeIndex = 0; timeIndex < resolutionTime; timeIndex++)
            {
                float t = period * timeIndex / (float)resolutionTime;

                buf.Clear();

                // generate multi-res FFT into a texture array
                var fftWaveDataTA = FFTCompute.GenerateDisplacements(buf, fftWaves._resolution, fftWaves._windTurbulence, fftWaves.WindDirRadForFFT, fftWaves.WindSpeedForFFT, t, fftWaves._spectrum, true);

                // Create a compute shader that generates the final waves:
                // - takes the fftWaveDataTA texture as input.
                // - outputs to the wavePatchData staging texture
                // - for each texel:
                //    - compute world position using UV and wavePatchWidth
                //    - initialise output to 0
                //    - loop over each slice in the wave data, compute slice UV. Similar to computation in AnimWavesGerstner.shader.
                //    - sample displacements from the slice and add them to the result. using Wrap sampling.
                //       - could convert to heightfield here, and only store a single value
                //       - some slices will be too high res. could omit, or maybe it doesnt matter.
                //buf.SetComputeTextureParam(waveCombineShader, "input", fftWaveDataTA);
                //buf.SetComputeTextureParam(waveCombineShader, "output", wavePatchData);
                // ...
                //buf.DispatchCompute();

                Graphics.ExecuteCommandBuffer(buf);

                // readback data to CPU
                // what was the trick to doing this again? copy the render texture to a normal texture then read it back? urgh
                //var data = wavePatchData.GetPixels();
            }

            // Save the data for each slice to disk - in some format?

            // Separately there should be a FFTCollisionProvider and the SimSettingsAnimWaves should allow
            // selecting this provider type, and also have a field for selecting the exported data.

            return true;
        }
    }
}
