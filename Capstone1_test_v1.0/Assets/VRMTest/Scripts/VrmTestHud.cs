using UnityEngine;

namespace CapstoneVrmTest
{
    /// <summary>On-screen help and status. Press H to hide/show.</summary>
    public class VrmTestHud : MonoBehaviour
    {
        public SimpleVrmController player;
        public VrmExpressionTester expressions;
        bool visible = true;

        void Update()
        {
            if (TestInputHelpToggle()) visible = !visible;
        }

        static bool TestInputHelpToggle()
        {
#if ENABLE_INPUT_SYSTEM
            return UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.hKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.H);
#endif
        }

        void OnGUI()
        {
            if (!visible) return;
            string text =
                "VRM TEST SCENE  (H: hide)\n" +
                "WASD / Arrows : move\n" +
                "Shift : run    Space : jump\n" +
                "Right mouse drag : orbit camera\n" +
                "Mouse wheel : zoom\n" +
                "1 happy  2 angry  3 sad  4 relaxed\n" +
                "5 surprised  6 blink  7 aa  0 reset\n";
            if (player != null) text += "\nSpeed: " + player.CurrentSpeed.ToString("0.0") + " m/s";
            if (expressions != null) text += "\nExpression: " + (expressions.Available ? expressions.Current : "unavailable");

            GUI.Box(new Rect(10, 10, 270, 190), GUIContent.none);
            GUI.Label(new Rect(20, 16, 260, 180), text);
        }
    }
}
