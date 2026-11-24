using UnityEngine;

namespace Chef24
{
    /// <summary>The one dish the player carries, shown in front of the camera.</summary>
    public class PlayerHands : MonoBehaviour
    {
        [Tooltip("Model shown while holding takoyaki (Takoyaki_Boat).")]
        public GameObject heldPrefab;
        [Tooltip("Swaps the held model's materials for their toon versions.")]
        public ToonMaterialMap toonMap;
        public Vector3 heldLocalPosition = new Vector3(0.24f, -0.3f, 0.6f);
        public float heldScale = 2.2f;

        public Dish Held { get; private set; }
        public bool IsEmpty => Held == null;

        GameObject visual;

        public void Take(Dish dish)
        {
            Held = dish;
            if (visual == null && heldPrefab != null)
            {
                visual = Instantiate(heldPrefab, transform);
                visual.transform.localPosition = heldLocalPosition;
                visual.transform.localRotation = Quaternion.Euler(0f, 160f, 0f);
                visual.transform.localScale = Vector3.one * heldScale;
                if (toonMap != null) toonMap.Apply(visual);
                foreach (var c in visual.GetComponentsInChildren<Collider>()) Destroy(c);
            }
            if (visual != null) visual.SetActive(true);
            UIManager.Instance?.SetHeld(dish.Describe());
        }

        /// <summary>Call after changing the held dish (e.g. a topping was added).</summary>
        public void Refresh()
        {
            UIManager.Instance?.SetHeld(Held != null ? Held.Describe() : null);
        }

        public Dish Release()
        {
            Dish d = Held;
            Held = null;
            if (visual != null) visual.SetActive(false);
            UIManager.Instance?.SetHeld(null);
            return d;
        }
    }
}
