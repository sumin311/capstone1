using UnityEngine;

namespace Chef24
{
    /// <summary>
    /// Looks through the camera centre, finds the IInteractable under the crosshair, shows its prompt
    /// and forwards E / R presses to it (AP-002 Interaction Service).
    /// </summary>
    public class Interactor : MonoBehaviour
    {
        public float range = 3.2f;
        [Tooltip("Aim assist: how far (m) from the exact crosshair line an object still counts as aimed at.")]
        public float aimRadius = 0.25f;
        public PlayerHands hands;
        public PlayerController player;

        public IInteractable Current { get; private set; }

        void Update()
        {
            Current = null;
            var gm = GameManager.Instance;
            if (gm == null || gm.State != GameState.Playing || (player != null && player.Locked))
            {
                UIManager.Instance?.SetPrompt(null);
                return;
            }

            Current = FindTarget();

            string prompt = null;
            if (Current != null)
            {
                prompt = Current.GetPrompt(hands);
                string alt = Current.GetAltPrompt(hands);
                if (!string.IsNullOrEmpty(alt)) prompt = string.IsNullOrEmpty(prompt) ? alt : prompt + "     " + alt;
            }
            UIManager.Instance?.SetPrompt(prompt);

            if (Current == null) return;
            if (GameInput.InteractDown()) Current.Interact(hands);
            else if (GameInput.AltDown()) Current.InteractAlt(hands);
        }

        /// <summary>
        /// Exact ray first. If that does not land on something usable, a thick sphere cast picks the
        /// interactable closest to the crosshair line, so near misses still count.
        /// </summary>
        IInteractable FindTarget()
        {
            Vector3 origin = transform.position, dir = transform.forward;
            if (Physics.Raycast(origin, dir, out RaycastHit hit, range, ~0, QueryTriggerInteraction.Collide))
            {
                var direct = hit.collider.GetComponentInParent<IInteractable>();
                if (direct != null) return direct;
            }

            IInteractable best = null;
            float bestOff = float.MaxValue;
            foreach (RaycastHit h in Physics.SphereCastAll(origin, aimRadius, dir, range, ~0, QueryTriggerInteraction.Collide))
            {
                var it = h.collider.GetComponentInParent<IInteractable>();
                if (it == null) continue;
                // Distance of the collider centre from the crosshair line.
                Vector3 c = h.collider.bounds.center - origin;
                float off = Vector3.Cross(dir, c).magnitude;
                if (Vector3.Dot(c, dir) > 0f && off < bestOff) { bestOff = off; best = it; }
            }
            return best;
        }
    }
}
