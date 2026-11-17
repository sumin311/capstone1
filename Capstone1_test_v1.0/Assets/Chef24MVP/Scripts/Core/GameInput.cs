using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Chef24
{
    /// <summary>
    /// All keyboard / mouse reads go through here so key changes (SFR-009) only touch one file.
    /// Works with both the new Input System and the legacy Input Manager.
    /// </summary>
    public static class GameInput
    {
        public static Vector2 Move()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return Vector2.zero;
            float x = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
            float y = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
            return new Vector2(x, y);
#else
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
        }

        /// <summary>Mouse movement this frame, roughly in legacy "Mouse X/Y" units.</summary>
        public static Vector2 Look()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.delta.ReadValue() * 0.1f : Vector2.zero;
#else
            return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
        }

        /// <summary>E: interact (take order, cook, pick up, add topping, hand over).</summary>
        public static bool InteractDown() => KeyDown(KeyCode.E);

        /// <summary>R: secondary action (reject an order).</summary>
        public static bool AltDown() => KeyDown(KeyCode.R);

        /// <summary>Space or E: stop the cooking bar.</summary>
        public static bool CookActionDown() => KeyDown(KeyCode.Space) || KeyDown(KeyCode.E);

        public static bool PauseDown() => KeyDown(KeyCode.Escape);

        public static bool ConfirmDown() => KeyDown(KeyCode.Return) || KeyDown(KeyCode.KeypadEnter);

        static bool KeyDown(KeyCode code)
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return false;
            switch (code)
            {
                case KeyCode.E: return k.eKey.wasPressedThisFrame;
                case KeyCode.R: return k.rKey.wasPressedThisFrame;
                case KeyCode.Space: return k.spaceKey.wasPressedThisFrame;
                case KeyCode.Escape: return k.escapeKey.wasPressedThisFrame;
                case KeyCode.Return: return k.enterKey.wasPressedThisFrame;
                case KeyCode.KeypadEnter: return k.numpadEnterKey.wasPressedThisFrame;
                default: return false;
            }
#else
            return Input.GetKeyDown(code);
#endif
        }
    }
}
