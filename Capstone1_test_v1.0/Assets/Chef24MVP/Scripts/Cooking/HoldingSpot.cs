using UnityEngine;

namespace Chef24
{
    /// <summary>
    /// A plate spot on the counter. E puts the held dish down, picks it back up, or swaps it with the held one,
    /// so a dish made for the wrong order can wait for the right customer instead of being thrown away.
    /// </summary>
    public class HoldingSpot : MonoBehaviour, IInteractable
    {
        [Tooltip("Model shown while a dish sits here (Takoyaki_Boat).")]
        public GameObject dishPrefab;
        public float dishScale = 1.6f;

        public Dish Stored { get; private set; }
        public bool IsEmpty => Stored == null;

        GameObject visual;

        public string GetPrompt(PlayerHands hands)
        {
            if (IsEmpty) return hands.IsEmpty ? "접시 자리 (음식을 잠깐 내려놓을 수 있어요)" : "E: 여기 내려놓기";
            if (hands.IsEmpty) return "E: 집기  -  " + Stored.Describe();
            return "E: 들고 있는 것과 바꾸기  -  " + Stored.Describe();
        }

        public void Interact(PlayerHands hands)
        {
            if (IsEmpty && hands.IsEmpty) return;
            Dish fromHands = hands.IsEmpty ? null : hands.Release();
            Dish fromSpot = Stored;
            Stored = fromHands;
            if (fromSpot != null) hands.Take(fromSpot);
            RefreshVisual();
        }

        public string GetAltPrompt(PlayerHands hands) => null;
        public void InteractAlt(PlayerHands hands) { }

        void RefreshVisual()
        {
            if (visual == null && dishPrefab != null)
            {
                visual = Instantiate(dishPrefab, transform);
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one * dishScale;
                visual.transform.localPosition = Vector3.zero;
                foreach (var c in visual.GetComponentsInChildren<Collider>()) Destroy(c);
                // Sit the model on the counter top: this transform's position is the counter surface.
                var rs = visual.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    Bounds b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    visual.transform.position += new Vector3(transform.position.x - b.center.x, transform.position.y - b.min.y, transform.position.z - b.center.z);
                }
            }
            if (visual != null) visual.SetActive(!IsEmpty);
        }
    }
}
