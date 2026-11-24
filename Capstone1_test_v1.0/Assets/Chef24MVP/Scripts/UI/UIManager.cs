using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Chef24
{
    /// <summary>
    /// Builds and drives every screen of the MVP in code (no prefabs needed):
    /// HUD (money / clock / order tickets / prompt / held dish / toasts), customer speech bubbles, the cooking bar,
    /// and the title / pause / result screens (UI-006, UI-007 - simplified).
    /// Look: the team's title mock-up - cream cards with brown outlines, orange pill buttons, chunky Korean lettering.
    /// All colours come from <see cref="Palette"/>; all motion runs on unscaled time so it survives pause.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("Fonts (set by the scene builder)")]
        public TMP_FontAsset bodyFont;
        public TMP_FontAsset titleFont;
        public Material bodyOutline;
        public Material titleOutline;

        [Header("Feel")]
        public float ticketSlideSpeed = 12f;
        public float moneyCountSpeed = 6f;
        public float toastSeconds = 2.4f;

        enum Style { Body, BodyOutline, Title, TitleOutline }

        Sprite round, pill, iconCheck, iconCross;
        RectTransform root, hud, bubbleLayer, fxLayer;
        CanvasGroup hudGroup;

        // HUD
        RectTransform moneyBox, moneyFill;
        Image moneyFillImage;
        TMP_Text moneyValue, moneyTarget, wasteText;
        TMP_Text clockTime, clockState;
        Image clockStateBg;
        GameObject lunchTag;
        RectTransform dayFill;
        RectTransform ticketColumn;
        TMP_Text ordersHeader, ordersEmpty;
        RectTransform crosshair;
        Image crosshairDot;
        RectTransform promptBox;
        TMP_Text promptText;
        RectTransform heldBox;
        TMP_Text heldText;
        RectTransform toastBox;
        CanvasGroup toastGroup;
        Image toastDot;
        TMP_Text toastText;
        Coroutine toastRoutine;

        // Cook bar
        GameObject cookPanel;
        TMP_Text cookTitle, cookResult, cookHint;
        RectTransform cookZone, cookPerfect, cookMarker;
        bool cookHadResult;

        // Screens
        CanvasGroup startScreen, pauseScreen, resultScreen;
        TMP_Text startStage, startPrompt;
        TMP_Text resultTitle, resultSub, resultRows, resultValues, resultProfit, resultNote;
        RectTransform resultBanner;
        Action onStart;

        // State
        float shownProfit;
        int lastProfit;
        bool moneyStarted;
        string lastHeld;
        Interactor interactor;

        class Card
        {
            public RectTransform rect;
            public Image border, fill;
        }

        class Ticket
        {
            public Order order;
            public Card card;
            public CanvasGroup group;
            public Image header, patienceFill;
            public TMP_Text who, title, status;
            public Image[] chipBg, chipIcon;
            public TMP_Text[] chipText;
            public float y;
            public bool removing;
        }
        readonly List<Ticket> tickets = new List<Ticket>();

        class Bubble
        {
            public Card card;
            public Image strip, fill;
            public TMP_Text text;
            public GameObject bar;
        }
        readonly Dictionary<Customer, Bubble> bubbles = new Dictionary<Customer, Bubble>();

        const float TicketWidth = 420f, TicketHeight = 164f, TicketGap = 12f, Border = 4f;

        void Awake()
        {
            Instance = this;
            if (bodyFont == null || titleFont == null)
                Debug.LogWarning("[Chef24] UIManager has no fonts. Run Tools > Chef24 > Build MVP Scene so Korean text shows.");
            round = MakeRoundedSprite(64, 18);
            pill = MakeRoundedSprite(64, 30);
            iconCheck = MakeIcon(true);
            iconCross = MakeIcon(false);
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
                FloatText((diff > 0 ? "+" : "-") + Names.Won(Mathf.Abs(diff)), diff > 0 ? Palette.Orange : Palette.Red,
                          new Vector2(50f, -200f));
                StartCoroutine(Pop(moneyBox, 1.06f));
                lastProfit = profit;
            }

            shownProfit = Mathf.Lerp(shownProfit, profit, 1f - Mathf.Exp(-moneyCountSpeed * Time.unscaledDeltaTime));
            if (Mathf.Abs(shownProfit - profit) < 50f) shownProfit = profit;
            moneyValue.text = Names.Won(Mathf.RoundToInt(shownProfit / 100f) * 100);
            moneyValue.color = profit >= target ? Palette.Leaf : Palette.Orange;
            moneyTarget.text = "목표 " + Names.Won(target) + "   (" + Mathf.Clamp(Mathf.RoundToInt(100f * profit / Mathf.Max(1, target)), 0, 999) + "%)";
            wasteText.text = waste > 0 ? "폐기 -" + Names.Won(waste) : "";

            float fill = Mathf.Clamp01(shownProfit / Mathf.Max(1, target));
            moneyFill.anchorMax = new Vector2(fill, 1f);
            moneyFillImage.color = profit >= target ? Palette.Leaf : Palette.Orange;
        }

        public void SetClock(string time, bool open)
        {
            clockTime.text = time;
            var clock = GameManager.Instance != null ? GameManager.Instance.clock : null;
            bool lunch = clock != null && clock.IsLunchTime && open;
            clockState.text = open ? "영업 중" : "영업 종료";
            clockStateBg.color = open ? Palette.Leaf : Palette.Red;
            if (lunchTag.activeSelf != lunch) lunchTag.SetActive(lunch);
            if (clock != null) dayFill.anchorMax = new Vector2(clock.DayProgress, 1f);
        }

        public void SetOrders(List<Order> orders)
        {
            if (interactor == null) interactor = FindObjectOfType<Interactor>();
            Dish held = GameManager.Instance != null && GameManager.Instance.hands != null ? GameManager.Instance.hands.Held : null;
            object looking = interactor != null ? interactor.Current : null;

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
                t.card.rect.anchoredPosition = new Vector2(t.card.rect.anchoredPosition.x, t.y);
                live++;
                UpdateTicket(t, live, held, ReferenceEquals(looking, t.order.customer));
            }

            ordersHeader.text = "주문  <color=" + Palette.Html(Palette.Gold) + ">" + live + "</color>";
            ordersEmpty.gameObject.SetActive(live == 0);
        }

        public void SetPrompt(string prompt)
        {
            bool show = !string.IsNullOrEmpty(prompt);
            promptBox.gameObject.SetActive(show);
            crosshairDot.color = show ? Palette.Gold : Palette.Cream;
            float size = show ? 18f : 12f;
            crosshair.sizeDelta = Vector2.Lerp(crosshair.sizeDelta, new Vector2(size, size), 0.4f);
            if (!show) return;
            promptText.text = KeyCaps(prompt);
            promptBox.sizeDelta = new Vector2(promptText.GetPreferredValues(promptText.text, 2000f, 60f).x + 64f, 62f);
        }

        public void SetHeld(string held)
        {
            bool show = !string.IsNullOrEmpty(held);
            heldBox.gameObject.SetActive(show);
            if (show)
            {
                heldText.text = "<size=18><color=" + Palette.Html(Palette.BrownSoft) + ">손에 든 것</color></size>\n" + held;
                heldBox.sizeDelta = new Vector2(Mathf.Max(320f, heldText.GetPreferredValues(heldText.text, 2000f, 90f).x + 70f), 92f);
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
            cookResult.gameObject.SetActive(result.HasValue);
            cookHint.gameObject.SetActive(!result.HasValue);
            if (result.HasValue)
            {
                cookResult.text = Names.Of(result.Value) + "!";
                cookResult.color = GradeColor(result.Value);
                if (!cookHadResult) StartCoroutine(Pop((RectTransform)cookResult.transform, 1.35f));
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
                StartCoroutine(Pop(b.card.rect, 1.12f));
            }
            Camera cam = Camera.main;
            bool show = !string.IsNullOrEmpty(text) && cam != null;
            Vector3 sp = show ? cam.WorldToScreenPoint(c.transform.position + Vector3.up * 2.0f) : Vector3.zero;
            show &= sp.z > 0f;
            b.card.rect.gameObject.SetActive(show);
            if (!show) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(bubbleLayer, sp, null, out Vector2 local);
            b.card.rect.anchoredPosition = local;
            b.text.text = text;
            b.strip.color = color;
            b.card.rect.sizeDelta = new Vector2(340f, Mathf.Max(100f, b.text.GetPreferredValues(text, 312f, 400f).y + 50f));
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
                if (b.card.rect != null) Destroy(b.card.rect.gameObject);
                bubbles.Remove(c);
            }
        }

        public void ShowStart(string stage, int target, Action start)
        {
            onStart = start;
            startStage.text = stage + " 1일차    오늘 목표 <color=" + Palette.Html(Palette.Gold) + ">" + Names.Won(target) + "</color>";
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
            resultTitle.color = success ? Palette.Leaf : Palette.Red;
            resultSub.text = stage + " 1일차 영업 결과";
            resultRows.text = "판매\n놓친 손님\n\n매출\n음식 폐기\n목표";
            resultValues.text = sold + "건\n" + lost + "명\n\n" + Names.Won(sales) + "\n<color=" + Palette.Html(Palette.Red) + ">-" +
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
                startPrompt.alpha = 0.55f + 0.45f * Mathf.PingPong(Time.unscaledTime / 0.8f, 1f);
                if (GameInput.ConfirmDown()) onStart?.Invoke();
            }

            List<Customer> dead = null;
            foreach (var kv in bubbles) if (kv.Key == null) (dead ??= new List<Customer>()).Add(kv.Key);
            if (dead != null) foreach (var d in dead) { if (bubbles[d].card.rect != null) Destroy(bubbles[d].card.rect.gameObject); bubbles.Remove(d); }
        }

        // ================================================================ tickets

        Ticket MakeTicket(Order o, int index)
        {
            var t = new Ticket { order = o };
            float offX = TicketWidth + 40f;
            t.card = MakeCard("Ticket", ticketColumn, new Vector2(1, 1), new Vector2(1, 1), new Vector2(offX, 0f), new Vector2(TicketWidth, TicketHeight), new Vector2(1, 1), Palette.Cream);
            t.group = t.card.rect.gameObject.AddComponent<CanvasGroup>();
            t.y = -index * (TicketHeight + TicketGap);
            t.card.rect.anchoredPosition = new Vector2(offX, t.y);
            var r = t.card.rect;

            t.header = Panel("Header", r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -Border), new Vector2(-Border * 2, 40), new Vector2(0.5f, 1), o.customer.type.color).GetComponent<Image>();
            t.who = Txt("Who", t.header.transform, Vector2.zero, Vector2.one, new Vector2(14, 0), new Vector2(-14, 0), 23, TextAlignmentOptions.Left, Palette.Brown, Style.Body);
            t.status = Txt("Status", t.header.transform, Vector2.zero, Vector2.one, new Vector2(14, 0), new Vector2(-14, 0), 21, TextAlignmentOptions.Right, Palette.Brown, Style.Body);

            t.title = Txt("Menu", r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(20, -96), new Vector2(-20, -46), 32, TextAlignmentOptions.Left, Palette.Brown, Style.Title);

            int n = o.menu.toppings.Length;
            t.chipBg = new Image[n];
            t.chipIcon = new Image[n];
            t.chipText = new TMP_Text[n];
            float chipW = (TicketWidth - 40f - (n - 1) * 8f) / n;
            for (int i = 0; i < n; i++)
            {
                var chip = Panel("Chip", r, new Vector2(0, 0), new Vector2(0, 0), new Vector2(20 + i * (chipW + 8f), 24), new Vector2(chipW, 40), new Vector2(0, 0), Palette.CreamDark, pill);
                t.chipBg[i] = chip.GetComponent<Image>();
                var icon = Panel("Icon", chip, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(22, 22), new Vector2(0, 0.5f), Palette.Brown, iconCheck);
                icon.GetComponent<Image>().type = Image.Type.Simple;
                t.chipIcon[i] = icon.GetComponent<Image>();
                t.chipText[i] = Txt("Label", chip, Vector2.zero, Vector2.one, new Vector2(30, 0), new Vector2(-6, 0), 21, TextAlignmentOptions.Center, Palette.Brown, Style.Body);
            }

            var track = Panel("Patience", r, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 10), new Vector2(-40, 8), new Vector2(0.5f, 0), Palette.CreamDark, pill);
            t.patienceFill = Panel("Fill", track, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0, 0.5f), Palette.Leaf, pill).GetComponent<Image>();
            var pf = (RectTransform)t.patienceFill.transform;
            pf.offsetMin = pf.offsetMax = Vector2.zero;

            StartCoroutine(Ease.Tween(0.35f, k => { if (r != null) r.anchoredPosition = new Vector2(Mathf.Lerp(offX, 0f, k), r.anchoredPosition.y); }, Ease.OutBack));
            return t;
        }

        void UpdateTicket(Ticket t, int number, Dish held, bool lookedAt)
        {
            Order o = t.order;
            bool ready = held != null && o.Matches(held);
            t.who.text = "#" + number + "  " + o.customer.type.name;
            t.status.text = ready ? "건네기 가능!" : "";
            t.title.text = o.menu.name;

            for (int i = 0; i < t.chipBg.Length; i++)
            {
                Topping top = o.menu.toppings[i];
                bool excluded = o.excluded.Contains(top);
                bool has = held != null && held.toppings.Contains(top);
                string name = Names.Of(top);
                Color bg, fg;
                Sprite icon;
                string label;
                if (excluded && has) { bg = Palette.Red; fg = Palette.Cream; icon = iconCross; label = name + " 빼야 해요!"; }
                else if (excluded) { bg = Palette.WithAlpha(Palette.Red, 0.16f); fg = Palette.Red; icon = iconCross; label = name + " 빼기"; }
                else if (has) { bg = Palette.Leaf; fg = Palette.Cream; icon = iconCheck; label = name; }
                else { bg = Palette.CreamDark; fg = Palette.Brown; icon = null; label = name; }
                t.chipBg[i].color = bg;
                t.chipText[i].color = fg;
                t.chipText[i].text = label;
                t.chipIcon[i].enabled = icon != null;
                t.chipIcon[i].sprite = icon;
                t.chipIcon[i].color = fg;
            }

            float p = o.customer.Patience01;
            ((RectTransform)t.patienceFill.transform).anchorMax = new Vector2(p, 1f);
            Color pc = PatienceColor(p);
            if (p < 0.25f) pc.a = 0.45f + 0.55f * Mathf.PingPong(Time.unscaledTime * 3f, 1f);
            t.patienceFill.color = pc;

            t.card.border.color = ready ? Palette.Leaf : lookedAt ? Palette.Gold : Palette.Brown;
            float s = Mathf.Lerp(t.card.rect.localScale.x, lookedAt || ready ? 1.03f : 1f, 0.25f);
            t.card.rect.localScale = new Vector3(s, s, 1f);
        }

        IEnumerator RetireTicket(Ticket t)
        {
            t.removing = true;
            var r = t.card.rect;
            float x0 = r.anchoredPosition.x;
            yield return Ease.Tween(0.25f, k =>
            {
                if (r == null) return;
                t.group.alpha = 1f - k;
                r.anchoredPosition = new Vector2(x0 + 60f * k, r.anchoredPosition.y);
            }, Ease.OutCubic);
            if (r != null) Destroy(r.gameObject);
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
            var card = MakeCard("Money", hud, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -28), new Vector2(460, 140), new Vector2(0, 1), Palette.Cream);
            moneyBox = card.rect;
            Txt("Label", moneyBox, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -42), new Vector2(-24, -12), 21, TextAlignmentOptions.Left, Palette.BrownSoft, Style.Body).text = "오늘 정산";
            moneyValue = Txt("Value", moneyBox, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -96), new Vector2(-22, -38), 48, TextAlignmentOptions.Left, Palette.Orange, Style.TitleOutline);
            wasteText = Txt("Waste", moneyBox, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -96), new Vector2(-24, -38), 22, TextAlignmentOptions.Right, Palette.Red, Style.Body);
            var track = Panel("Track", moneyBox, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 34), new Vector2(-48, 12), new Vector2(0.5f, 0), Palette.CreamDark, pill);
            moneyFill = Panel("Fill", track, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0, 0.5f), Palette.Orange, pill);
            moneyFill.offsetMin = moneyFill.offsetMax = Vector2.zero;
            moneyFill.anchorMax = new Vector2(0f, 1f);
            moneyFillImage = moneyFill.GetComponent<Image>();
            moneyTarget = Txt("Target", moneyBox, new Vector2(0, 0), new Vector2(1, 0), new Vector2(24, 4), new Vector2(-24, 32), 18, TextAlignmentOptions.Left, Palette.BrownSoft, Style.Body);
        }

        void BuildClock()
        {
            var card = MakeCard("Clock", hud, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-28, -28), new Vector2(TicketWidth, 108), new Vector2(1, 1), Palette.Cream);
            var box = card.rect;
            clockTime = Txt("Time", box, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -72), new Vector2(-24, -12), 42, TextAlignmentOptions.Left, Palette.Brown, Style.Title);
            var state = Panel("State", box, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-22, -24), new Vector2(110, 38), new Vector2(1, 1), Palette.Leaf, pill);
            clockStateBg = state.GetComponent<Image>();
            clockState = Txt("Text", state, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 21, TextAlignmentOptions.Center, Palette.Cream, Style.Body);
            var tag = Panel("Lunch", box, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-140, -24), new Vector2(76, 38), new Vector2(1, 1), Palette.Gold, pill);
            Txt("Text", tag, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 21, TextAlignmentOptions.Center, Palette.Brown, Style.Body).text = "점심";
            lunchTag = tag.gameObject;
            lunchTag.SetActive(false);
            var track = Panel("Day", box, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 18), new Vector2(-48, 12), new Vector2(0.5f, 0), Palette.CreamDark, pill);
            dayFill = Panel("Fill", track, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0, 0.5f), Palette.Mint, pill);
            dayFill.offsetMin = dayFill.offsetMax = Vector2.zero;
            dayFill.anchorMax = new Vector2(0f, 1f);
        }

        void BuildOrders()
        {
            var header = Panel("Orders Header", hud, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-28, -150), new Vector2(TicketWidth, 46), new Vector2(1, 1), Palette.Brown, pill);
            ordersHeader = Txt("Text", header, Vector2.zero, Vector2.one, new Vector2(22, 0), new Vector2(-20, 0), 25, TextAlignmentOptions.Left, Palette.Cream, Style.Title);
            // Legend: [check] 넣음  [cross] 빼기
            var legend = Rect("Legend", header, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-18, 0), new Vector2(170, 30));
            legend.pivot = new Vector2(1, 0.5f);
            legend.anchoredPosition = new Vector2(-18, 0);
            Icon(legend, iconCheck, Palette.Leaf, new Vector2(0, 0));
            Txt("L1", legend, new Vector2(0, 0), new Vector2(0, 1), new Vector2(24, 0), new Vector2(84, 0), 18, TextAlignmentOptions.Left, Palette.Cream, Style.Body).text = "넣음";
            Icon(legend, iconCross, Palette.Red, new Vector2(92, 0));
            Txt("L2", legend, new Vector2(0, 0), new Vector2(0, 1), new Vector2(116, 0), new Vector2(176, 0), 18, TextAlignmentOptions.Left, Palette.Cream, Style.Body).text = "빼기";

            ticketColumn = Rect("Tickets", hud, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-28, -210), new Vector2(TicketWidth, 600));
            ticketColumn.pivot = new Vector2(1, 1);
            ticketColumn.anchoredPosition = new Vector2(-28, -210);
            ordersEmpty = Txt("Empty", ticketColumn, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -70), new Vector2(0, 0), 21, TextAlignmentOptions.Center, Palette.Cream, Style.BodyOutline);
            ordersEmpty.text = "받은 주문이 없어요\n손님을 보고 E로 주문 받기";
        }

        void BuildCenter()
        {
            crosshair = Panel("Crosshair", hud, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12, 12), new Vector2(0.5f, 0.5f), Palette.Brown, pill);
            crosshairDot = Panel("Dot", crosshair, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Palette.Cream, pill).GetComponent<Image>();
            var dr = (RectTransform)crosshairDot.transform;
            dr.offsetMin = new Vector2(3, 3);
            dr.offsetMax = new Vector2(-3, -3);

            var prompt = MakeCard("Prompt", hud, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(300, 62), new Vector2(0.5f, 1f), Palette.Cream, pill);
            promptBox = prompt.rect;
            promptText = Txt("Text", promptBox, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 28, TextAlignmentOptions.Center, Palette.Brown, Style.Body);
            promptText.enableWordWrapping = false;

            var held = MakeCard("Held", hud, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 34), new Vector2(360, 92), new Vector2(0.5f, 0), Palette.Cream);
            heldBox = held.rect;
            heldText = Txt("Text", heldBox, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 27, TextAlignmentOptions.Center, Palette.Brown, Style.Body);
            heldText.enableWordWrapping = false;

            var toast = MakeCard("Toast", hud, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -36), new Vector2(640, 66), new Vector2(0.5f, 1), Palette.Cream, pill);
            toastBox = toast.rect;
            toastGroup = toastBox.gameObject.AddComponent<CanvasGroup>();
            toastGroup.alpha = 0f;
            toastGroup.blocksRaycasts = false;
            toastDot = Panel("Dot", toastBox, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(28, 0), new Vector2(18, 18), new Vector2(0, 0.5f), Palette.Orange, pill).GetComponent<Image>();
            toastText = Txt("Text", toastBox, Vector2.zero, Vector2.one, new Vector2(36, 0), Vector2.zero, 29, TextAlignmentOptions.Center, Palette.Brown, Style.Body);
            toastText.enableWordWrapping = false;

            var hint = Txt("Hint", hud, new Vector2(0, 0), new Vector2(0, 0), new Vector2(28, 18), new Vector2(820, 54), 20, TextAlignmentOptions.Left, Palette.Cream, Style.BodyOutline);
            hint.text = KeyCaps("WASD: 이동   E: 상호작용   R: 거절   Esc: 일시정지");
        }

        void BuildCookBar()
        {
            var card = MakeCard("Cook Bar", hud, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 150), new Vector2(800, 200), new Vector2(0.5f, 0f), Palette.Cream);
            var panel = card.rect;
            cookPanel = panel.gameObject;
            cookTitle = Txt("Title", panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -60), new Vector2(0, -12), 34, TextAlignmentOptions.Center, Palette.Brown, Style.Title);
            var bar = MakeCard("Bar", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(700, 52), new Vector2(0.5f, 0.5f), Palette.CreamDark, pill);
            var inner = (RectTransform)bar.fill.transform;
            cookZone = Panel("Zone", inner, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Palette.Leaf);
            cookPerfect = Panel("Perfect", inner, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Palette.Gold);
            cookMarker = Panel("Marker", inner, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(12, 26), new Vector2(0.5f, 0.5f), Palette.Brown, pill);
            cookHint = Txt("Hint", panel, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 12), new Vector2(0, 62), 25, TextAlignmentOptions.Center, Palette.Brown, Style.Body);
            cookHint.text = KeyCaps("Space: 멈추기") + "    <size=20><color=" + Palette.Html(Palette.BrownSoft) + ">노란 칸에 멈추면 완벽</color></size>";
            cookResult = Txt("Result", panel, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 6), new Vector2(0, 66), 46, TextAlignmentOptions.Center, Palette.Gold, Style.TitleOutline);
            cookPanel.SetActive(false);
        }

        void BuildScreens()
        {
            // ---- Title: the live scene is the backdrop, a warm scrim keeps text readable.
            startScreen = MakeScreen("Title Screen", 0.5f);
            var st = (RectTransform)startScreen.transform;
            var title = Txt("Title", st, new Vector2(0, 0.64f), new Vector2(1, 0.64f), new Vector2(0, -80), new Vector2(0, 80), 118, TextAlignmentOptions.Center, Palette.Cream, Style.TitleOutline);
            title.text = "등록금은 장사로 갚는다";
            startStage = Txt("Stage", st, new Vector2(0, 0.52f), new Vector2(1, 0.52f), new Vector2(0, -30), new Vector2(0, 30), 36, TextAlignmentOptions.Center, Palette.Cream, Style.BodyOutline);
            var how = MakeCard("How To", st, new Vector2(0.5f, 0.31f), new Vector2(0.5f, 0.31f), Vector2.zero, new Vector2(1020, 300), new Vector2(0.5f, 0.5f), Palette.Cream);
            Txt("Text", how.rect, Vector2.zero, Vector2.one, new Vector2(44, 22), new Vector2(-44, -22), 26, TextAlignmentOptions.Left, Palette.Brown, Style.Body).text =
                "1.  손님을 보고 " + Key("E") + " 주문 받기   (" + Key("R") + " 거절)\n" +
                "2.  그릴에서 " + Key("E") + ", 움직이는 막대를 " + Key("Space") + " 로 초록 칸에 멈추기\n" +
                "3.  다 익으면 그릴에서 " + Key("E") + " 로 담기  (오래 두면 타요!)\n" +
                "4.  주문표대로 소스, 마요, 파 넣기  (<color=" + Palette.Html(Palette.Red) + ">빼기</color> 표시는 넣지 말기)\n" +
                "5.  손님을 보고 " + Key("E") + " 로 건네기\n" +
                "6.  잠깐 둘 땐 카운터의 <color=" + Palette.Html(Palette.Mint) + ">초록 접시 자리</color>, 못 쓰는 건 쓰레기통";
            startPrompt = Txt("Prompt", st, new Vector2(0, 0.135f), new Vector2(1, 0.135f), new Vector2(0, -24), new Vector2(0, 24), 30, TextAlignmentOptions.Center, Palette.Cream, Style.BodyOutline);
            startPrompt.text = "Enter 를 눌러 장사 시작";
            Button(st, "장사 시작", new Vector2(0.5f, 0.055f), Vector2.zero, new Vector2(400, 76), () => onStart?.Invoke());
            startScreen.gameObject.SetActive(false);

            // ---- Pause
            pauseScreen = MakeScreen("Pause Screen", 0.45f);
            var pc = MakeCard("Card", (RectTransform)pauseScreen.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500, 450), new Vector2(0.5f, 0.5f), Palette.Cream).rect;
            Txt("Title", pc, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -104), new Vector2(0, -24), 52, TextAlignmentOptions.Center, Palette.Brown, Style.Title).text = "일시정지";
            Button(pc, "계속하기", new Vector2(0.5f, 1f), new Vector2(0, -170), new Vector2(340, 70), () => GameManager.Instance.Resume());
            Button(pc, "처음부터 다시", new Vector2(0.5f, 1f), new Vector2(0, -256), new Vector2(340, 70), () => GameManager.Instance.Retry(), secondary: true);
            Button(pc, "나가기", new Vector2(0.5f, 1f), new Vector2(0, -342), new Vector2(340, 70), () => GameManager.Instance.Quit(), secondary: true);
            pauseScreen.gameObject.SetActive(false);

            // ---- Result
            resultScreen = MakeScreen("Result Screen", 0.55f);
            var rc = MakeCard("Card", (RectTransform)resultScreen.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(780, 740), new Vector2(0.5f, 0.5f), Palette.Cream).rect;
            resultBanner = Rect("Banner", rc, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -72), new Vector2(0, 100));
            resultTitle = Txt("Title", resultBanner, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 72, TextAlignmentOptions.Center, Palette.Leaf, Style.TitleOutline);
            resultSub = Txt("Sub", rc, new Vector2(0, 1), new Vector2(1, 1), new Vector2(80, -176), new Vector2(-80, -130), 25, TextAlignmentOptions.Center, Palette.BrownSoft, Style.Body);
            resultRows = Txt("Rows", rc, new Vector2(0, 1), new Vector2(1, 1), new Vector2(90, -450), new Vector2(-90, -205), 29, TextAlignmentOptions.TopLeft, Palette.BrownSoft, Style.Body);
            resultValues = Txt("Values", rc, new Vector2(0, 1), new Vector2(1, 1), new Vector2(90, -450), new Vector2(-90, -205), 29, TextAlignmentOptions.TopRight, Palette.Brown, Style.Body);
            Panel("Line", rc, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 290), new Vector2(-180, 4), new Vector2(0.5f, 0.5f), Palette.CreamDark, pill);
            Txt("Profit Label", rc, new Vector2(0, 0), new Vector2(1, 0), new Vector2(90, 212), new Vector2(-90, 272), 32, TextAlignmentOptions.Left, Palette.Brown, Style.Title).text = "정산";
            resultProfit = Txt("Profit", rc, new Vector2(0, 0), new Vector2(1, 0), new Vector2(90, 200), new Vector2(-90, 284), 58, TextAlignmentOptions.Right, Palette.Orange, Style.TitleOutline);
            resultNote = Txt("Note", rc, new Vector2(0, 0), new Vector2(1, 0), new Vector2(40, 140), new Vector2(-40, 192), 25, TextAlignmentOptions.Center, Palette.BrownSoft, Style.Body);
            Button(rc, "재도전", new Vector2(0.5f, 0f), new Vector2(-160, 70), new Vector2(280, 74), () => GameManager.Instance.Retry());
            Button(rc, "나가기", new Vector2(0.5f, 0f), new Vector2(160, 70), new Vector2(280, 74), () => GameManager.Instance.Quit(), secondary: true);
            resultScreen.gameObject.SetActive(false);
        }

        Bubble MakeBubble()
        {
            var b = new Bubble();
            b.card = MakeCard("Bubble", bubbleLayer, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(340, 120), new Vector2(0.5f, 0f), Palette.Cream, border: 3f);
            var r = b.card.rect;
            b.strip = Panel("Strip", r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -3), new Vector2(-6, 12), new Vector2(0.5f, 1), Palette.Mint).GetComponent<Image>();
            b.text = Txt("Text", r, Vector2.zero, Vector2.one, new Vector2(14, 26), new Vector2(-14, -18), 24, TextAlignmentOptions.Top, Palette.Brown, Style.Body);
            var bar = Panel("Patience", r, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 12), new Vector2(-30, 9), new Vector2(0.5f, 0), Palette.CreamDark, pill);
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
            toastDot.color = color;
            toastBox.sizeDelta = new Vector2(toastText.GetPreferredValues(message, 2000f, 60f).x + 120f, 66f);
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

        void FloatText(string text, Color color, Vector2 start)
        {
            var t = Txt("Float", fxLayer, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero, 38, TextAlignmentOptions.Left, color, Style.TitleOutline);
            t.text = text;
            t.enableWordWrapping = false;
            var r = (RectTransform)t.transform;
            r.sizeDelta = new Vector2(400, 56);
            r.pivot = new Vector2(0, 0.5f);
            StartCoroutine(FloatRoutine(t, r, start));
        }

        IEnumerator FloatRoutine(TMP_Text t, RectTransform r, Vector2 start)
        {
            yield return Ease.Tween(0.9f, k =>
            {
                if (r == null) return;
                r.anchoredPosition = start + new Vector2(0f, 50f * k);
                t.alpha = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
            }, Ease.OutCubic);
            if (r != null) Destroy(r.gameObject);
        }

        IEnumerator CountUp(TMP_Text text, int value, float seconds)
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

        static Color PatienceColor(float p) => p > 0.5f ? Palette.Leaf : p > 0.25f ? Palette.Gold : Palette.Red;

        static Color GradeColor(CookGrade g) => g == CookGrade.Perfect ? Palette.Gold : g == CookGrade.Good ? Palette.Leaf : Palette.Red;

        static string Key(string k) => "<color=" + Palette.Html(Palette.Orange) + ">[" + k + "]</color>";

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

        /// <summary>
        /// A signboard card: soft brown drop shadow, brown outline, coloured fill. Content goes on <see cref="Card.rect"/>,
        /// drawn above the fill.
        /// </summary>
        Card MakeCard(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, Vector2 pivot, Color fill, Sprite sprite = null, float border = Border)
        {
            var c = new Card();
            c.rect = Rect(name, parent, anchorMin, anchorMax, pos, size);
            c.rect.pivot = pivot;
            c.rect.anchoredPosition = pos;
            Sprite s = sprite != null ? sprite : round;

            var shadow = Panel("Shadow", c.rect, Vector2.zero, Vector2.one, new Vector2(0, -5), Vector2.zero, new Vector2(0.5f, 0.5f), Palette.WithAlpha(Palette.Brown, 0.3f), s);
            shadow.offsetMin = new Vector2(0, -6);
            shadow.offsetMax = new Vector2(0, -6);
            var b = Panel("Border", c.rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Palette.Brown, s);
            b.offsetMin = b.offsetMax = Vector2.zero;
            c.border = b.GetComponent<Image>();
            var f = Panel("Fill", c.rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), fill, s);
            f.offsetMin = new Vector2(border, border);
            f.offsetMax = new Vector2(-border, -border);
            c.fill = f.GetComponent<Image>();
            return c;
        }

        void Icon(Transform parent, Sprite sprite, Color color, Vector2 pos)
        {
            var r = Panel("Icon", parent, new Vector2(0, 0.5f), new Vector2(0, 0.5f), pos, new Vector2(20, 20), new Vector2(0, 0.5f), color, sprite);
            r.GetComponent<Image>().type = Image.Type.Simple;
        }

        /// <summary>TextMeshPro text stretched inside its parent between anchorMin/anchorMax, inset by offsetMin/offsetMax.</summary>
        TMP_Text Txt(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, float size, TextAlignmentOptions align, Color color, Style style)
        {
            var r = Rect(name, parent, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
            r.offsetMin = offsetMin;
            r.offsetMax = offsetMax;
            var t = r.gameObject.AddComponent<TextMeshProUGUI>();
            bool title = style == Style.Title || style == Style.TitleOutline;
            TMP_FontAsset fa = title ? titleFont : bodyFont;
            if (fa != null) t.font = fa;
            Material outline = title ? titleOutline : bodyOutline;
            if ((style == Style.BodyOutline || style == Style.TitleOutline) && outline != null) t.fontSharedMaterial = outline;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.richText = true;
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }

        CanvasGroup MakeScreen(string name, float scrimAlpha)
        {
            var r = Stretch(name, root);
            var img = r.gameObject.AddComponent<Image>();
            img.color = Palette.WithAlpha(Palette.Brown, scrimAlpha);
            img.raycastTarget = true;
            var g = r.gameObject.AddComponent<CanvasGroup>();
            g.alpha = 0f;
            return g;
        }

        void Button(RectTransform parent, string label, Vector2 anchor, Vector2 pos, Vector2 size, Action onClick, bool secondary = false)
        {
            var c = MakeCard(label, parent, anchor, anchor, pos, size, new Vector2(0.5f, 0.5f), secondary ? Palette.Cream : Palette.Orange, pill);
            c.fill.raycastTarget = true;
            var b = c.rect.gameObject.AddComponent<Button>();
            b.targetGraphic = c.fill;
            var colors = b.colors;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            colors.fadeDuration = 0.08f;
            b.colors = colors;
            b.onClick.AddListener(() => onClick());
            var t = Txt("Label", c.rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 32, TextAlignmentOptions.Center,
                        secondary ? Palette.Brown : Palette.Cream, secondary ? Style.Title : Style.TitleOutline);
            t.text = label;
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
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        /// <summary>A white check mark or cross drawn with thick anti-aliased strokes (the UI fonts have no such glyphs).</summary>
        static Sprite MakeIcon(bool check)
        {
            const int size = 64;
            const float half = 5f;
            Vector2[][] strokes = check
                ? new[] { new[] { new Vector2(12, 34), new Vector2(26, 19) }, new[] { new Vector2(26, 19), new Vector2(52, 47) } }
                : new[] { new[] { new Vector2(16, 16), new Vector2(48, 48) }, new[] { new Vector2(16, 48), new Vector2(48, 16) } };
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = check ? "IconCheck" : "IconCross" };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float d = float.MaxValue;
                foreach (var s in strokes) d = Mathf.Min(d, SegmentDistance(p, s[0], s[1]));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(half - d + 0.5f) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
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
