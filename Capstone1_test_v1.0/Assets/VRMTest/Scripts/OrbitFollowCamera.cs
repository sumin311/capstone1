using UnityEngine;

namespace CapstoneVrmTest
{
    /// <summary>
    /// Third-person camera: hold the right mouse button to orbit, scroll to zoom.
    /// </summary>
    public class OrbitFollowCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 targetOffset = new Vector3(0f, 1.4f, 0f);
        public float distance = 3f;
        public float minDistance = 0.8f;
        public float maxDistance = 8f;
        public float sensitivity = 3f;
        public float yaw;
        public float pitch = 12f;

        void Start()
        {
            if (target != null) yaw = target.eulerAngles.y;
        }

        void LateUpdate()
        {
            if (target == null) return;

            if (TestInput.RightMouseHeld())
            {
                Vector2 d = TestInput.MouseDelta();
                yaw += d.x * sensitivity;
                pitch -= d.y * sensitivity;
                pitch = Mathf.Clamp(pitch, -30f, 80f);
            }

            distance = Mathf.Clamp(distance - TestInput.Scroll() * 0.5f, minDistance, maxDistance);

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 focus = target.position + targetOffset;
            transform.position = focus - rot * Vector3.forward * distance;
            transform.rotation = rot;
        }
    }
}
