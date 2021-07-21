// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

namespace Crest
{
    using System.Collections.Generic;
    using Unity.Collections;
    using UnityEngine;
    using UnityEngine.Rendering;

    public partial class UnderwaterRenderer
    {
        const string SHADER_UNDERWATER_EFFECT = "Hidden/Crest/Underwater/Underwater Effect";
        internal const string FULL_SCREEN_EFFECT = "_FULL_SCREEN_EFFECT";
        internal const string DEBUG_VIEW_OCEAN_MASK = "_DEBUG_VIEW_OCEAN_MASK";

        internal static readonly int sp_CrestCameraColorTexture = Shader.PropertyToID("_CrestCameraColorTexture");
        static readonly int sp_InvViewProjection = Shader.PropertyToID("_InvViewProjection");
        static readonly int sp_InvViewProjectionRight = Shader.PropertyToID("_InvViewProjectionRight");
        static readonly int sp_AmbientLighting = Shader.PropertyToID("_AmbientLighting");
        static readonly int sp_HorizonPosNormal = Shader.PropertyToID("_HorizonPosNormal");
        static readonly int sp_HorizonPosNormalRight = Shader.PropertyToID("_HorizonPosNormalRight");
        static readonly int sp_DataSliceOffset = Shader.PropertyToID("_DataSliceOffset");

        CommandBuffer _underwaterEffectCommandBuffer;
        PropertyWrapperMaterial _underwaterEffectMaterial;
        internal readonly UnderwaterSphericalHarmonicsData _sphericalHarmonicsData = new UnderwaterSphericalHarmonicsData();
        MaterialPropertyBlock _materialPropertyBlock;

        internal class UnderwaterSphericalHarmonicsData
        {
            internal Color[] _ambientLighting = new Color[1];
            internal Vector3[] _shDirections = { new Vector3(0.0f, 0.0f, 0.0f) };
        }

        CrestSortedList<float, Renderer> _registry = new CrestSortedList<float, Renderer>(new TransparentRenderOrderComparer());

        internal class TransparentRenderOrderComparer : IComparer<float>
        {
            int IComparer<float>.Compare(float x, float y)
            {
                return x.CompareTo(y) * -1;
            }
        }

        void SetupUnderwaterEffect()
        {
            if (_underwaterEffectMaterial?.material == null)
            {
                _underwaterEffectMaterial = new PropertyWrapperMaterial(SHADER_UNDERWATER_EFFECT);
            }

            if (_materialPropertyBlock == null)
            {
                _materialPropertyBlock = new MaterialPropertyBlock();
            }

            if (_underwaterEffectCommandBuffer == null)
            {
                _underwaterEffectCommandBuffer = new CommandBuffer()
                {
                    name = "Underwater Pass",
                };
            }
        }

        void OnPreRenderUnderwaterEffect()
        {
            // Ensure legacy underwater fog is disabled.
            if (_firstRender)
            {
                OceanRenderer.Instance.OceanMaterial.DisableKeyword("_OLD_UNDERWATER");
            }

            RenderTextureDescriptor descriptor = XRHelpers.GetRenderTextureDescriptor(_camera);
            descriptor.useDynamicScale = _camera.allowDynamicResolution;
            // Format must be correct for CopyTexture to work. Hopefully this is good enough.
            if (_camera.allowHDR)
            {
                descriptor.colorFormat = RenderTextureFormat.DefaultHDR;
            }

            var temporaryColorBuffer = RenderTexture.GetTemporary(descriptor);

            UpdatePostProcessMaterial(
                _camera,
                _underwaterEffectMaterial,
                _sphericalHarmonicsData,
                _meniscus,
                _firstRender || _copyOceanMaterialParamsEachFrame,
                _debug._viewOceanMask,
                // horizonSafetyMarginMultiplier is added to the horizon, so no-op is zero.
                _useHorizonSafetyMarginMultiplier ? _horizonSafetyMarginMultiplier : 0f,
                // farPlaneMultiplier is multiplied to the far plane, so no-op is one.
                _useHorizonSafetyMarginMultiplier ? 1f : _farPlaneMultiplier,
                _filterOceanData,
                s_xrPassIndex
            );

            _underwaterEffectCommandBuffer.Clear();

            CopyTexture(_underwaterEffectCommandBuffer, temporaryColorBuffer, _camera);

            _underwaterEffectMaterial.SetTexture(sp_CrestCameraColorTexture, temporaryColorBuffer);

            _underwaterEffectCommandBuffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget, 0, CubemapFace.Unknown, -1);
            _underwaterEffectCommandBuffer.DrawProcedural(Matrix4x4.identity, _underwaterEffectMaterial.material,
                shaderPass: 0, MeshTopology.Triangles, vertexCount: 3, instanceCount: 1);

