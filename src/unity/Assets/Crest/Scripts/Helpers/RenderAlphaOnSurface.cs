﻿using UnityEngine;

namespace Crest
{
    /// <summary>
    /// Helper script for alpha geometry rendering on top of ocean surface. This is required to select the best
    /// LOD and assign the shape texture to the material.
    /// </summary>
    public class RenderAlphaOnSurface : MonoBehaviour
    {
        MaterialPropertyBlock _mpb;
        Renderer _rend;

        private void Start()
        {
            _rend = GetComponent<Renderer>();
        }

        private void LateUpdate()
        {
            // find which lod this object is overlapping
            var rect = new Rect(transform.position.x, transform.position.z, 0f, 0f);
            var idx = WaveDataCam.SuggestCollisionLOD(rect);

            if (idx > -1)
            {
                if (_mpb == null)
                {
                    _mpb = new MaterialPropertyBlock();
                }

                _rend.GetPropertyBlock(_mpb);

                var wdcs = OceanRenderer.Instance.Builder._shapeWDCs;
                wdcs[idx].ApplyMaterialParams(0, _mpb);
                int idx1 = Mathf.Min(idx + 1, wdcs.Length - 1);
                wdcs[idx1].ApplyMaterialParams(1, _mpb);

                // blend LOD 0 shape in/out to avoid pop, if the ocean might scale up later (it is smaller than its maximum scale)
                bool needToBlendOutShape = idx == 0 && OceanRenderer.Instance.ScaleCouldIncrease;
                float meshScaleLerp = needToBlendOutShape ? OceanRenderer.Instance.ViewerAltitudeLevelAlpha : 0f;

                // blend furthest normals scale in/out to avoid pop, if scale could reduce
                bool needToBlendOutNormals = idx == wdcs.Length - 1 && OceanRenderer.Instance.ScaleCouldDecrease;
                float farNormalsWeight = needToBlendOutNormals ? OceanRenderer.Instance.ViewerAltitudeLevelAlpha : 1f;
                _mpb.SetVector("_InstanceData", new Vector4(meshScaleLerp, farNormalsWeight, idx));

                _rend.SetPropertyBlock(_mpb);
            }
        }
    }
}
