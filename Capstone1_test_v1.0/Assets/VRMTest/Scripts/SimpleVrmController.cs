using UnityEngine;

namespace CapstoneVrmTest
{
    /// <summary>
    /// WASD movement relative to the camera, Shift to run, Space to jump.
    /// If the Animator has a controller with a float parameter named "Speed",
    /// it is driven automatically (handy once you add Mixamo idle/walk/run clips).
    /// "Jump" (trigger) and "Grounded" (bool) are driven too when they exist.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class SimpleVrmController : MonoBehaviour
    {
        public float walkSpeed = 2f;
        public float runSpeed = 5f;
        public float turnSpeed = 720f;
        public float jumpHeight = 1f;
        public float gravity = -9.81f;
        public Transform cameraTransform;

        public float CurrentSpeed { get; private set; }

        CharacterController controller;
        Animator animator;
        bool hasSpeedParam;
        bool hasJumpParam;
        bool hasGroundedParam;
        float verticalVelocity;

        void Start()
        {
            controller = GetComponent<CharacterController>();
            animator = GetComponentInChildren<Animator>();
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;

            if (animator != null && animator.runtimeAnimatorController != null)
            {
                foreach (var p in animator.parameters)
                {
                    if (p.name == "Speed" && p.type == AnimatorControllerParameterType.Float) hasSpeedParam = true;
                    if (p.name == "Jump" && p.type == AnimatorControllerParameterType.Trigger) hasJumpParam = true;
                    if (p.name == "Grounded" && p.type == AnimatorControllerParameterType.Bool) hasGroundedParam = true;
                }
            }
        }

        void Update()
        {
            Vector2 input = TestInput.Move();

            Vector3 forward = cameraTransform != null
                ? Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized
                : Vector3.forward;
            if (forward.sqrMagnitude < 0.001f) forward = transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            Vector3 dir = forward * input.y + right * input.x;
            if (dir.sqrMagnitude > 1f) dir.Normalize();

            float speed = TestInput.Run() ? runSpeed : walkSpeed;

            if (dir.sqrMagnitude > 0.001f)
            {
                Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
            }

            if (controller.isGrounded)
            {
                if (verticalVelocity < 0f) verticalVelocity = -2f;
                if (TestInput.JumpDown())
                {
                    verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                    if (hasJumpParam) animator.SetTrigger("Jump");
                }
            }
            verticalVelocity += gravity * Time.deltaTime;

            Vector3 velocity = dir * speed;
            velocity.y = verticalVelocity;
            controller.Move(velocity * Time.deltaTime);

            CurrentSpeed = dir.magnitude * speed;
            if (hasSpeedParam) animator.SetFloat("Speed", CurrentSpeed, 0.1f, Time.deltaTime);
            if (hasGroundedParam) animator.SetBool("Grounded", controller.isGrounded);
        }
    }
}
