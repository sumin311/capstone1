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

        ParticleSystem steam, smoke;

        void Start()
        {
            // Effects sit just above the grill plate: steam while cooking / ready, smoke when it is about to burn or burnt.
            var rs = GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                Bounds b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                var top = new Vector3(b.center.x, b.max.y, b.center.z);
                var area = new Vector2(b.size.x * 0.6f, b.size.z * 0.5f);
                steam = Vfx.Column("Steam", transform, top, area);
                smoke = Vfx.Column("Smoke", transform, top, area, smoke: true);
            }
            UpdateVisual();
        }

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
                        UIManager.Instance?.Toast("타코야끼 완성! (" + Names.Of(readyGrade) + ")", Palette.Mint);
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
            UpdateEffects();
            if (state != State.Ready) return;
            float age = Time.time - readySince;
            if (!warned && age > warnAfter)
            {
                warned = true;
                UIManager.Instance?.Toast("그릴 위 타코야끼가 타려고 해요!", Palette.Amber);
            }
            if (age > burnAfter)
            {
                state = State.Burnt;
                UpdateVisual();
                UIManager.Instance?.Toast("타코야끼가 타 버렸어요...", Palette.Tomato);
            }
        }

        void UpdateEffects()
        {
            bool almostBurnt = state == State.Ready && Time.time - readySince > warnAfter;
            Vfx.SetColumn(steam, state == State.Cooking || (state == State.Ready && !almostBurnt), state == State.Cooking ? 3f : 6f, Vfx.SteamColor);
            Vfx.SetColumn(smoke, almostBurnt || state == State.Burnt, state == State.Burnt ? 10f : 6f,
                          state == State.Burnt ? Vfx.BurntSmokeColor : Vfx.WarnSmokeColor);
        }

        void UpdateVisual()
        {
            bool showBalls = state == State.Ready || state == State.Burnt || state == State.Cooking;
            if (emptyVisual != null) emptyVisual.SetActive(!showBalls);
            if (cookingVisual != null)
            {
                cookingVisual.SetActive(showBalls);
                // Darken only the takoyaki (not the grill body) when burnt; otherwise use the material's own colour.
                foreach (var r in cookingVisual.GetComponentsInChildren<Renderer>())
                {
                    var m = r.sharedMaterial;
                    if (m == null || m.name.StartsWith("Grill")) continue;
                    if (state != State.Burnt)
                    {
                        r.SetPropertyBlock(null);
                        continue;
                    }
                    Color baseColor = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
                    Color c = baseColor * new Color(0.3f, 0.22f, 0.16f, 1f);
                    var block = new MaterialPropertyBlock();
                    r.GetPropertyBlock(block);
                    block.SetColor("_Color", c);
                    block.SetColor("_BaseColor", c);
                    r.SetPropertyBlock(block);
                }
            }
        }
    }
}
