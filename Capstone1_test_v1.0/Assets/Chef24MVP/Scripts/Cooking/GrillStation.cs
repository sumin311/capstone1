using UnityEngine;

namespace Chef24
{
    /// <summary>
    /// Takoyaki grill. Empty -> (E) cooking bar: pour batter, then grill &amp; flip -> Ready.
    /// Pick it up with E before it burns (SFR-006 조리, 개발계획서: 태우기).
    /// </summary>
    public class GrillStation : MonoBehaviour, IInteractable
    {
        enum State { Empty, Cooking, Ready, Burnt }

        public GameObject emptyVisual;
        public GameObject cookingVisual;
        public TimingMiniGame miniGame;
        [Tooltip("Seconds a finished batch can wait on the grill before it burns.")]
        public float burnAfter = 25f;
        public float warnAfter = 15f;

        public TimingMiniGame.Step[] steps =
        {
            new TimingMiniGame.Step("반죽 붓기", 0.32f, 0.9f),
            new TimingMiniGame.Step("굽고 뒤집기", 0.28f, 1.05f),
        };

        State state = State.Empty;
        CookGrade readyGrade;
        float readySince;
        bool warned;

        GameManager GM => GameManager.Instance;

        void Start() => UpdateVisual();

        public string GetPrompt(PlayerHands hands)
        {
            switch (state)
            {
                case State.Empty:
                    return hands.IsEmpty ? "E: 타코야끼 굽기" : "손이 비어 있어야 해요";
                case State.Cooking:
                    return null;
                case State.Ready:
                    float left = burnAfter - (Time.time - readySince);
                    string warn = left < burnAfter - warnAfter ? "  <color=#ff7a50>(타기 직전! " + Mathf.CeilToInt(left) + "초)</color>" : "";
                    return (hands.IsEmpty ? "E: 다 익은 타코야끼 담기 (" + Names.Of(readyGrade) + ")" : "손이 비어 있어야 해요") + warn;
                default:
                    return "E: 탄 타코야끼 버리기";
            }
        }

        public string GetAltPrompt(PlayerHands hands) => null;
        public void InteractAlt(PlayerHands hands) { }

        public void Interact(PlayerHands hands)
        {
            switch (state)
            {
                case State.Empty:
                    if (!hands.IsEmpty || miniGame == null) return;
                    state = State.Cooking;
                    GM.player.SetLocked(true);
                    miniGame.Run(steps, grades =>
                    {
                        readyGrade = TimingMiniGame.Combine(grades);
                        readySince = Time.time;
                        warned = false;
                        state = State.Ready;
                        UpdateVisual();
                        if (GM.State == GameState.Playing) GM.player.SetLocked(false);
                        UIManager.Instance?.Toast("타코야끼 완성! (" + Names.Of(readyGrade) + ")", Color.white);
                    });
                    UpdateVisual();
                    break;

                case State.Ready:
                    if (!hands.IsEmpty) return;
                    hands.Take(new Dish { menu = GM.menu, grade = readyGrade });
                    state = State.Empty;
                    UpdateVisual();
                    break;

                case State.Burnt:
                    GM.AddWaste("탄 타코야끼를 버렸어요");
                    state = State.Empty;
                    UpdateVisual();
                    break;
            }
        }

        void Update()
        {
            if (state != State.Ready) return;
            float age = Time.time - readySince;
            if (!warned && age > warnAfter)
            {
                warned = true;
                UIManager.Instance?.Toast("그릴 위 타코야끼가 타려고 해요!", new Color(1f, 0.6f, 0.3f));
            }
            if (age > burnAfter)
            {
                state = State.Burnt;
                UpdateVisual();
                UIManager.Instance?.Toast("타코야끼가 타 버렸어요...", new Color(1f, 0.45f, 0.45f));
            }
        }

        void UpdateVisual()
        {
            bool showBalls = state == State.Ready || state == State.Burnt || state == State.Cooking;
            if (emptyVisual != null) emptyVisual.SetActive(!showBalls);
            if (cookingVisual != null)
            {
                cookingVisual.SetActive(showBalls);
                // Darken the balls when burnt.
                foreach (var r in cookingVisual.GetComponentsInChildren<Renderer>())
                {
                    if (r.transform == cookingVisual.transform) continue;
                    var block = new MaterialPropertyBlock();
                    r.GetPropertyBlock(block);
                    Color c = state == State.Burnt ? new Color(0.25f, 0.18f, 0.12f) : Color.white;
                    block.SetColor("_Color", c);
                    block.SetColor("_BaseColor", c);
                    r.SetPropertyBlock(block);
                }
            }
        }
    }
}
