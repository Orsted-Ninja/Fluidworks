using UnityEngine;

namespace AeroFlow.Core
{
    public class PartMotionAnimator : MonoBehaviour
    {
        public PartRegistry partRegistry;
        public bool applyOnlyWhenPlaying = true;
        public bool updateInFixedUpdate = false;
        public bool drawAxisGizmos = true;
        public Color axisColor = Color.cyan;
        public float axisLength = 5.0f;

        private void LateUpdate()
        {
            if (!updateInFixedUpdate)
            {
                Tick(Time.deltaTime);
            }
        }

        private void FixedUpdate()
        {
            if (updateInFixedUpdate)
            {
                Tick(Time.fixedDeltaTime);
            }
        }

        private void Tick(float dt)
        {
            if (partRegistry == null) return;
            if (applyOnlyWhenPlaying && !IsSimulationRunning()) return;
            if (dt <= 0f) return;

            var parts = partRegistry.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part == null || part.partTransform == null || part.motionSettings == null) continue;
                if (part.motionSettings.motionType != PartMotionType.ConstantRotation) continue;

                Vector3 axis = part.motionSettings.useWorldAxis
                    ? part.motionSettings.axis
                    : part.partTransform.TransformDirection(part.motionSettings.axis);
                if (axis.sqrMagnitude < 1e-6f) continue;
                axis.Normalize();

                Vector3 pivot = part.motionSettings.useWorldPivotOverride
                    ? part.motionSettings.worldPivotOverride
                    : part.partTransform.position + part.partTransform.TransformVector(part.motionSettings.pivotOffset);

                float degrees = part.motionSettings.intensity * 6f * dt;
                if (Mathf.Abs(degrees) < 1e-5f) continue;

                part.partTransform.RotateAround(pivot, axis, degrees);

                if (drawAxisGizmos)
                {
                    Debug.DrawLine(pivot - axis * (axisLength * 0.5f), pivot + axis * (axisLength * 0.5f), axisColor);
                }
            }
        }

        private bool IsSimulationRunning()
        {
            if (!Application.isPlaying) return false;

            var machinery = Object.FindFirstObjectByType<AeroFlow.Sim3D.RotatingMachinery.RotatingMachinerySimulation3D>();
            if (machinery != null)
            {
                return !machinery.IsPaused;
            }

            var wind = Object.FindFirstObjectByType<global::WindTunnelSimulation3D>();
            if (wind != null)
            {
                return !wind.IsPaused;
            }

            var damBreak = Object.FindFirstObjectByType<global::Simulation3D>();
            if (damBreak != null)
            {
                return !damBreak.IsPaused;
            }

            var pipe = Object.FindFirstObjectByType<AeroFlow.Sim3D.PipeFlow.PipeFlowSimulation3D>();
            if (pipe != null)
            {
                return !pipe.IsPaused;
            }

            return true;
        }
    }
}
