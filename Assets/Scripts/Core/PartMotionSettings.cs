using UnityEngine;

namespace AeroFlow.Core
{
    public enum PartMotionType
    {
        Static,
        ConstantRotation,
        ConstantTranslation,
        PhysicsDriven
    }

    /// <summary>
    /// Stores mechanical motion properties for a segmented model part.
    /// These properties are used by the voxelizer to generate surface velocity fields.
    /// </summary>
    public class PartMotionSettings : MonoBehaviour
    {
        public string partId;
        public PartMotionType motionType = PartMotionType.Static;
        
        [Header("Motion Parameters")]
        public float intensity = 0f; // RPM for rotation, m/s for translation
        public Vector3 axis = Vector3.up;
        public Vector3 pivotOffset = Vector3.zero; // Local offset from part center
        public bool useWorldAxis = false;
        public bool useWorldPivotOverride = false;
        public Vector3 worldPivotOverride = Vector3.zero;

        public Vector3 GetWorldVelocityAtPoint(Vector3 worldPoint)
        {
            if (motionType == PartMotionType.Static) return Vector3.zero;

            Vector3 worldAxis = useWorldAxis ? axis.normalized : transform.TransformDirection(axis).normalized;
            
            if (motionType == PartMotionType.ConstantTranslation)
            {
                return worldAxis * intensity;
            }

            if (motionType == PartMotionType.ConstantRotation)
            {
                // v = omega x r
                float omega = intensity * Mathf.PI / 30f; // RPM to rad/s
                Vector3 worldPivot = useWorldPivotOverride
                    ? worldPivotOverride
                    : transform.position + transform.TransformVector(pivotOffset);
                Vector3 r = worldPoint - worldPivot;
                return Vector3.Cross(worldAxis * omega, r);
            }

            return Vector3.zero;
        }

        public void AutoConfigure()
        {
            if (string.IsNullOrEmpty(partId)) partId = gameObject.name;
            
            string lowerName = partId.ToLower();
            if (lowerName.Contains("blade") || lowerName.Contains("fin") || lowerName.Contains("rotor") ||
                lowerName.Contains("runner") || lowerName.Contains("impeller") || lowerName.Contains("turbine") ||
                lowerName.Contains("propeller") || lowerName.Contains("fan") || lowerName.Contains("windmill"))
            {
                motionType = PartMotionType.ConstantRotation;
                intensity = 1500f; // Default RPM
                axis = Vector3.up;
            }
            else if (lowerName.Contains("wheel") || lowerName.Contains("tire"))
            {
                motionType = PartMotionType.ConstantRotation;
                intensity = 800f;
                axis = Vector3.right;
            }
        }

        public void SetWorldPivot(Vector3 pivot)
        {
            useWorldPivotOverride = true;
            worldPivotOverride = pivot;
        }

        public void ClearWorldPivot()
        {
            useWorldPivotOverride = false;
            worldPivotOverride = Vector3.zero;
        }
    }
}
