using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Chef24
{
    /// <summary>
    /// Builds and drives every screen of the MVP in code (no prefabs needed):
    /// HUD (money / clock / order tickets / prompt / held dish / toasts), customer speech bubbles, the cooking bar,
    /// and the title / pause / result screens (UI-006, UI-007 - simplified).
    /// All colours come from <see cref="Palette"/>; all motion runs on unscaled time so it survives pause.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("Feel")]
        public float ticketSlideSpeed = 12f;
        public float moneyCountSpeed = 6f;
        public float toastSeconds = 2.4f;

        Font font;
        Sprite round, pill;
        RectTransform root, hud, bubbleLayer, fxLayer;
        CanvasGroup hudGroup;

        // HUD
        RectTransform moneyBox, moneyFill;
        Image moneyFillImage;
        Text moneyValue, moneyTarget, wasteText;
        Text clockTime, clockState;
        GameObject lunchTag;
        RectTransform dayFill;
        RectTransform ticketColumn;
        Text ordersHeader, ordersEmpty;
        RectTransform crosshair;
        Image crosshairImage;
        RectTransform promptBox;
        Text promptText;
        RectTransform heldBox;
        Text heldText;
        RectTransform toastBox;
        CanvasGroup toastGroup;
        Image toastStrip;
        Text toastText;
        Coroutine toastRoutine;

        // Cook bar
        GameObject cookPanel;
        Text cookTitle, cookResult;
        RectTransform cookZone, cookPerfect, cookMarker;
        bool cookHadResult;

        // Screens
        CanvasGroup startScreen, pauseScreen, resultScreen;
        Text startStage, startPrompt;
        Text resultTitle, resultSub, resultRows, resultValues, resultProfit, resultNote;
        RectTransform resultBanner;
        Action onStart;

        // State
        float shownProfit;
        int lastProfit;
        bool moneyStarted;
        string lastHeld;
        Interactor interactor;

        class Ticket
        {
            public Order order;
            public RectTransform rect;
            public CanvasGroup group;
            public Image frame, header, patienceFill;
            public Text who, title, status;
            public Image[] chipBg;
            public Text[] chipText;
            public float y;
            public bool removing;
        }
        readonly List<Ticket> tickets = new List<Ticket>();

        class Bubble
        {
            public RectTransform rect;
            public Image strip, fill;
            public Text text;
            public GameObject bar;
        }
        readonly Dictionary<Customer, Bubble> bubbles = new Dictionary<Customer, Bubble>();

        const float TicketWidth = 420f, TicketHeight = 158f, TicketGap = 12f;

        void Awake()
        {
            Instance = this;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Segoe UI Symbol", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 28);
            round = MakeRoundedSprite(64, 18);
            pill = MakeRoundedSprite(64, 30);
            Build();
        }

        // ================================================================ public API

        public void SetMoney(int profit, int target, int waste)
        {
            if (!moneyStarted)
            {
                moneyStarted = true;
                shownProfit = lastProfit = profit;
            }
            if (profit != lastProfit)
            {
                int diff = profit - lastProfit;
                FloatText((diff > 0 ? "+" : "-") + Names.Won(Mathf.Abs(diff)), diff > 0 ? Palette.Amber : Palette.Tomato,
                          moneyBox, new Vector2(0f, -185f));
                StartCoroutine(Pop(moneyBox, 1.06f));
                lastProfit = profit;
            }

            shownProfit = Mathf.Lerp(shownProfit, profit, 1f - Mathf.Exp(-moneyCountSpeed * Time.unscaledDeltaTime));
            if (Mathf.Abs(shownProfit - profit) < 50f) shownProfit = profit;
            moneyValue.text = Names.Won(Mathf.RoundToInt(shownProfit / 100f) * 100);
            moneyValue.color = profit >= target ? Palette.Leaf : Palette.Amber;
            moneyTarget.text = "목표 " + Names.Won(target) + "   ·   " + Mathf.Clamp(Mathf.RoundToInt(100f * profit / Mathf.Max(1, target)), 0, 999) + "%";
            wasteText.text = waste > 0 ? "폐기 -" + Names.Won(waste) : "";

            float fill = Mathf.Clamp01(shownProfit / Mathf.Max(1, target));
            moneyFill.anchorMax = new Vector2(fill, 1f);
            moneyFillImage.color = profit >= target ? Palette.Leaf : Palette.Amber;
        }

        public void SetClock(string time, bool open)
        {
            clockTime.text = time;
            var clock = GameManager.Instance != null ? GameManager.Instance.clock : null;
            bool lunch = clock != null && clock.IsLunchTime && open;
            clockState.text = open ? "영업 중" : "영업 종료";
            clockState.color = open ? Palette.Leaf : Palette.Tomato;
            if (lunchTag.activeSelf != lunch) lunchTag.SetActive(lunch);
            if (clock != null) dayFill.anchorMax = new Vector2(clock.DayProgress, 1f);
        }

        public void SetOrders(List<Order> orders)
        {
            if (interactor == null) interactor = FindObjectOfType<Interactor>();
            Dish held = GameManager.Instance != null && GameManager.Instance.hands != null ? GameManager.Instance.hands.Held : null;
            object looking = interactor != null ? interactor.Current : null;

            // Add tickets for new orders, retire tickets whose order is gone.
            foreach (var o in orders)
                if (o.customer != null && tickets.All(t => t.order != o)) tickets.Add(MakeTicket(o, tickets.Count(t => !t.removing)));
            foreach (var t in tickets)
                if (!t.removing && (!orders.Contains(t.order) || t.order.customer == null)) StartCoroutine(RetireTicket(t));

            int live = 0;
            float dt = Time.unscaledDeltaTime;
            foreach (var t in tickets)
            {
                if (t.removing) continue;
                float targetY = -live * (TicketHeight + TicketGap);
                t.y = Mathf.Lerp(t.y, targetY, 1f - Mathf.Exp(-ticketSlideSpeed * dt));
                t.rect.anchoredPosition = new Vector2(t.rect.anchoredPosition.x, t.y);
                live++;
                UpdateTicket(t, live, held, ReferenceEquals(looking, t.order.customer));
            }

            ordersHeader.text = "주문  <color=" + Palette.Html(Palette.Amber) + ">" + live + "</color>";
            ordersEmpty.gameObject.SetActive(live == 0);
        }

        public void SetPrompt(string prompt)
        {
            bool show = !string.IsNullOrEmpty(prompt);
            promptBox.gameObject.SetActive(show);
            crosshairImage.color = show ? Palette.Amber : Palette.WithAlpha(Palette.TextLight, 0.8f);
            float size = show ? 14f : 8f;
            crosshair.sizeDelta = Vector2.Lerp(crosshair.sizeDelta, new Vector2(size, size), 0.4f);
            if (!show) return;
            promptText.text = KeyCaps(prompt);
            promptBox.sizeDelta = new Vector2(promptText.preferredWidth + 56f, 58f);
        }

        public void SetHeld(string held)
        {
            bool show = !string.IsNullOrEmpty(held);
            heldBox.gameObject.SetActive(show);
            if (show)
            {
                heldText.text = "<size=18><color=" + Palette.Html(Palette.Muted) + ">손에 든 것</color></size>\n" + held;
                heldBox.sizeDelta = new Vector2(Mathf.Max(320f, heldText.preferredWidth + 60f), 84f);
                if (held != lastHeld) StartCoroutine(Pop(heldBox, 1.1f));
            }
            lastHeld = held;
        }

        public void Toast(string message, Color color)
        {
            if (toastRoutine != null) StopCoroutine(toastRoutine);
            toastRoutine = StartCoroutine(ToastRoutine(message, color));
        }

        public void ShowCookBar(string title, float marker, float zoneCenter, float zoneWidth, CookGrade? result)
        {
            if (!cookPanel.activeSelf)
            {
                cookPanel.SetActive(true);
                StartCoroutine(Pop((RectTransform)cookPanel.transform, 1.05f));
            }
            cookTitle.text = title;
            SetSpan(cookZone, zoneCenter - zoneWidth * 0.5f, zoneCenter + zoneWidth * 0.5f);
            SetSpan(cookPerfect, zoneCenter - zoneWidth * 0.2f, zoneCenter + zoneWidth * 0.2f);
            cookMarker.anchorMin = new Vector2(Mathf.Clamp01(marker), 0f);
            cookMarker.anchorMax = new Vector2(Mathf.Clamp01(marker), 1f);
            if (result.HasValue)
            {
                cookResult.text = Names.Of(result.Value) + "!";
                cookResult.fontSize = 40;
                cookResult.color = GradeColor(result.Value);
                if (!cookHadResult) StartCoroutine(Pop((RectTransform)cookResult.transform, 1.35f));
            }
            else
            {
                cookResult.text = KeyCaps("Space: 멈추기") + "   <size=20><color=" + Palette.Html(Palette.Muted) + ">노란 칸 = 완벽</color></size>";
                cookResult.fontSize = 26;
                cookResult.color = Palette.TextLight;
            }
            cookHadResult = result.HasValue;
        }

        public void HideCookBar()
        {
            cookPanel.SetActive(false);
            cookHadResult = false;
        }

        public void SetBubble(Customer c, string text, Color color, float patience01)
        {
            if (!bubbles.TryGetValue(c, out Bubble b))
            {
                b = MakeBubble();
                bubbles[c] = b;
                StartCoroutine(Pop(b.rect, 1.12f));
            }
            Camera cam = Camera.main;
            bool show = !string.IsNullOrEmpty(text) && cam != null;
            Vector3 sp = show ? cam.WorldToScreenPoint(c.transform.position + Vector3.up * 2.0f) : Vector3.zero;
            show &= sp.z > 0f;
            b.rect.gameObject.SetActive(show);
            if (!show) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(bubbleLayer, sp, null, out Vector2 local);
            b.rect.anchoredPosition = local;
            b.text.text = text;
            b.strip.color = color;
            b.rect.sizeDelta = new Vector2(340f, Mathf.Max(96f, b.text.preferredHeight + 44f));
            b.bar.SetActive(patience01 >= 0f);
            if (patience01 >= 0f)
            {
                ((RectTransform)b.fill.transform).anchorMax = new Vector2(patience01, 1f);
                b.fill.color = PatienceColor(patience01);
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
            startStage.text = stage + " 1일차   ·   오늘 목표 <color=" + Palette.Html(Palette.Amber) + ">" + Names.Won(target) + "</color>";
            SetScreen(startScreen, true, instant: true);
            hudGroup.alpha = 0f;
        }

        public void HideStart()
        {
            SetScreen(startScreen, false);
            StartCoroutine(Ease.Tween(0.4f, k => hudGroup.alpha = k, Ease.OutCubic));
        }

        public void ShowPause(bool show) => SetScreen(pauseScreen, show);

        public void ShowResult(bool success, string stage, int sales, int waste, int profit, int target, int sold, int lost)
        {
            HideCookBar();
            SetScreen(resultScreen, true);
            StartCoroutine(Ease.Tween(0.3f, k => hudGroup.alpha = 1f - k, Ease.OutCubic));
            resultTitle.text = success ? "목표 달성!" : "목표 미달성";
            resultTitle.color = success ? Palette.Leaf : Palette.Tomato;
            resultSub.text = stage + " 1일차 영업 결과";
            resultRows.text = "판매\n놓친 손님\n\n매출\n음식 폐기\n목표";
            resultValues.text = sold + "건\n" + lost + "명\n\n" + Names.Won(sales) + "\n<color=" + Palette.Html(Palette.Tomato) + ">-" +
                                Names.Won(waste) + "</color>\n" + Names.Won(target);
            resultNote.text = success ? "다음 스테이지는 아직 준비 중이에요." : "부족한 금액  " + Names.Won(target - profit);
            StartCoroutine(Pop(resultBanner, 1.15f));
            StartCoroutine(CountUp(resultProfit, profit, 1.1f));
        }

        // ================================================================ per frame

        void Update()
        {
            if (startScreen.gameObject.activeSelf)
            {
                startPrompt.color = Palette.WithAlpha(Palette.TextLight, 0.55f + 0.45f * Mathf.PingPong(Time.unscaledTime / 0.8f, 1f));
                if (GameInput.ConfirmDown()) onStart?.Invoke();
            }

            // Drop bubbles of customers that were destroyed.
            List<Customer> dead = null;
            foreach (var kv in bubbles) if (kv.Key == null) (dead ??= new List<Customer>()).Add(kv.Key);
            if (dead != null) foreach (var d in dead) { if (bubbles[d].rect != null) Destroy(bubbles[d].rect.gameObject); bubbles.Remove(d); }
        }

        // ================================================================ tickets

        Ticket MakeTicket(Order o, int index)
        {
            var t = new Ticket { order = o };
            t.rect = Panel("Ticket", ticketColumn, new Vector2(1, 1), new Vector2(1, 1), new Vector2(TicketWidth + 40f, 0f), new Vector2(TicketWidth, TicketHeight), new Vector2(1, 1), Palette.Paper);
            t.group = t.rect.gameObject.AddComponent<CanvasGroup>();
            t.y = -index * (TicketHeight + TicketGap);
            t.rect.anchoredPosition = new Vector2(TicketWidth + 40f, t.y);

            // Highlight frame (behind), shown when the player looks at this customer.
            t.frame = Panel("Frame", t.rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Palette.Amber).GetComponent<Image>();
            var fr = (RectTransform)t.frame.transform;
            fr.offsetMin = new Vector2(-5, -5);
            fr.offsetMax = new Vector2(5, 5);
            fr.SetAsFirstSibling();
            var paper = Panel("Paper", t.rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Palette.Paper);
            paper.offsetMin = paper.offsetMax = Vector2.zero;

            t.header = Panel("Header", paper, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, new Vector2(0, 40), new Vector2(0.5f, 1), o.customer.type.color).GetComponent<Image>();
            t.who = Txt("Who", (RectTransform)t.header.transform, Vector2.zero, Vector2.one, new Vector2(16, 0), new Vector2(-16, 0), 22, TextAnchor.MiddleLeft, Palette.TextDark);
            t.status = Txt("Status", (RectTransform)t.header.transform, Vector2.zero, Vector2.one, new Vector2(16, 0), new Vector2(-16, 0), 20, TextAnchor.MiddleRight, Palette.TextDark);

            t.title = Txt("Menu", paper, new Vector2(0, 1), new Vector2(1, 1), new Vector2(18, -92), new Vector2(-18, -46), 30, TextAnchor.MiddleLeft, Palette.TextDark);
            t.title.fontStyle = FontStyle.Bold;

            int n = o.menu.toppings.Length;
            t.chipBg = new Image[n];
            t.chipText = new Text[n];
            float chipW = (TicketWidth - 36f - (n - 1) * 8f) / n;
            for (int i = 0; i < n; i++)
            {
                var chip = Panel("Chip", paper, new Vector2(0, 0), new Vector2(0, 0), new Vector2(18 + i * (chipW + 8f), 22), new Vector2(chipW, 38), new Vector2(0, 0), Palette.PaperDim, pill);
                t.chipBg[i] = chip.GetComponent<Image>();
                t.chipText[i] = Txt("Label", chip, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 21, TextAnchor.MiddleCenter, Palette.TextDark);
            }

            var track = Panel("Patience", paper, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 6), new Vector2(-36, 8), new Vector2(0.5f, 0), Palette.PaperDim, pill);
            t.patienceFill = Panel("Fill", track, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0, 0.5f), Palette.Leaf, pill).GetComponent<Image>();
            var pf = (RectTransform)t.patienceFill.transform;
            pf.offsetMin = pf.offsetMax = Vector2.zero;

            StartCoroutine(Ease.Tween(0.35f, k => { if (t.rect != null) t.rect.anchoredPosition = new Vector2(Mathf.Lerp(TicketWidth + 40f, 0f, k), t.rect.anchoredPosition.y); }, Ease.OutBack));
            return t;
        }

        void UpdateTicket(Ticket t, int number, Dish held, bool lookedAt)
        {
            Order o = t.order;
            bool ready = held != null && o.Matches(held);
            t.who.text = "<b>#" + number + "  " + o.customer.type.name + "</b>";
            t.status.text = ready ? "<b>건네기 가능!</b>" : "";
            t.title.text = o.menu.name;

            for (int i = 0; i < t.chipBg.Length; i++)
            {
                Topping top = o.menu.toppings[i];
                bool excluded = o.excluded.Contains(top);
                bool has = held != null && held.toppings.Contains(top);
                string name = Names.Of(top);
                if (excluded && has) { t.chipBg[i].color = Palette.Tomato; t.chipText[i].color = Palette.TextLight; t.chipText[i].text = "✕ " + name + " 빼야 함!"; }
                else if (excluded) { t.chipBg[i].color = Palette.WithAlpha(Palette.Tomato, 0.18f); t.chipText[i].color = Palette.Tomato; t.chipText[i].text = "✕ " + name + " 빼기"; }
                else if (has) { t.chipBg[i].color = Palette.Leaf; t.chipText[i].color = Palette.TextLight; t.chipText[i].text = "✓ " + name; }
                else { t.chipBg[i].color = Palette.PaperDim; t.chipText[i].color = Palette.TextDark; t.chipText[i].text = name; }
            }

            float p = o.customer.Patience01;
            ((RectTransform)t.patienceFill.transform).anchorMax = new Vector2(p, 1f);
            Color pc = PatienceColor(p);
            if (p < 0.25f) pc.a = 0.45f + 0.55f * Mathf.PingPong(Time.unscaledTime * 3f, 1f);
            t.patienceFill.color = pc;

            t.frame.color = ready ? Palette.Leaf : Palette.Amber;
            t.frame.enabled = lookedAt || ready;
            float s = Mathf.Lerp(t.rect.localScale.x, lookedAt ? 1.03f : 1f, 0.25f);
            t.rect.localScale = new Vector3(s, s, 1f);
        }

        IEnumerator RetireTicket(Ticket t)
        {
            t.removing = true;
            float x0 = t.rect.anchoredPosition.x;
            yield return Ease.Tween(0.25f, k =>
            {
                if (t.rect == null) return;
                t.group.alpha = 1f - k;
                t.rect.anchoredPosition = new Vector2(x0 + 60f * k, t.rect.anchoredPosition.y);
            }, Ease.OutCubic);
            if (t.rect != null) Destroy(t.rect.gameObject);
            tickets.Remove(t);
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

            bubbleLayer = Stretch("Bubbles", root);
            hud = Stretch("HUD", root);
            hudGroup = hud.gameObject.AddComponent<CanvasGroup>();
            hudGroup.blocksRaycasts = false;
            BuildMoney();
            BuildClock();
            BuildOrders();
            BuildCenter();
            BuildCookBar();
            fxLayer = Stretch("FX", root);
            BuildScreens();

            SetPrompt(null);
            SetHeld(null);
        }

        void BuildMoney()
        {
            moneyBox = Panel("Money", hud, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -28), new Vector2(460, 132), new Vector2(0, 1), Palette.WithAlpha(Palette.Ink, 0.9f));
            Txt("Label", moneyBox, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -40), new Vector2(-22, -10), 20, TextAnchor.MiddleLeft, Palette.Muted).text = "오늘 정산";
            moneyValue = Txt("Value", moneyBox, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -88), new Vector2(-22, -38), 44, TextAnchor.MiddleLeft, Palette.Amber);
            moneyValue.fontStyle = FontStyle.Bold;
            wasteText = Txt("Waste", moneyBox, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -88), new Vector2(-22, -38), 22, TextAnchor.MiddleRight, Palette.Tomato);
            var track = Panel("Track", moneyBox, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 30), new Vector2(-44, 10), new Vector2(0.5f, 0), Palette.WithAlpha(Palette.TextLight, 0.14f), pill);
            moneyFill = Panel("Fill", track, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0, 0.5f), Palette.Amber, pill);
            moneyFill.offsetMin = moneyFill.offsetMax = Vector2.zero;
            moneyFillImage = moneyFill.GetComponent<Image>();
            moneyFill.anchorMax = new Vector2(0f, 1f);
            moneyTarget = Txt("Target", moneyBox, new Vector2(0, 0), new Vector2(1, 0), new Vector2(22, 0), new Vector2(-22, 28), 18, TextAnchor.MiddleLeft, Palette.Muted);
        }

        void BuildClock()
        {
            var box = Panel("Clock", hud, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-28, -28), new Vector2(TicketWidth, 104), new Vector2(1, 1), Palette.WithAlpha(Palette.Ink, 0.9f));
            clockTime = Txt("Time", box, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -66), new Vector2(-22, -10), 40, TextAnchor.MiddleLeft, Palette.TextLight);
            clockTime.fontStyle = FontStyle.Bold;
            clockState = Txt("State", box, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -66), new Vector2(-22, -10), 22, TextAnchor.MiddleRight, Palette.Leaf);
            var tag = Panel("Lunch", box, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-130, -22), new Vector2(84, 30), new Vector2(1, 1), Palette.Amber, pill);
            Txt("Text", tag, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 18, TextAnchor.MiddleCenter, Palette.TextDark).text = "<b>점심</b>";
            lunchTag = tag.gameObject;
            lunchTag.SetActive(false);
            var track = Panel("Day", box, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 18), new Vector2(-44, 10), new Vector2(0.5f, 0), Palette.WithAlpha(Palette.TextLight, 0.14f), pill);
            dayFill = Panel("Fill", track, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0, 0.5f), Palette.Mint, pill);
            dayFill.offsetMin = dayFill.offsetMax = Vector2.zero;
            dayFill.anchorMax = new Vector2(0f, 1f);
        }

        void BuildOrders()
        {
            var header = Panel("Orders Header", hud, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-28, -146), new Vector2(TicketWidth, 44), new Vector2(1, 1), Palette.WithAlpha(Palette.Ink, 0.9f));
            ordersHeader = Txt("Text", header, Vector2.zero, Vector2.one, new Vector2(18, 0), new Vector2(-18, 0), 24, TextAnchor.MiddleLeft, Palette.TextLight);
            ordersHeader.fontStyle = FontStyle.Bold;
            Txt("Hint", header, Vector2.zero, Vector2.one, new Vector2(18, 0), new Vector2(-18, 0), 17, TextAnchor.MiddleRight, Palette.Muted).text = "✓ 넣음   ✕ 빼기";
            ticketColumn = Rect("Tickets", hud, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-28, -202), new Vector2(TicketWidth, 600));
            ticketColumn.pivot = new Vector2(1, 1);
            ticketColumn.anchoredPosition = new Vector2(-28, -202);
            ordersEmpty = Txt("Empty", ticketColumn, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -60), new Vector2(0, 0), 20, TextAnchor.MiddleCenter, Palette.WithAlpha(Palette.TextLight, 0.75f));
            ordersEmpty.text = "받은 주문이 없어요\n손님을 보고 E로 주문 받기";
            AddShadow(ordersEmpty);
        }

        void BuildCenter()
        {
            crosshair = Panel("Crosshair", hud, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8, 8), new Vector2(0.5f, 0.5f), Palette.TextLight, pill);
            crosshairImage = crosshair.GetComponent<Image>();
            crosshairImage.raycastTarget = false;

            promptBox = Panel("Prompt", hud, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -64), new Vector2(300, 58), new Vector2(0.5f, 1f), Palette.WithAlpha(Palette.Ink, 0.88f), pill);
            promptText = Txt("Text", promptBox, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 28, TextAnchor.MiddleCenter, Palette.TextLight);
            promptText.horizontalOverflow = HorizontalWrapMode.Overflow;

            heldBox = Panel("Held", hud, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 36), new Vector2(360, 84), new Vector2(0.5f, 0), Palette.WithAlpha(Palette.Ink, 0.9f));
            heldText = Txt("Text", heldBox, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 26, TextAnchor.MiddleCenter, Palette.TextLight);
            heldText.horizontalOverflow = HorizontalWrapMode.Overflow;

            toastBox = Panel("Toast", hud, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(640, 64), new Vector2(0.5f, 1), Palette.WithAlpha(Palette.Ink, 0.94f), pill);
            toastGroup = toastBox.gameObject.AddComponent<CanvasGroup>();
            toastGroup.alpha = 0f;
            toastGroup.blocksRaycasts = false;
            toastStrip = Panel("Dot", toastBox, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(26, 0), new Vector2(16, 16), new Vector2(0, 0.5f), Palette.Amber, pill).GetComponent<Image>();
            toastText = Txt("Text", toastBox, Vector2.zero, Vector2.one, new Vector2(30, 0), Vector2.zero, 28, TextAnchor.MiddleCenter, Palette.TextLight);
            toastText.horizontalOverflow = HorizontalWrapMode.Overflow;

            var hint = Txt("Hint", hud, new Vector2(0, 0), new Vector2(0, 0), new Vector2(28, 20), new Vector2(760, 52), 19, TextAnchor.MiddleLeft, Palette.WithAlpha(Palette.TextLight, 0.7f));
            hint.text = KeyCaps("WASD: 이동   E: 상호작용   R: 거절   Esc: 일시정지");
            AddShadow(hint);
        }

        void BuildCookBar()
        {
            var panel = Panel("Cook Bar", hud, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 150), new Vector2(800, 196), new Vector2(0.5f, 0f), Palette.WithAlpha(Palette.Ink, 0.94f));
            cookPanel = panel.gameObject;
            cookTitle = Txt("Title", panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -56), new Vector2(0, -12), 30, TextAnchor.MiddleCenter, Palette.TextLight);
            cookTitle.fontStyle = FontStyle.Bold;
            var bar = Panel("Bar", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 4), new Vector2(700, 48), new Vector2(0.5f, 0.5f), Palette.InkSoft, pill);
            cookZone = Panel("Zone", bar, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Palette.Leaf);
            cookPerfect = Panel("Perfect", bar, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Palette.Amber);
            cookMarker = Panel("Marker", bar, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(10, 22), new Vector2(0.5f, 0.5f), Palette.TextLight, pill);
            cookResult = Txt("Result", panel, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 10), new Vector2(0, 62), 26, TextAnchor.MiddleCenter, Palette.TextLight);
            cookPanel.SetActive(false);
        }

        void BuildScreens()
        {
            // ---- Title: the live scene is the backdrop, a scrim keeps text readable.
            startScreen = MakeScreen("Title Screen", 0.72f);
            var st = (RectTransform)startScreen.transform;
            var title = Txt("Title", st, new Vector2(0, 0.62f), new Vector2(1, 0.62f), new Vector2(0, -60), new Vector2(0, 60), 88, TextAnchor.MiddleCenter, Palette.Amber);
            title.text = "<b>등록금은 장사로 갚는다</b>";
            AddShadow(title, 4f);
            startStage = Txt("Stage", st, new Vector2(0, 0.52f), new Vector2(1, 0.52f), new Vector2(0, -26), new Vector2(0, 26), 34, TextAnchor.MiddleCenter, Palette.TextLight);
            var how = Panel("How To", st, new Vector2(0.5f, 0.32f), new Vector2(0.5f, 0.32f), Vector2.zero, new Vector2(1000, 250), new Vector2(0.5f, 0.5f), Palette.WithAlpha(Palette.InkSoft, 0.92f));
            Txt("Text", how, Vector2.zero, Vector2.one, new Vector2(40, 20), new Vector2(-40, -20), 25, TextAnchor.MiddleLeft, Palette.TextLight).text =
                "1.  손님을 보고 " + Key("E") + " 주문 받기   (" + Key("R") + " 거절)\n" +
                "2.  그릴에서 " + Key("E") + " → 움직이는 막대를 " + Key("Space") + " 로 초록 칸에 멈추기\n" +
                "3.  다 익으면 그릴에서 " + Key("E") + " 로 담기  (오래 두면 타요!)\n" +
                "4.  주문표를 보고 소스 · 마요 · 파 넣기  (<color=" + Palette.Html(Palette.Tomato) + ">✕ 빼기</color> 는 넣지 말기)\n" +
                "5.  손님을 보고 " + Key("E") + " 로 건네기.  잘못 만든 건 쓰레기통에";
            startPrompt = Txt("Prompt", st, new Vector2(0, 0.13f), new Vector2(1, 0.13f), new Vector2(0, -24), new Vector2(0, 24), 30, TextAnchor.MiddleCenter, Palette.TextLight);
            startPrompt.text = "Enter 를 눌러 장사 시작";
            Button(st, "장사 시작", new Vector2(0.5f, 0.05f), Vector2.zero, new Vector2(300, 62), () => onStart?.Invoke());
            startScreen.gameObject.SetActive(false);

            // ---- Pause
            pauseScreen = MakeScreen("Pause Screen", 0.6f);
            var pc = Panel("Card", (RectTransform)pauseScreen.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(480, 420), new Vector2(0.5f, 0.5f), Palette.Ink);
            Txt("Title", pc, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -96), new Vector2(0, -24), 46, TextAnchor.MiddleCenter, Palette.TextLight).text = "<b>일시정지</b>";
            Button(pc, "계속하기", new Vector2(0.5f, 1f), new Vector2(0, -160), new Vector2(320, 64), () => GameManager.Instance.Resume());
            Button(pc, "처음부터 다시", new Vector2(0.5f, 1f), new Vector2(0, -240), new Vector2(320, 64), () => GameManager.Instance.Retry(), secondary: true);
            Button(pc, "나가기", new Vector2(0.5f, 1f), new Vector2(0, -320), new Vector2(320, 64), () => GameManager.Instance.Quit(), secondary: true);
            pauseScreen.gameObject.SetActive(false);

            // ---- Result
            resultScreen = MakeScreen("Result Screen", 0.75f);
            var rc = Panel("Card", (RectTransform)resultScreen.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 720), new Vector2(0.5f, 0.5f), Palette.Ink);
            resultBanner = Rect("Banner", rc, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -70), new Vector2(0, 90));
            resultTitle = Txt("Title", resultBanner, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 60, TextAnchor.MiddleCenter, Palette.Leaf);
            resultTitle.fontStyle = FontStyle.Bold;
            resultSub = Txt("Sub", rc, new Vector2(0, 1), new Vector2(1, 1), new Vector2(80, -170), new Vector2(-80, -126), 24, TextAnchor.MiddleCenter, Palette.Muted);
            resultRows = Txt("Rows", rc, new Vector2(0, 1), new Vector2(1, 1), new Vector2(80, -440), new Vector2(-80, -200), 28, TextAnchor.UpperLeft, Palette.Muted);
            resultValues = Txt("Values", rc, new Vector2(0, 1), new Vector2(1, 1), new Vector2(80, -440), new Vector2(-80, -200), 28, TextAnchor.UpperRight, Palette.TextLight);
            Panel("Line", rc, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 286), new Vector2(-160, 3), new Vector2(0.5f, 0.5f), Palette.InkSoft);
            Txt("Profit Label", rc, new Vector2(0, 0), new Vector2(1, 0), new Vector2(80, 210), new Vector2(-80, 270), 30, TextAnchor.MiddleLeft, Palette.TextLight).text = "<b>정산</b>";
            resultProfit = Txt("Profit", rc, new Vector2(0, 0), new Vector2(1, 0), new Vector2(80, 200), new Vector2(-80, 280), 52, TextAnchor.MiddleRight, Palette.Amber);
            resultProfit.fontStyle = FontStyle.Bold;
            resultNote = Txt("Note", rc, new Vector2(0, 0), new Vector2(1, 0), new Vector2(40, 140), new Vector2(-40, 190), 24, TextAnchor.MiddleCenter, Palette.Muted);
            Button(rc, "재도전", new Vector2(0.5f, 0f), new Vector2(-150, 60), new Vector2(260, 66), () => GameManager.Instance.Retry());
            Button(rc, "나가기", new Vector2(0.5f, 0f), new Vector2(150, 60), new Vector2(260, 66), () => GameManager.Instance.Quit(), secondary: true);
            resultScreen.gameObject.SetActive(false);
        }

        Bubble MakeBubble()
        {
            var b = new Bubble();
            b.rect = Panel("Bubble", bubbleLayer, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(340, 120), new Vector2(0.5f, 0f), Palette.WithAlpha(Palette.Paper, 0.96f));
            b.strip = Panel("Strip", b.rect, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, new Vector2(0, 8), new Vector2(0.5f, 1), Palette.Mint).GetComponent<Image>();
            b.text = Txt("Text", b.rect, Vector2.zero, Vector2.one, new Vector2(14, 22), new Vector2(-14, -14), 23, TextAnchor.UpperCenter, Palette.TextDark);
            var bar = Panel("Patience", b.rect, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 10), new Vector2(-28, 8), new Vector2(0.5f, 0), Palette.PaperDim, pill);
            b.bar = bar.gameObject;
            b.fill = Panel("Fill", bar, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0, 0.5f), Palette.Leaf, pill).GetComponent<Image>();
            var f = (RectTransform)b.fill.transform;
            f.offsetMin = f.offsetMax = Vector2.zero;
            return b;
        }

        // ================================================================ motion

        IEnumerator Pop(RectTransform r, float peak)
        {
            if (r == null) yield break;
            yield return Ease.Tween(0.09f, k => { if (r != null) r.localScale = Vector3.one * Mathf.Lerp(1f, peak, k); }, Ease.OutCubic);
            yield return Ease.Tween(0.22f, k => { if (r != null) r.localScale = Vector3.one * Mathf.LerpUnclamped(peak, 1f, k); }, Ease.OutBack);
        }

        IEnumerator ToastRoutine(string message, Color color)
        {
            toastText.text = message;
            toastStrip.color = color;
            toastBox.sizeDelta = new Vector2(toastText.preferredWidth + 110f, 64f);
            yield return Ease.Tween(0.25f, k =>
            {
                toastGroup.alpha = Mathf.Clamp01(k * 2f);
                toastBox.localScale = Vector3.one * Mathf.LerpUnclamped(0.8f, 1f, k);
            }, Ease.OutBack);
            float end = Time.unscaledTime + toastSeconds;
            while (Time.unscaledTime < end) yield return null;
            yield return Ease.Tween(0.3f, k => toastGroup.alpha = 1f - k, Ease.OutCubic);
            toastRoutine = null;
        }

        void FloatText(string text, Color color, RectTransform anchor, Vector2 offset)
        {
            var t = Txt("Float", fxLayer, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero, 34, TextAnchor.MiddleLeft, color);
            t.fontStyle = FontStyle.Bold;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            AddShadow(t, 2f);
            var r = (RectTransform)t.transform;
            r.sizeDelta = new Vector2(400, 50);
            r.pivot = new Vector2(0, 0.5f);
            Vector2 start = new Vector2(anchor.anchoredPosition.x + 22f, anchor.anchoredPosition.y) + offset;
            StartCoroutine(FloatRoutine(t, r, start));
        }

        IEnumerator FloatRoutine(Text t, RectTransform r, Vector2 start)
        {
            yield return Ease.Tween(0.9f, k =>
            {
                if (r == null) return;
                r.anchoredPosition = start + new Vector2(0f, 50f * k);
                Color c = t.color;
                c.a = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
                t.color = c;
            }, Ease.OutCubic);
            if (r != null) Destroy(r.gameObject);
        }

        IEnumerator CountUp(Text text, int value, float seconds)
        {
            yield return Ease.Tween(seconds, k => text.text = Names.Won(Mathf.RoundToInt(value * k / 100f) * 100), Ease.OutCubic);
            text.text = Names.Won(value);
        }

        void SetScreen(CanvasGroup screen, bool show, bool instant = false)
        {
            if (show == (screen.gameObject.activeSelf && screen.alpha > 0.99f) && !instant) return;
            StartCoroutine(FadeScreen(screen, show, instant ? 0f : 0.3f));
        }

        IEnumerator FadeScreen(CanvasGroup g, bool show, float seconds)
        {
            g.gameObject.SetActive(true);
            g.interactable = g.blocksRaycasts = show;
            float from = g.alpha, to = show ? 1f : 0f;
            if (seconds > 0f) yield return Ease.Tween(seconds, k => g.alpha = Mathf.Lerp(from, to, k), Ease.OutCubic);
            g.alpha = to;
            if (!show) g.gameObject.SetActive(false);
        }

        // ================================================================ helpers

        static Color PatienceColor(float p) => p > 0.5f ? Palette.Leaf : p > 0.25f ? Palette.Amber : Palette.Tomato;

        static Color GradeColor(CookGrade g) => g == CookGrade.Perfect ? Palette.Amber : g == CookGrade.Good ? Palette.Leaf : Palette.Tomato;

        static string Key(string k) => "<color=" + Palette.Html(Palette.Amber) + "><b>[" + k + "]</b></color>";

        static readonly Regex KeyPattern = new Regex(@"(^|\s)(WASD|Space|Esc|[A-Z]): ");

        /// <summary>"E: 주문 받기" → "[E] 주문 받기" with the key in the accent colour.</summary>
        static string KeyCaps(string s) => KeyPattern.Replace(s, m => m.Groups[1].Value + Key(m.Groups[2].Value) + " ");

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

        static RectTransform Stretch(string name, Transform parent)
        {
            var r = Rect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            r.offsetMin = r.offsetMax = Vector2.zero;
            return r;
        }

        RectTransform Panel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, Vector2 pivot, Color color, Sprite sprite = null)
        {
            var r = Rect(name, parent, anchorMin, anchorMax, pos, size);
            r.pivot = pivot;
            r.anchoredPosition = pos;
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = sprite != null ? sprite : round;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
            return r;
        }

        /// <summary>Text stretched inside its parent between anchorMin/anchorMax, inset by offsetMin/offsetMax.</summary>
        Text Txt(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, int size, TextAnchor align, Color color)
        {
            var r = Rect(name, parent, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
            r.offsetMin = offsetMin;
            r.offsetMax = offsetMax;
            var t = r.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        static void AddShadow(Text t, float distance = 1.5f)
        {
            var s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = Palette.WithAlpha(Palette.Ink, 0.85f);
            s.effectDistance = new Vector2(distance, -distance);
        }

        CanvasGroup MakeScreen(string name, float scrimAlpha)
        {
            var r = Stretch(name, root);
            var img = r.gameObject.AddComponent<Image>();
            img.color = Palette.WithAlpha(Palette.Ink, scrimAlpha);
            img.raycastTarget = true;
            var g = r.gameObject.AddComponent<CanvasGroup>();
            g.alpha = 0f;
            return g;
        }

        void Button(RectTransform parent, string label, Vector2 anchor, Vector2 pos, Vector2 size, Action onClick, bool secondary = false)
        {
            var r = Panel(label, parent, anchor, anchor, pos, size, new Vector2(0.5f, 0.5f), secondary ? Palette.InkSoft : Palette.Amber, pill);
            var img = r.GetComponent<Image>();
            img.raycastTarget = true;
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.fadeDuration = 0.08f;
            b.colors = colors;
            b.onClick.AddListener(() => onClick());
            var t = Txt("Label", r, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 28, TextAnchor.MiddleCenter, secondary ? Palette.TextLight : Palette.TextDark);
            t.text = "<b>" + label + "</b>";
        }

        /// <summary>A white rounded-rectangle sprite with 9-slice borders, generated once (no art assets needed).</summary>
        static Sprite MakeRoundedSprite(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Rounded" + radius };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
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
