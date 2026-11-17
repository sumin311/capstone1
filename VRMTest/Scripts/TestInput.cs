using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CapstoneVrmTest
{
    /// <summary>
    /// Works with both the new Input System and the legacy Input Manager.
    /// </summary>
    public static class TestInput
    {
        public static Vector2 Move()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return Vector2.zero;
            float x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1f : 0f) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1f : 0f);
            float y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1f : 0f) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1f : 0f);
            return new Vector2(x, y);
#else
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
        }

        public static bool Run()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
#else
            return Input.GetKey(KeyCode.LeftShift);
#endif
        }

        public static bool JumpDown()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Space);
#endif
        }

        /// <summary>Number keys 0-9 on the top row.</summary>
        public static bool DigitDown(int n)
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return false;
            Key key = n == 0 ? Key.Digit0 : Key.Digit1 + (n - 1);
            return k[key].wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Alpha0 + n);
#endif
        }

        public static bool RightMouseHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
            return Input.GetMouseButton(1);
#endif
        }

        /// <summary>Mouse movement this frame, roughly in legacy "Mouse X/Y" units.</summary>
        public static Vector2 MouseDelta()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.delta.ReadValue() * 0.1f : Vector2.zero;
#else
            return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
        }

        /// <summary>Scroll wheel notches this frame (+ = up).</summary>
        public static float Scroll()
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
