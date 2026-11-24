using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chef24
{
    public enum GameState { Ready, Playing, Paused, Result }

    /// <summary>
    /// One business day of one stage: money, accepted orders, start / pause / end of day
    /// (AP-006 시간 및 영업, AP-007 정산, AP-010 결과 처리 - MVP version, no DB yet).
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Stage (MVP: 기숙사 1일차)")]
        public string stageName = "기숙사";
        [Tooltip("문서 기준은 300만원이지만 메뉴 가격과 맞지 않아 MVP에서는 임시 값을 씀.")]
        public int targetAmount = 60000;
        public int wasteCost = 2000;
        public MenuData menu = new MenuData();

        [Header("References")]
        public DayClock clock;
        public PlayerController player;
        public PlayerHands hands;
        public CustomerSpawner spawner;

        [Tooltip("After closing time, the day ends when every customer has left, or after this many seconds.")]
        public float closingGraceSeconds = 25f;

        public GameState State { get; private set; } = GameState.Ready;
        public int Sales { get; private set; }
        public int Waste { get; private set; }
        public int Profit => Sales - Waste;
        public int SoldCount { get; private set; }
        public int LostCustomers { get; private set; }
        public readonly List<Order> AcceptedOrders = new List<Order>();

        float closedAt = -1f;

        void Awake()
        {
            Instance = this;
            Time.timeScale = 1f;
        }

        void Start()
        {
            if (clock != null) clock.Closed += OnClosed;
            player?.SetLocked(true);
            UIManager.Instance?.ShowStart(stageName, targetAmount, StartDay);
        }

        public void StartDay()
        {
            if (State != GameState.Ready) return;
            State = GameState.Playing;
            clock?.StartDay();
            player?.SetLocked(false);
            UIManager.Instance?.HideStart();
            UIManager.Instance?.Toast(stageName + " 영업 시작! 손님을 맞이하세요.", Palette.Amber);
        }

        void Update()
        {
            UIManager.Instance?.SetMoney(Profit, targetAmount, Waste);
            if (clock != null) UIManager.Instance?.SetClock(clock.TimeText(), clock.IsOpen);
            UIManager.Instance?.SetOrders(AcceptedOrders);

            if (State == GameState.Ready && GameInput.ConfirmDown()) StartDay();

            if (GameInput.PauseDown())
            {
                if (State == GameState.Playing) Pause();
                else if (State == GameState.Paused) Resume();
            }

            if (State == GameState.Playing && closedAt >= 0f)
            {
                bool nobodyLeft = spawner == null || spawner.ActiveCount == 0;
                if (nobodyLeft || Time.time - closedAt > closingGraceSeconds) EndDay();
            }
        }

        void OnClosed()
        {
            closedAt = Time.time;
            spawner?.StopSpawning();
            UIManager.Instance?.Toast("오후 7시, 영업 종료! 남은 손님만 응대하세요.", Palette.Amber);
        }

        // ---------------------------------------------------------------- money

        public void AddSale(int amount, string message)
        {
            Sales += amount;
            SoldCount++;
            UIManager.Instance?.Toast(message + "  +" + Names.Won(amount), Palette.Leaf);
        }

        public void AddWaste(string reason)
        {
            Waste += wasteCost;
            UIManager.Instance?.Toast(reason + "  폐기 -" + Names.Won(wasteCost), Palette.Tomato);
        }

        public void CustomerLost(Customer c, string message)
        {
            LostCustomers++;
            UIManager.Instance?.Toast(message, Palette.Amber);
        }

        public void RemoveOrder(Order o) => AcceptedOrders.Remove(o);

        // ---------------------------------------------------------------- flow

        public void Pause()
        {
            State = GameState.Paused;
            Time.timeScale = 0f;
            player?.SetLocked(true);
            UIManager.Instance?.ShowPause(true);
        }

        public void Resume()
        {
            State = GameState.Playing;
            Time.timeScale = 1f;
            player?.SetLocked(false);
            UIManager.Instance?.ShowPause(false);
        }

        void EndDay()
        {
            State = GameState.Result;
            spawner?.StopSpawning();
            player?.SetLocked(true);
            bool success = Profit >= targetAmount;
            UIManager.Instance?.ShowResult(success, stageName, Sales, Waste, Profit, targetAmount, SoldCount, LostCustomers);
        }

        public void Retry()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0
                ? SceneManager.GetActiveScene().name
                : SceneManager.GetActiveScene().path);
        }

        public void Quit()
        {
            Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
