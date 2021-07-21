// Taken from:
// https://github.com/keijiro/LightProbeUtility/blob/85c93577338e10a52dd53f263056de08d883337a/Assets/LightProbeUtility.cs

namespace Crest
{
    using UnityEngine;
    using UnityEngine.Rendering;

    public static class LightProbeUtility
    {
        // Set SH coefficients to MaterialPropertyBlock
        public static void SetSHCoefficients(
            Vector3 position, MaterialPropertyBlock properties
        )
        {
            SphericalHarmonicsL2 sh;
            LightProbes.GetInterpolatedProbe(position, null, out sh);

            // Constant + Linear
            for (var i = 0; i < 3; i++)
                properties.SetVector(_idSHA[i], new Vector4(
                    sh[i, 3], sh[i, 1], sh[i, 2], sh[i, 0] - sh[i, 6]
                ));

            // Quadratic polynomials
            for (var i = 0; i < 3; i++)
                properties.SetVector(_idSHB[i], new Vector4(
                    sh[i, 4], sh[i, 6], sh[i, 5] * 3, sh[i, 7]
                ));

            // Final quadratic polynomial
            properties.SetVector(_idSHC, new Vector4(
                sh[0, 8], sh[2, 8], sh[1, 8], 1
            ));
        }

        // Set SH coefficients to Material
        public static void SetSHCoefficients(
            Vector3 position, Material material
        )
        {
            SphericalHarmonicsL2 sh;
            LightProbes.GetInterpolatedProbe(position, null, out sh);

            // Constant + Linear
            for (var i = 0; i < 3; i++)
                material.SetVector(_idSHA[i], new Vector4(
                    sh[i, 3], sh[i, 1], sh[i, 2], sh[i, 0] - sh[i, 6]
                ));

            // Quadratic polynomials
            for (var i = 0; i < 3; i++)
                material.SetVector(_idSHB[i], new Vector4(
                    sh[i, 4], sh[i, 6], sh[i, 5] * 3, sh[i, 7]
                ));

            // Final quadratic polynomial
            material.SetVector(_idSHC, new Vector4(
                sh[0, 8], sh[2, 8], sh[1, 8], 1
            ));
        }

        static int[] _idSHA = {
            Shader.PropertyToID("unity_SHAr"),
            Shader.PropertyToID("unity_SHAg"),
            Shader.PropertyToID("unity_SHAb")
        };

        static int[] _idSHB = {
            Shader.PropertyToID("unity_SHBr"),
            Shader.PropertyToID("unity_SHBg"),
            Shader.PropertyToID("unity_SHBb")
        };

        static int _idSHC =
            Shader.PropertyToID("unity_SHC");
    }
}

