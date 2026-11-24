using UnityEngine;

namespace Chef24
{
    /// <summary>Anything the player can use with E (and optionally R) while looking at it.</summary>
    public interface IInteractable
    {
        /// <summary>Text shown under the crosshair, e.g. "E: 주문 받기". Null hides the prompt.</summary>
        string GetPrompt(PlayerHands hands);
        void Interact(PlayerHands hands);
        /// <summary>Optional R action text. Null if there is none.</summary>
        string GetAltPrompt(PlayerHands hands);
        void InteractAlt(PlayerHands hands);
    }
}