            // NOTE: Sorting Transparent Objects Manually
            // We are sorting manually but Unity might provide a way as we still need to take TransparencySortMode
            // into account.

            // Add renderers if within frustum and sort transparency as Unity does.
            _registry.Clear();
            foreach (var renderer in RegisterUnderwaterInput.s_Renderers)
            {
                // Disabled renderer means we control the rendering.
                if (!renderer.enabled && GeometryUtility.TestPlanesAABB(_cameraFrustumPlanes, renderer.bounds))
                {
                    _registry.Add(Vector3.Distance(_camera.transform.position, renderer.transform.position),  renderer);
                }
            }

            // Enable probe sampling.
            _underwaterEffectCommandBuffer.EnableShaderKeyword("LIGHTPROBE_SH");

            foreach (var registered in _registry)
            {
                var renderer = registered.Value;

                // NOTE:
                // We only need to do this so we can set a white texture for when there is no texture. Otherwise,
                // _MainTex is already setup.

                // Set _MainTex so we can get the alpha channel for blending.
                var texture = renderer.sharedMaterial.GetTexture("_MainTex");
                if (texture != null)
                {
                    _materialPropertyBlock.SetTexture("_MainTex", renderer.sharedMaterial.GetTexture("_MainTex"));
                    _materialPropertyBlock.SetVector("_MainTex_ST", renderer.sharedMaterial.GetVector("_MainTex_ST"));
                }
                else
                {
                    _materialPropertyBlock.SetTexture("_MainTex", Texture2D.whiteTexture);
                }

                // Add missing probe data.
                LightProbeUtility.SetSHCoefficients(renderer.gameObject.transform.position, _materialPropertyBlock);
                renderer.SetPropertyBlock(_materialPropertyBlock, 0);

                // Render into temporary render texture so the effect shader will have colour to work with. I could not
                // work out how to use GPU blending to apply the underwater fog correctly.
                CopyTexture(_underwaterEffectCommandBuffer, temporaryColorBuffer, _camera);
                _underwaterEffectCommandBuffer.SetRenderTarget(temporaryColorBuffer, 0, CubemapFace.Unknown, -1);
                _underwaterEffectCommandBuffer.DrawRenderer(renderer, renderer.sharedMaterial, submeshIndex: 0, shaderPass: 0);

                // Render the fog and apply to camera target.
                _underwaterEffectCommandBuffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget, 0, CubemapFace.Unknown, -1);
                _underwaterEffectCommandBuffer.DrawRenderer(renderer, _underwaterEffectMaterial.material, submeshIndex: 0, shaderPass: 1);
            }

            _underwaterEffectCommandBuffer.DisableShaderKeyword("LIGHTPROBE_SH");

