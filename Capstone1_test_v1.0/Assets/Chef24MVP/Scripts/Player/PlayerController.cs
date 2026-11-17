using UnityEngine;

namespace Chef24
{
    /// <summary>
    /// First person movement (WASD) and mouse look inside the food truck (SFR-002, COR-002).
    /// Movement and look stop while <see cref="Locked"/> (cooking bar, pause, result screen).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public Transform cameraPivot;
        public float moveSpeed = 2.6f;
        public float lookSensitivity = 2.2f;
        public float gravity = -9.81f;

        public bool Locked { get; private set; }

        CharacterController body;
        float yaw, pitch, verticalVelocity;

        void Awake()
        {
            body = GetComponent<CharacterController>();
            yaw = transform.eulerAngles.y;
        }

        public void SetLocked(bool locked)
        {
            Locked = locked;
            Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = locked;
        }

        void Update()
        {
            if (Locked) return;

            Vector2 look = GameInput.Look() * lookSensitivity;
            yaw += look.x;
            pitch = Mathf.Clamp(pitch - look.y, -80f, 80f);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            Vector2 input = GameInput.Move();
            Vector3 move = transform.right * input.x + transform.forward * input.y;
            if (move.sqrMagnitude > 1f) move.Normalize();

            if (body.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;

            Vector3 velocity = move * moveSpeed;
            velocity.y = verticalVelocity;
            body.Move(velocity * Time.deltaTime);
        }
    }
}
