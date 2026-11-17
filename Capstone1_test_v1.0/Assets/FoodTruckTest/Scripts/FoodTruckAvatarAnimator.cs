using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CapstoneFoodTruckTest
{
    /// <summary>
    /// Plays the extra ("action") animations that Tools > FoodTruck Test > Rebuild Avatar Animations
    /// put into the avatar's Animator Controller.
    ///
    /// From other scripts:  GetComponent&lt;FoodTruckAvatarAnimator&gt;().Play("Cooking");  /  .Stop();
    /// In Play mode: click a button in the "Animations" panel (M: show / hide), or F1-F12 for the first 12.
    ///
    /// Idle / walk / run are not listed here: they are a blend tree driven by the "Speed" parameter,
    /// which SimpleVrmController (VRMTest) sets while you move.
    /// </summary>
    public class FoodTruckAvatarAnimator : MonoBehaviour
    {
        public const string LocomotionState = "Locomotion";
        public const string UpperBodyLayer = "UpperBody";
        public const string UpperBodyEmptyState = "Empty";

        [Serializable]
        public class ActionInfo
        {
            public string name;
            [Tooltip("Keeps playing until Stop() (file name ends with _loop).")]
            public bool loop;
            [Tooltip("Only moves the upper body, so it can play while walking (file name ends with _upper).")]
            public bool upperBody;
        }

        [Tooltip("Filled in by Rebuild Avatar Animations. Must match the states in the Animator Controller.")]
        public List<ActionInfo> actions = new List<ActionInfo>();
        public float crossFade = 0.15f;
        [Tooltip("A looping full-body action stops as soon as the character starts moving.")]
        public bool stopLoopWhenMoving = true;
        public bool showMenu = true;

        public string CurrentAction => current != null ? current.name : null;
        public event Action<string> ActionStarted;
        public event Action<string> ActionFinished;

        Animator animator;
        CharacterController body;
        ActionInfo current;
        float startedAt;
        Vector2 scroll;

        void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            body = GetComponent<CharacterController>();
        }

        /// <summary>Plays an action by name. Returns false if there is no such action.</summary>
        public bool Play(string actionName)
        {
            ActionInfo a = actions.Find(x => x.name == actionName);
            if (a == null || animator == null || animator.runtimeAnimatorController == null)
            {
                Debug.LogWarning("[FoodTruckTest] Unknown animation '" + actionName + "'. Run Tools > FoodTruck Test > Rebuild Avatar Animations.");
                return false;
            }
            int layer = LayerOf(a);
            if (layer < 0) return false;

            if (current != null && current != a) Finish();
            animator.CrossFadeInFixedTime(a.name, crossFade, layer, 0f);
            current = a;
            startedAt = Time.time;
            ActionStarted?.Invoke(a.name);
            return true;
        }

        /// <summary>Stops the current action and goes back to idle / walk / run.</summary>
        public void Stop()
        {
            if (current == null || animator == null) return;
            int layer = LayerOf(current);
            if (layer >= 0)
                animator.CrossFadeInFixedTime(current.upperBody ? UpperBodyEmptyState : LocomotionState, crossFade, layer);
            Finish();
        }

        void Update()
        {
            if (MenuTogglePressed()) showMenu = !showMenu;
            for (int i = 0; i < actions.Count && i < 12; i++)
                if (FunctionKeyDown(i + 1)) Play(actions[i].name);

            if (current == null || animator == null) return;
            int layer = LayerOf(current);

            // A one-shot action has gone back to its default state by itself.
            if (!current.loop && Time.time > startedAt + crossFade + 0.05f && layer >= 0 &&
                !animator.IsInTransition(layer) && !animator.GetCurrentAnimatorStateInfo(layer).IsName(current.name))
            {
                Finish();
                return;
            }

            if (current.loop && !current.upperBody && stopLoopWhenMoving && body != null)
            {
                Vector3 v = body.velocity;
                v.y = 0f;
                if (v.magnitude > 0.2f) Stop();
            }
        }

        void Finish()
        {
            string n = current != null ? current.name : null;
            current = null;
            if (n != null) ActionFinished?.Invoke(n);
        }

        int LayerOf(ActionInfo a)
        {
            if (!a.upperBody) return 0;
            int layer = animator.GetLayerIndex(UpperBodyLayer);
            if (layer < 0) Debug.LogWarning("[FoodTruckTest] The Animator has no UpperBody layer. Rebuild Avatar Animations.");
            return layer;
        }

        void OnGUI()
        {
            if (!showMenu) return;
            float w = 230f;
            float h = Mathf.Min(Screen.height - 20f, 70f + Mathf.Max(1, actions.Count) * 24f);
            GUILayout.BeginArea(new Rect(Screen.width - w - 10f, 10f, w, h), GUI.skin.box);
            GUILayout.Label("Animations   (M: hide)");
            if (actions.Count == 0)
            {
                GUILayout.Label("None yet. Put FBX files in\nFoodTruckTest/Animations/Actions\nand run Rebuild Avatar Animations.");
            }
            else
            {
                scroll = GUILayout.BeginScrollView(scroll);
                for (int i = 0; i < actions.Count; i++)
                {
                    ActionInfo a = actions[i];
                    string label = (i < 12 ? "F" + (i + 1) + "  " : "") + a.name +
                                   (a.loop ? "  (loop)" : "") + (a.upperBody ? "  [upper]" : "");
                    GUI.color = a == current ? new Color(1f, 0.85f, 0.4f) : Color.white;
                    if (GUILayout.Button(label)) Play(a.name);
                }
                GUI.color = Color.white;
                GUILayout.EndScrollView();
                if (GUILayout.Button("Stop")) Stop();
            }
            GUILayout.EndArea();
        }

        // ---------------------------------------------------------------- input

        static bool MenuTogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.M);
#endif
        }

        static bool FunctionKeyDown(int n)
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            return k != null && k[Key.F1 + (n - 1)].wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.F1 + (n - 1));
#endif
        }
    }
}
