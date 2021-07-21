// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

namespace Crest
{
    using System.Collections.Generic;
    using UnityEngine;

    // TODO: Maybe rename.
    public class RegisterUnderwaterInput : MonoBehaviour
    {
        public static readonly List<Renderer> s_Renderers = new List<Renderer>();
        Renderer _renderer;

        void OnEnable()
        {
            if (TryGetComponent(out _renderer) && !s_Renderers.Contains(_renderer))
            {
                _renderer.enabled = false;
                s_Renderers.Add(_renderer);
            }
        }

        void LateUpdate()
        {
            // Quick and dirty optimisation.
            var maxWaterHeight = OceanRenderer.Instance.SeaLevel + OceanRenderer.Instance.MaxVertDisplacement;
            // TODO: Throws exceptions when renderer is disabled for ParticleSystem.
            // TODO: Probably a better to check this.
            _renderer.enabled = _renderer.bounds.ClosestPoint(transform.position + Vector3.down * 10000f).y > maxWaterHeight;
        }

        void OnDisable()
        {
            if (_renderer != null && s_Renderers.Contains(_renderer))
            {
                _renderer.enabled = true;
                s_Renderers.Remove(_renderer);
            }
        }
    }
}
