using AeroFlow.Core;
using UnityEngine;

namespace AeroFlow.Physics
{
    public class FluidLoadIntegrator : MonoBehaviour
    {
        [Header("References")]
        public PartRegistry partRegistry;
        public WindTunnelSimulation3D windTunnelSimulation;

        [Header("Force Model")]
        [Range(0.1f, 3f)] public float dragCoefficient = 1.1f;
        [Range(0f, 2f)] public float torqueGain = 0.35f;
        public bool applyOnlyWhenPlaying = true;

        private void FixedUpdate()
        {
            if (partRegistry == null || windTunnelSimulation == null) return;
            if (applyOnlyWhenPlaying && !Application.isPlaying) return;

            var settings = windTunnelSimulation.settings;
            float rho = Mathf.Max(settings.airDensity, 0.01f);
            float speed = Mathf.Max(settings.inletVelocity, 0f);
            if (speed <= 0.001f) return;

            Vector3 windDir = windTunnelSimulation.ResolveWindDirection();
            float dynamicPressure = 0.5f * rho * speed * speed;

            var parts = partRegistry.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part == null || part.partTransform == null) continue;
                var movable = part.partTransform.GetComponent<MovablePart>();
                if (movable == null) continue;

                if (!partRegistry.TryGetPartBounds(part, out var bounds)) continue;

                float area = Mathf.Max(part.referenceArea, 1e-4f);
                Vector3 force = windDir * (dynamicPressure * dragCoefficient * area);
                Vector3 lever = bounds.center - part.partTransform.position;
                Vector3 torque = Vector3.Cross(lever, force) * torqueGain;

                movable.ApplyFluidLoad(force, bounds.center, torque);
            }
        }
    }
}
