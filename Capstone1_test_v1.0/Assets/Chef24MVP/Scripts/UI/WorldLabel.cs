using UnityEngine;

namespace Chef24
{
    /// <summary>Keeps a floating name tag (TextMeshPro) turned towards the player's camera.</summary>
    public class WorldLabel : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 away = transform.position - cam.transform.position;
            if (away.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }
}
