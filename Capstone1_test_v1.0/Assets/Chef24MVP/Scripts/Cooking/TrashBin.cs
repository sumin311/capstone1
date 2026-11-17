using UnityEngine;

namespace Chef24
{
    /// <summary>Throws away the held dish (costs the waste fee).</summary>
    public class TrashBin : MonoBehaviour, IInteractable
    {
        public string GetPrompt(PlayerHands hands) =>
            hands.IsEmpty ? "쓰레기통" : "E: 들고 있는 음식 버리기 (-" + Names.Won(GameManager.Instance.wasteCost) + ")";

        public void Interact(PlayerHands hands)
        {
            if (hands.IsEmpty) return;
            hands.Release();
            GameManager.Instance.AddWaste("음식을 버렸어요");
        }

        public string GetAltPrompt(PlayerHands hands) => null;
        public void InteractAlt(PlayerHands hands) { }
    }
}