            RenderTexture.ReleaseTemporary(temporaryColorBuffer);
        }

        static void CopyTexture(CommandBuffer buffer, RenderTexture texture, Camera camera)
        {
            if (camera.allowMSAA)
            {
                // Use blit if MSAA is active because transparents were not included with CopyTexture.
                // Not sure if we need an MSAA resolve? Not sure how to do that...
                buffer.Blit(BuiltinRenderTextureType.CameraTarget, texture);
            }
            else
            {
                // Copy the frame buffer as we cannot read/write at the same time. If it causes problems, replace with Blit.
                buffer.CopyTexture(BuiltinRenderTextureType.CameraTarget, texture);
            }
        }

        internal static void UpdatePostProcessMaterial(
            Camera camera,
            PropertyWrapperMaterial underwaterPostProcessMaterialWrapper,
            UnderwaterSphericalHarmonicsData sphericalHarmonicsData,
            bool isMeniscusEnabled,
            bool copyParamsFromOceanMaterial,
            bool debugViewPostProcessMask,
            float horizonSafetyMarginMultiplier,
            float farPlaneMultiplier,
            int dataSliceOffset,
            int xrPassIndex
        )
        {
            Material underwaterPostProcessMaterial = underwaterPostProcessMaterialWrapper.material;
            if (copyParamsFromOceanMaterial)
            {
                // Measured this at approx 0.05ms on dell laptop
                underwaterPostProcessMaterial.CopyPropertiesFromMaterial(OceanRenderer.Instance.OceanMaterial);
            }

            // Enable/Disable meniscus.
            if (isMeniscusEnabled)
            {
                underwaterPostProcessMaterial.EnableKeyword("CREST_MENISCUS");
            }
            else
            {
                underwaterPostProcessMaterial.DisableKeyword("CREST_MENISCUS");
            }

            // Enabling/disabling keywords each frame don't seem to have large measurable overhead
            if (debugViewPostProcessMask)
            {
                underwaterPostProcessMaterial.EnableKeyword(DEBUG_VIEW_OCEAN_MASK);
            }
            else
            {
                underwaterPostProcessMaterial.DisableKeyword(DEBUG_VIEW_OCEAN_MASK);
            }

            // We sample shadows at the camera position which will be the first slice.
            // We also use this for caustics to get the displacement.
            underwaterPostProcessMaterial.SetFloat(LodDataMgr.sp_LD_SliceIndex, 0);
            underwaterPostProcessMaterial.SetInt(sp_DataSliceOffset, dataSliceOffset);

            LodDataMgrAnimWaves.Bind(underwaterPostProcessMaterialWrapper);
            LodDataMgrSeaFloorDepth.Bind(underwaterPostProcessMaterialWrapper);
            LodDataMgrShadow.Bind(underwaterPostProcessMaterialWrapper);

            float seaLevel = OceanRenderer.Instance.SeaLevel;

            // We don't both setting the horizon value if we know we are going to be having to apply the effect
            // full-screen anyway.
            var forceFullShader = OceanRenderer.Instance.ViewerHeightAboveWater < -2f;
            if (!forceFullShader)
            {
                // We only apply the horizon safety margin multiplier to horizon if and only if
                // concrete height of the camera relative to the water and the height of the camera
                // relative to the sea-level are the same. This ensures that in incredibly turbulent
                // water - if in doubt - use the neutral horizon.
                float seaLevelHeightDifference = camera.transform.position.y - seaLevel;
                if (seaLevelHeightDifference >= 0.0f ^ OceanRenderer.Instance.ViewerHeightAboveWater >= 0.0f)
                {
                    horizonSafetyMarginMultiplier = 0.0f;
                }

                float maxOceanVerticalDisplacement = OceanRenderer.Instance.MaxVertDisplacement * 0.5f;
                float cameraYPosition = camera.transform.position.y;
                float nearPlaneFrustumWorldHeight;
                {
                    float current = camera.ViewportToWorldPoint(new Vector3(0f, 0f, camera.nearClipPlane)).y;
                    float maxY = current, minY = current;

                    current = camera.ViewportToWorldPoint(new Vector3(0f, 1f, camera.nearClipPlane)).y;
                    maxY = Mathf.Max(maxY, current);
                    minY = Mathf.Min(minY, current);

                    current = camera.ViewportToWorldPoint(new Vector3(1f, 0f, camera.nearClipPlane)).y;
                    maxY = Mathf.Max(maxY, current);
                    minY = Mathf.Min(minY, current);

                    current = camera.ViewportToWorldPoint(new Vector3(1f, 1f, camera.nearClipPlane)).y;
                    maxY = Mathf.Max(maxY, current);
                    minY = Mathf.Min(minY, current);

                    nearPlaneFrustumWorldHeight = maxY - minY;
                }

                forceFullShader = (cameraYPosition + nearPlaneFrustumWorldHeight + maxOceanVerticalDisplacement) <= seaLevel;
            }

            if (forceFullShader)
            {
                underwaterPostProcessMaterial.EnableKeyword(FULL_SCREEN_EFFECT);
            }
            else
            {
                underwaterPostProcessMaterial.DisableKeyword(FULL_SCREEN_EFFECT);
            }

            // Have to set these explicitly as the built-in transforms aren't in world-space for the blit function
            if (XRHelpers.IsSinglePass)
            {
                // Store projection matrix to restore later.
                var projectionMatrix = camera.projectionMatrix;

                {
                    // ViewportToWorldPoint is bugged in HDRP so we have to set the matrix and not use the eye parameter.
                    camera.projectionMatrix = XRHelpers.LeftEyeProjectionMatrix;
                    GetHorizonPosNormal(camera, seaLevel, horizonSafetyMarginMultiplier, farPlaneMultiplier, out Vector2 pos, out Vector2 normal);
                    underwaterPostProcessMaterial.SetVector(sp_HorizonPosNormal, new Vector4(pos.x, pos.y, normal.x, normal.y));
                }

                {
                    // ViewportToWorldPoint is bugged in HDRP so we have to set the matrix and not use the eye parameter.
                    camera.projectionMatrix = XRHelpers.RightEyeProjectionMatrix;
                    GetHorizonPosNormal(camera, seaLevel, horizonSafetyMarginMultiplier, farPlaneMultiplier, out Vector2 pos, out Vector2 normal);
                    underwaterPostProcessMaterial.SetVector(sp_HorizonPosNormalRight, new Vector4(pos.x, pos.y, normal.x, normal.y));
                }

                // Restore projection matrix.
                camera.projectionMatrix = projectionMatrix;

                // NOTE: Not needed for HDRP.
                underwaterPostProcessMaterial.SetMatrix(sp_InvViewProjection, (GL.GetGPUProjectionMatrix(XRHelpers.LeftEyeProjectionMatrix, false) * XRHelpers.LeftEyeViewMatrix).inverse);
                underwaterPostProcessMaterial.SetMatrix(sp_InvViewProjectionRight, (GL.GetGPUProjectionMatrix(XRHelpers.RightEyeProjectionMatrix, false) * XRHelpers.RightEyeViewMatrix).inverse);
            }
            else
            {
                // NOTE: Needed for HDRP.
                XRHelpers.SetViewProjectionMatrices(camera, xrPassIndex);

                {
                    GetHorizonPosNormal(camera, seaLevel, horizonSafetyMarginMultiplier, farPlaneMultiplier, out Vector2 pos, out Vector2 normal);
                    underwaterPostProcessMaterial.SetVector(sp_HorizonPosNormal, new Vector4(pos.x, pos.y, normal.x, normal.y));
                }

                // NOTE: Not needed for HDRP.
                var inverseViewProjectionMatrix = (GL.GetGPUProjectionMatrix(camera.projectionMatrix, false) * camera.worldToCameraMatrix).inverse;
                underwaterPostProcessMaterial.SetMatrix(sp_InvViewProjection, inverseViewProjectionMatrix);
            }

            // Compute ambient lighting SH
            {
                // We could pass in a renderer which would prime this lookup. However it doesnt make sense to use an existing render
                // at different position, as this would then thrash it and negate the priming functionality. We could create a dummy invis GO
                // with a dummy Renderer which might be enoguh, but this is hacky enough that we'll wait for it to become a problem
                // rather than add a pre-emptive hack.

                UnityEngine.Profiling.Profiler.BeginSample("Underwater sample spherical harmonics");

                LightProbes.GetInterpolatedProbe(OceanRenderer.Instance.ViewCamera.transform.position, null, out SphericalHarmonicsL2 sphericalHarmonicsL2);
                sphericalHarmonicsL2.Evaluate(sphericalHarmonicsData._shDirections, sphericalHarmonicsData._ambientLighting);
                underwaterPostProcessMaterial.SetVector(sp_AmbientLighting, sphericalHarmonicsData._ambientLighting[0]);

                UnityEngine.Profiling.Profiler.EndSample();
            }
        }

        /// <summary>
        /// Compute intersection between the frustum far plane and the ocean plane, and return view space pos and normal
        /// for this horizon line.
        /// </summary>
        static void GetHorizonPosNormal(Camera camera, float seaLevel, float horizonSafetyMarginMultiplier, float farPlaneMultiplier, out Vector2 resultPos, out Vector2 resultNormal)
        {
            // Set up back points of frustum
            NativeArray<Vector3> v_screenXY_viewZ = new NativeArray<Vector3>(4, Allocator.Temp);
            NativeArray<Vector3> v_world = new NativeArray<Vector3>(4, Allocator.Temp);
            try
            {

                var farPlane = camera.farClipPlane * farPlaneMultiplier;
                v_screenXY_viewZ[0] = new Vector3(0f, 0f, farPlane);
                v_screenXY_viewZ[1] = new Vector3(0f, 1f, farPlane);
                v_screenXY_viewZ[2] = new Vector3(1f, 1f, farPlane);
                v_screenXY_viewZ[3] = new Vector3(1f, 0f, farPlane);

                // Project out to world
                for (int i = 0; i < v_world.Length; i++)
                {
                    // Eye parameter works for BIRP. With it we could skip setting matrices.
                    // In HDRP it doesn't work for XR MP. And completely breaks horizon in XR SPI.
                    v_world[i] = camera.ViewportToWorldPoint(v_screenXY_viewZ[i]);
                }

                NativeArray<Vector2> intersectionsScreen = new NativeArray<Vector2>(2, Allocator.Temp);
                // This is only used to disambiguate the normal later. Could be removed if we were more careful with point order/indices below.
                NativeArray<Vector3> intersectionsWorld = new NativeArray<Vector3>(2, Allocator.Temp);
                try
                {
                    var resultCount = 0;

                    // Iterate over each back point
                    for (int i = 0; i < 4; i++)
                    {
                        // Get next back point, to obtain line segment between them
                        var inext = (i + 1) % 4;

                        // See if one point is above and one point is below sea level - then sign of the two differences
                        // will be different, and multiplying them will give a negative
                        if ((v_world[i].y - seaLevel) * (v_world[inext].y - seaLevel) < 0f)
                        {
                            // Proportion along line segment where intersection occurs
                            float prop = Mathf.Abs((seaLevel - v_world[i].y) / (v_world[inext].y - v_world[i].y));
                            intersectionsScreen[resultCount] = Vector2.Lerp(v_screenXY_viewZ[i], v_screenXY_viewZ[inext], prop);
                            intersectionsWorld[resultCount] = Vector3.Lerp(v_world[i], v_world[inext], prop);

                            resultCount++;
                        }
                    }

                    // Two distinct results - far plane intersects water
                    if (resultCount == 2 /*&& (props[1] - props[0]).sqrMagnitude > 0.000001f*/)
                    {
                        resultPos = intersectionsScreen[0];
                        var tangent = intersectionsScreen[0] - intersectionsScreen[1];
                        resultNormal.x = -tangent.y;
                        resultNormal.y = tangent.x;

                        // Disambiguate the normal. The tangent normal might go from left to right or right to left
                        // since we do not handle ordering of intersection points.
                        if (Vector3.Dot(intersectionsWorld[0] - intersectionsWorld[1], camera.transform.right) > 0f)
                        {
                            resultNormal = -resultNormal;
                        }

                        // Invert the normal if camera is upside down.
                        if (camera.transform.up.y <= 0f)
                        {
                            resultNormal = -resultNormal;
                        }

                        // The above will sometimes produce a normal that is inverted around 90° along the Z axis. Here
                        // we are using world up to make sure that water is world down.
                        {
                            var cameraFacing = Vector3.Dot(camera.transform.right, Vector3.up);
                            var normalFacing = Vector2.Dot(resultNormal, Vector2.right);

                            if (cameraFacing > 0.75f && normalFacing > 0.9f)
                            {
                                resultNormal = -resultNormal;
                            }
                            else if (cameraFacing < -0.75f && normalFacing < -0.9f)
                            {
                                resultNormal = -resultNormal;
                            }
                        }

                        // Calculate a scale value so that the multiplier is consistent when rotating camera. We need
                        // to do this because we are working in view space which is always 0-1.
                        {
                            var angleFromWorldNormal = Mathf.Abs(Vector2.Angle(Vector2.up, -resultNormal.normalized) / 90f);
                            if (angleFromWorldNormal > 1f)
                            {
                                angleFromWorldNormal = Mathf.Abs(2f - angleFromWorldNormal);
                            }
                            horizonSafetyMarginMultiplier /= Mathf.Lerp(1f, camera.aspect, angleFromWorldNormal);
                        }

                        // Get the sign (with zero) of the camera-to-sea-level to set the multiplier direction. We don't
                        // want the distance as it will influence the size of the safety margin which it might then
                        // appear in turbulent water edge cases.
                        var cameraToSeaLevelSign = seaLevel - camera.transform.position.y;
                        cameraToSeaLevelSign = cameraToSeaLevelSign > 0f ? 1f : cameraToSeaLevelSign < 0f ? -1f : 0f;

                        // We want to invert the direction of the multiplier when underwater.
                        horizonSafetyMarginMultiplier *= -cameraToSeaLevelSign;
                        // For compatibility so previous 0.01f property value is the same strength as before.
                        horizonSafetyMarginMultiplier *= 0.01f;
                        // We use the normal so the multiplier is applied in the correct direction.
                        resultPos += resultNormal.normalized * horizonSafetyMarginMultiplier;
                    }
                    else
                    {
                        // 1 or 0 results - far plane either touches ocean plane or is completely above/below
                        resultNormal = Vector2.up;
                        bool found = false;
                        resultPos = default;
                        for (int i = 0; i < 4; i++)
                        {
                            if (v_world[i].y < seaLevel)
                            {
                                // Underwater
                                resultPos = Vector2.zero;
                                found = true;
                                break;
                            }
                            else if (v_world[i].y > seaLevel)
                            {
                                // Underwater
                                resultPos = Vector2.up;
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            throw new System.Exception("GetHorizonPosNormal: Could not determine if far plane is above or below water.");
                        }
                    }
                }
                finally
                {
                    intersectionsScreen.Dispose();
                    intersectionsWorld.Dispose();
                }
            }
            finally
            {
                v_screenXY_viewZ.Dispose();
                v_world.Dispose();
            }
        }
    }
}
