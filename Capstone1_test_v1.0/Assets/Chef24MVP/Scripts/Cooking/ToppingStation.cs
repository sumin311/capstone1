using UnityEngine;

namespace Chef24
{
    /// <summary>Sauce bottle / mayo bottle / green onion bowl: adds one topping to the held takoyaki.</summary>
    public class ToppingStation : MonoBehaviour, IInteractable
    {
        public Topping topping;

        public string GetPrompt(PlayerHands hands)
        {
            string n = Names.Full(topping);
            if (hands.IsEmpty) return n + " (타코야끼를 들고 오세요)";
            if (hands.Held.toppings.Contains(topping)) return n + ": 이미 넣었어요";
            return "E: " + n + " 넣기";
        }

        public void Interact(PlayerHands hands)
        {
            if (hands.IsEmpty || hands.Held.toppings.Contains(topping)) return;
            hands.Held.toppings.Add(topping);
            hands.Refresh();
        }

        public string GetAltPrompt(PlayerHands hands) => null;
        public void InteractAlt(PlayerHands hands) { }
    }
}
