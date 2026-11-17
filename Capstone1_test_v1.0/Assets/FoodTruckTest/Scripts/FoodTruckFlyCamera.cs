using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CapstoneFoodTruckTest
{
    /// <summary>
    /// Free camera for looking around the food truck.
    /// Right mouse drag: look, WASD: move, Q/E: down/up, Shift: faster, wheel: dolly, H: hide help.
    /// Works with both the new Input System and the legacy Input Manager.
    /// </summary>
    public class FoodTruckFlyCamera : MonoBehaviour
    {
        public float moveSpeed = 1.2f;
        public float fastMultiplier = 3f;
        public float lookSensitivity = 3f;
        public bool showHelp = true;

        float yaw, pitch;

        void Start()
        {
            Vector3 e = transform.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180f ? e.x - 360f : e.x;
        }

        void Update()
        {
            if (RightMouseHeld())
            {
                Vector2 d = MouseDelta();
                yaw += d.x * lookSensitivity;
                pitch = Mathf.Clamp(pitch - d.y * lookSensitivity, -89f, 89f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }

            Vector3 m = Move();
            float speed = moveSpeed * (Fast() ? fastMultiplier : 1f);
            transform.position += (transform.right * m.x + transform.forward * m.z + Vector3.up * m.y) * speed * Time.deltaTime;
            transform.position += transform.forward * Scroll() * 0.25f;

            if (HelpToggled()) showHelp = !showHelp;
        }

        void OnGUI()
        {
            if (!showHelp) return;
            GUI.Box(new Rect(10, 10, 300, 112),
                "FoodTruck Test\n" +
                "Right mouse drag : look\n" +
                "WASD : move    Q / E : down / up\n" +
                "Shift : faster    Wheel : forward / back\n" +
                "H : hide this help");
        }

        // ---------------------------------------------------------------- input

        static Vector3 Move()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return Vector3.zero;
            float x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1f : 0f) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1f : 0f);
            float z = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1f : 0f) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1f : 0f);
            float y = (k.eKey.isPressed ? 1f : 0f) - (k.qKey.isPressed ? 1f : 0f);
            return new Vector3(x, y, z);
#else
            float y = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
            return new Vector3(Input.GetAxisRaw("Horizontal"), y, Input.GetAxisRaw("Vertical"));
#endif
        }

        static bool Fast()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
#else
            return Input.GetKey(KeyCode.LeftShift);
#endif
        }

        static bool HelpToggled()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.H);
#endif
        }

        static bool RightMouseHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
            return Input.GetMouseButton(1);
#endif
        }

        static Vector2 MouseDelta()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.delta.ReadValue() * 0.1f : Vector2.zero;
#else
            return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
        }

        static float Scroll()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null) return 0f;
            float s = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(s) > 10f) s /= 120f; // some platforms report 120 per notch
            return s;
#else
            return Input.mouseScrollDelta.y;
#endif
        }
    }
}
