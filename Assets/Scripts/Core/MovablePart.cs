using UnityEngine;

namespace AeroFlow.Core
{
    [RequireComponent(typeof(Rigidbody))]
    public class MovablePart : MonoBehaviour
    {
        public string partId = "part";
        [Range(0f, 10f)] public float forceScale = 1f;
        [Range(0f, 10f)] public float torqueScale = 1f;

        private Rigidbody rb;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.useGravity = false;
        }

        public void ApplyFluidLoad(Vector3 forceWorld, Vector3 applicationPointWorld, Vector3 torqueWorld)
        {
            if (rb == null || rb.isKinematic) return;

            rb.AddForceAtPosition(forceWorld * forceScale, applicationPointWorld, ForceMode.Force);
            rb.AddTorque(torqueWorld * torqueScale, ForceMode.Force);
        }
    }
}
