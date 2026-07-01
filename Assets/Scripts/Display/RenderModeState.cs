using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AeroFlow.Display
{
    public class RenderModeState : MonoBehaviour
    {
        [SerializeField] private Material[] _originalMaterials;
        private bool _cached;
        private WireframeOverlay _overlay;

        public void CacheOriginal(Renderer renderer)
        {
            if (_cached || renderer == null) return;
            _originalMaterials = renderer.sharedMaterials;
            _cached = true;
        }

        public void ApplyOriginal(Renderer renderer)
        {
            if (renderer == null || _originalMaterials == null || _originalMaterials.Length == 0) return;
            renderer.sharedMaterials = _originalMaterials;
        }

        public void ApplyPressure(Renderer renderer, Material pressureMaterial)
        {
            if (renderer == null || pressureMaterial == null) return;
            var mats = new Material[renderer.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = pressureMaterial;
            renderer.sharedMaterials = mats;
        }

        public void SetWireframeOverlay(bool enabled, Material wireframeMaterial)
        {
            if (_overlay == null)
            {
                var mf = GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) return;
                _overlay = WireframeOverlay.Attach(gameObject, mf.sharedMesh, wireframeMaterial);
            }

            if (_overlay != null)
            {
                _overlay.SetEnabled(enabled);
            }
        }

        public void ApplyGhost(Renderer renderer, Material ghostMaterial)
        {
            if (renderer == null || ghostMaterial == null) return;
            var mats = new Material[renderer.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = ghostMaterial;
            renderer.sharedMaterials = mats;
        }

        public void ApplySegmentationTint(Renderer renderer, Material tintMaterial)
        {
            if (renderer == null || tintMaterial == null) return;
            var mats = new Material[renderer.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = tintMaterial;
            renderer.sharedMaterials = mats;
        }
    }
}
