using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Chef24
{
    /// <summary>
    /// Builds and drives every screen of the MVP in code (no prefabs needed):
    /// HUD (money / time / orders / prompt / held dish), customer speech bubbles, the cooking bar,
    /// and the start / pause / result panels (UI-006, UI-007 - simplified).
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        Font font;
        RectTransform root, bubbleLayer;
        Text moneyText, clockText, ordersText, promptText, heldText, toastText;
        GameObject cookPanel;
        Text cookTitle, cookResult;
        RectTransform cookZone, cookPerfect, cookMarker;
        GameObject startPanel, pausePanel, resultPanel;
        Text startTitle, startBody, resultTitle, resultBody;
        Action onStart;
        float toastUntil;

        class Bubble { public RectTransform rect; public Text text; public RectTransform fill; public Image fillImage; public GameObject bar; }
        readonly Dictionary<Customer, Bubble> bubbles = new Dictionary<Customer, Bubble>();

        static readonly Color PanelColor = new Color(0.08f, 0.07f, 0.1f, 0.82f);

        void Awake()
        {
            Instance = this;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 28);
            Build();
        }

        // ================================================================ public API

        public void SetMoney(int profit, int target, int waste)
        {
            string color = profit >= target ? "#7dff8a" : "#ffffff";
            moneyText.text = "<b>정산</b>  <color=" + color + ">" + Names.Won(profit) + "</color>  /  목표 " + Names.Won(target) +
                             (waste > 0 ? "\n<size=20><color=#ff8a8a>폐기 -" + Names.Won(waste) + "</color></size>" : "");
        }

        public void SetClock(string time, bool open)
        {
            clockText.text = "<b>" + time + "</b>\n<size=20>" + (open ? "영업 중" : "<color=#ffb070>영업 종료</color>") + "</size>";
        }

        public void SetOrders(List<Order> orders)
        {
            var sb = new StringBuilder("<b>주문 목록</b>");
            if (orders.Count == 0) sb.Append("\n<size=20><color=#bbbbbb>받은 주문 없음</color></size>");
            for (int i = 0; i < orders.Count; i++)
            {
                Order o = orders[i];
                if (o.customer == null) continue;
                var t = o.customer.type;
                sb.Append("\n").Append(i + 1).Append(". <color=#").Append(ColorUtility.ToHtmlStringRGB(t.color)).Append(">[")
                  .Append(t.name).Append("]</color> ").Append(o.menu.name).Append("  ").Append(o.ToppingsRichText())
                  .Append("  ").Append(PatienceBar(o.customer.Patience01));
            }
            ordersText.text = sb.ToString();
        }

        public void SetPrompt(string prompt)
        {
            promptText.text = prompt ?? "";
        }

        public void SetHeld(string held)
        {
            heldText.text = string.IsNullOrEmpty(held) ? "" : "들고 있는 것:  " + held;
        }

        public void Toast(string message, Color color)
        {
            toastText.text = message;
            toastText.color = color;
            toastUntil = Time.unscaledTime + 2.6f;
        }

        public void ShowCookBar(string title, float marker, float zoneCenter, float zoneWidth, CookGrade? result)
        {
            cookPanel.SetActive(true);
            cookTitle.text = title;
            SetSpan(cookZone, zoneCenter - zoneWidth * 0.5f, zoneCenter + zoneWidth * 0.5f);
            SetSpan(cookPerfect, zoneCenter - zoneWidth * 0.2f, zoneCenter + zoneWidth * 0.2f);
            cookMarker.anchorMin = new Vector2(Mathf.Clamp01(marker), 0f);
            cookMarker.anchorMax = new Vector2(Mathf.Clamp01(marker), 1f);
            if (result.HasValue)
            {
                cookResult.text = Names.Of(result.Value) + "!";
                cookResult.color = result.Value == CookGrade.Perfect ? new Color(1f, 0.9f, 0.3f)
                    : result.Value == CookGrade.Good ? new Color(0.5f, 1f, 0.55f) : new Color(1f, 0.5f, 0.45f);
            }
            else
            {
                cookResult.text = "Space / E : 멈추기";
                cookResult.color = new Color(0.85f, 0.85f, 0.85f);
            }
        }

        public void HideCookBar() => cookPanel.SetActive(false);

        public void SetBubble(Customer c, string text, Color color, float patience01)
        {
            if (!bubbles.TryGetValue(c, out Bubble b))
            {
                b = MakeBubble();
                bubbles[c] = b;
            }
            Camera cam = Camera.main;
            bool show = !string.IsNullOrEmpty(text) && cam != null;
            Vector3 sp = show ? cam.WorldToScreenPoint(c.transform.position + Vector3.up * 1.95f) : Vector3.zero;
            show &= sp.z > 0f;
            b.rect.gameObject.SetActive(show);
            if (!show) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(bubbleLayer, sp, null, out Vector2 local);
            b.rect.anchoredPosition = local;
            b.text.text = text;
            b.bar.SetActive(patience01 >= 0f);
            if (patience01 >= 0f)
            {
                b.fill.anchorMax = new Vector2(patience01, 1f);
                b.fillImage.color = Color.Lerp(new Color(1f, 0.3f, 0.25f), color, patience01 * 1.4f);
            }
        }

        public void RemoveBubble(Customer c)
        {
            if (bubbles.TryGetValue(c, out Bubble b))
            {
                if (b.rect != null) Destroy(b.rect.gameObject);
                bubbles.Remove(c);
            }
        }

        public void ShowStart(string stage, int target, Action start)
        {
            onStart = start;
            startTitle.text = "등록금은 장사로 갚는다";
            startBody.text =
                "<b>" + stage + " 1일차</b>   ·   오늘 목표: <color=#ffd25a>" + Names.Won(target) + "</color>\n\n" +
                "손님이 창구에 오면 <b>E</b>로 주문을 받아요 (<b>R</b>: 거절).\n" +
                "그릴에서 <b>E</b> → 조리 바가 움직이면 <b>Space</b>로 초록 구간에 맞춰 멈춰요.\n" +
                "다 익은 타코야끼를 담고, 주문대로 <b>소스 · 마요 · 파</b>를 넣어요. (빼 달라는 건 넣지 말기!)\n" +
                "손님을 보고 <b>E</b>로 건네면 판매 완료. 잘못 만든 건 쓰레기통에 버려요.\n\n" +
                "<size=22><color=#bbbbbb>WASD 이동 · 마우스 시점 · Esc 일시정지 · 오전 8시 ~ 오후 7시 영업</color></size>";
            startPanel.SetActive(true);
        }

        public void HideStart() => startPanel.SetActive(false);

        public void ShowPause(bool show) => pausePanel.SetActive(show);

        public void ShowResult(bool success, string stage, int sales, int waste, int profit, int target, int sold, int lost)
        {
            cookPanel.SetActive(false);
            resultPanel.SetActive(true);
            resultTitle.text = success ? "<color=#7dff8a>목표 달성!</color>" : "<color=#ff8a7a>목표 미달성</color>";
            resultBody.text =
                stage + " 1일차 영업 결과\n\n" +
                "판매 " + sold + "건   ·   놓친 손님 " + lost + "명\n\n" +
                "매출                " + Names.Won(sales) + "\n" +
                "음식 폐기 비용   -" + Names.Won(waste) + "\n" +
                "<b>정산                " + Names.Won(profit) + "</b>\n" +
                "목표                " + Names.Won(target) + "\n\n" +
                (success ? "다음 스테이지는 아직 준비 중이에요." : "부족한 금액: " + Names.Won(target - profit));
        }

        // ================================================================ per frame

        void Update()
        {
            if (Time.unscaledTime > toastUntil) toastText.text = "";
            if (startPanel.activeSelf && GameInput.ConfirmDown()) onStart?.Invoke();

            // Drop bubbles of customers that were destroyed.
            List<Customer> dead = null;
            foreach (var kv in bubbles) if (kv.Key == null) (dead ??= new List<Customer>()).Add(kv.Key);
            if (dead != null) foreach (var d in dead) { if (bubbles[d].rect != null) Destroy(bubbles[d].rect.gameObject); bubbles.Remove(d); }
        }

        // ================================================================ building

        void Build()
        {
            var canvasGo = new GameObject("HUD Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            root = (RectTransform)canvasGo.transform;

            EnsureEventSystem();

            bubbleLayer = Rect("Bubbles", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // HUD corners
            var moneyBox = Box("Money", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -24), new Vector2(520, 92), new Vector2(0, 1));
            moneyText = Label(moneyBox, 28, TextAnchor.MiddleLeft, Color.white, new Vector2(18, 8));
            var clockBox = Box("Clock", root, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -24), new Vector2(230, 92), new Vector2(1, 1));
            clockText = Label(clockBox, 34, TextAnchor.MiddleCenter, Color.white, new Vector2(8, 8));
            var ordersBox = Box("Orders", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -132), new Vector2(560, 300), new Vector2(0, 1));
            ordersText = Label(ordersBox, 24, TextAnchor.UpperLeft, Color.white, new Vector2(18, 14));

            // Crosshair, prompt, held item, toast
            var cross = Rect("Crosshair", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8, 8));
            cross.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.85f);
            promptText = FreeText("Prompt", root, new Vector2(0.5f, 0.5f), new Vector2(0, -70), new Vector2(1200, 60), 30, TextAnchor.MiddleCenter, new Color(1f, 0.95f, 0.75f));
            heldText = FreeText("Held", root, new Vector2(0.5f, 0f), new Vector2(0, 60), new Vector2(1200, 50), 28, TextAnchor.MiddleCenter, Color.white);
            toastText = FreeText("Toast", root, new Vector2(0.5f, 1f), new Vector2(0, -150), new Vector2(1400, 60), 34, TextAnchor.MiddleCenter, Color.white);
            FreeText("Hint", root, new Vector2(0f, 0f), new Vector2(330, 26), new Vector2(640, 40), 20, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.6f))
                .text = "WASD 이동 · 마우스 시점 · E 상호작용 · R 거절 · Esc 일시정지";

            BuildCookBar();
            BuildPanels();

            SetPrompt(null);
            SetHeld(null);
            toastText.text = "";
        }

        void BuildCookBar()
        {
            var panel = Box("Cook Bar", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 150), new Vector2(760, 170), new Vector2(0.5f, 0f));
            cookPanel = panel.gameObject;
            cookTitle = FreeText("Title", panel, new Vector2(0.5f, 1f), new Vector2(0, -28), new Vector2(720, 40), 28, TextAnchor.MiddleCenter, Color.white);
            var bar = Rect("Bar", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(660, 34));
            bar.gameObject.AddComponent<Image>().color = new Color(0.25f, 0.25f, 0.28f);
            cookZone = Rect("Zone", bar, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            cookZone.gameObject.AddComponent<Image>().color = new Color(0.3f, 0.75f, 0.35f);
            cookPerfect = Rect("Perfect", bar, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            cookPerfect.gameObject.AddComponent<Image>().color = new Color(1f, 0.85f, 0.25f);
            cookMarker = Rect("Marker", bar, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(8, 14));
            cookMarker.gameObject.AddComponent<Image>().color = Color.white;
            cookResult = FreeText("Result", panel, new Vector2(0.5f, 0f), new Vector2(0, 30), new Vector2(720, 40), 28, TextAnchor.MiddleCenter, Color.white);
            cookPanel.SetActive(false);
        }

        void BuildPanels()
        {
            // Start
            var sp = Box("Start Panel", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100, 620), new Vector2(0.5f, 0.5f));
            sp.GetComponent<Image>().color = new Color(0.08f, 0.07f, 0.1f, 0.93f);
            startTitle = FreeText("Title", sp, new Vector2(0.5f, 1f), new Vector2(0, -60), new Vector2(1000, 70), 52, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.45f));
            startBody = FreeText("Body", sp, new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(980, 380), 27, TextAnchor.MiddleCenter, Color.white);
            Button(sp, "장사 시작  (Enter)", new Vector2(0.5f, 0f), new Vector2(0, 60), new Vector2(360, 70), () => onStart?.Invoke());
            startPanel = sp.gameObject;
            startPanel.SetActive(false);

            // Pause
            var pp = Box("Pause Panel", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(460, 400), new Vector2(0.5f, 0.5f));
            FreeText("Title", pp, new Vector2(0.5f, 1f), new Vector2(0, -55), new Vector2(400, 60), 44, TextAnchor.MiddleCenter, Color.white).text = "일시정지";
            Button(pp, "계속하기", new Vector2(0.5f, 1f), new Vector2(0, -150), new Vector2(300, 62), () => GameManager.Instance.Resume());
            Button(pp, "처음부터 다시", new Vector2(0.5f, 1f), new Vector2(0, -230), new Vector2(300, 62), () => GameManager.Instance.Retry());
            Button(pp, "나가기", new Vector2(0.5f, 1f), new Vector2(0, -310), new Vector2(300, 62), () => GameManager.Instance.Quit());
            pausePanel = pp.gameObject;
            pausePanel.SetActive(false);

            // Result
            var rp = Box("Result Panel", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 640), new Vector2(0.5f, 0.5f));
            rp.GetComponent<Image>().color = new Color(0.08f, 0.07f, 0.1f, 0.94f);
            resultTitle = FreeText("Title", rp, new Vector2(0.5f, 1f), new Vector2(0, -60), new Vector2(700, 70), 52, TextAnchor.MiddleCenter, Color.white);
            resultBody = FreeText("Body", rp, new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(640, 400), 28, TextAnchor.MiddleCenter, Color.white);
            Button(rp, "재도전", new Vector2(0.5f, 0f), new Vector2(-150, 60), new Vector2(260, 66), () => GameManager.Instance.Retry());
            Button(rp, "나가기", new Vector2(0.5f, 0f), new Vector2(150, 60), new Vector2(260, 66), () => GameManager.Instance.Quit());
            resultPanel = rp.gameObject;
            resultPanel.SetActive(false);
        }

        Bubble MakeBubble()
        {
            var b = new Bubble();
            b.rect = Box("Bubble", bubbleLayer, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(330, 120), new Vector2(0.5f, 0f));
            b.rect.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.92f);
            b.text = Label(b.rect, 22, TextAnchor.UpperCenter, new Color(0.12f, 0.1f, 0.12f), new Vector2(10, 22));
            b.text.GetComponent<Outline>().enabled = false;
            var bar = Rect("Patience", b.rect, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 8), new Vector2(-24, 10));
            bar.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.15f);
            b.bar = bar.gameObject;
            b.fill = Rect("Fill", bar, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            b.fillImage = b.fill.gameObject.AddComponent<Image>();
            return b;
        }

        // ---------------------------------------------------------------- helpers

        static string PatienceBar(float p)
        {
            int n = Mathf.CeilToInt(Mathf.Clamp01(p) * 5f);
            string color = p > 0.5f ? "#9dff8a" : p > 0.25f ? "#ffd25a" : "#ff6a5a";
            return "<color=" + color + ">" + new string('■', n) + "</color><color=#666666>" + new string('■', 5 - n) + "</color>";
        }

        static void SetSpan(RectTransform r, float from, float to)
        {
            r.anchorMin = new Vector2(Mathf.Clamp01(from), 0f);
            r.anchorMax = new Vector2(Mathf.Clamp01(to), 1f);
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = anchorMin;
            r.anchorMax = anchorMax;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            return r;
        }

        RectTransform Box(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, Vector2 pivot)
        {
            var r = Rect(name, parent, anchorMin, anchorMax, pos, size);
            r.pivot = pivot;
            r.anchoredPosition = pos;
            r.gameObject.AddComponent<Image>().color = PanelColor;
            return r;
        }

        Text Label(RectTransform box, int size, TextAnchor anchor, Color color, Vector2 padding)
        {
            var r = Rect("Text", box, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            r.offsetMin = padding;
            r.offsetMax = -padding;
            return SetupText(r, size, anchor, color);
        }

        Text FreeText(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize, TextAnchor align, Color color)
        {
            var r = Rect(name, parent, anchor, anchor, pos, size);
            return SetupText(r, fontSize, align, color);
        }

        Text SetupText(RectTransform r, int size, TextAnchor anchor, Color color)
        {
            var t = r.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var o = r.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0, 0, 0, 0.6f);
            o.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        void Button(RectTransform parent, string label, Vector2 anchor, Vector2 pos, Vector2 size, Action onClick)
        {
            var r = Rect(label, parent, anchor, anchor, pos, size);
            var img = r.gameObject.AddComponent<Image>();
            img.color = new Color(0.95f, 0.55f, 0.2f);
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.highlightedColor = new Color(1f, 0.85f, 0.7f);
            colors.pressedColor = new Color(0.8f, 0.6f, 0.5f);
            b.colors = colors;
            b.onClick.AddListener(() => onClick());
            var t = Label(r, 28, TextAnchor.MiddleCenter, Color.white, Vector2.zero);
            t.text = label;
        }

        static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }
    }
}
