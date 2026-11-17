using System.Linq;
using UnityEngine;

namespace Chef24
{
    /// <summary>
    /// One customer (AP-003): walks to the window, orders, waits, then leaves.
    /// E on the customer = take the order / hand over the food, R = reject the order.
    /// </summary>
    public class Customer : MonoBehaviour, IInteractable
    {
        enum State { Arriving, WaitingOrder, WaitingFood, Leaving }

        public CustomerTypeData type;
        public float walkSpeed = 1.4f;

        public Order Order { get; private set; }
        public float Patience01 => patienceMax > 0f ? Mathf.Clamp01(patience / patienceMax) : 0f;
        public bool IsLeaving => state == State.Leaving;

        CustomerSpawner spawner;
        int slot;
        Vector3 target, faceDirection, exitPoint;
        Animator animator;
        bool hasSpeedParam;
        State state;
        float patience, patienceMax;
        string mood;
        float moodUntil;

        GameManager GM => GameManager.Instance;

        public void Init(CustomerSpawner owner, int slotIndex, CustomerTypeData data, Vector3 slotPos, Vector3 face, Vector3 exit)
        {
            spawner = owner;
            slot = slotIndex;
            type = data;
            target = slotPos;
            faceDirection = face;
            exitPoint = exit;
            state = State.Arriving;
            animator = GetComponentInChildren<Animator>();
            if (animator != null && animator.runtimeAnimatorController != null)
                hasSpeedParam = animator.parameters.Any(p => p.name == "Speed" && p.type == AnimatorControllerParameterType.Float);
        }

        void Update()
        {
            if (Time.timeScale == 0f) return;
            switch (state)
            {
                case State.Arriving:
                    if (WalkTo(target))
                    {
                        transform.rotation = Quaternion.LookRotation(faceDirection, Vector3.up);
                        MakeOrder();
                    }
                    break;
                case State.WaitingOrder:
                case State.WaitingFood:
                    SetSpeed(0f);
                    patience -= Time.deltaTime;
                    if (patience <= 0f)
                    {
                        GM.CustomerLost(this, "[" + type.name + "] 손님이 기다리다 떠났어요");
                        Say("너무 오래 걸리네...", 3f);
                        Leave();
                    }
                    break;
                case State.Leaving:
                    if (WalkTo(exitPoint)) Destroy(gameObject);
                    break;
            }
            UIManager.Instance?.SetBubble(this, BubbleText(), type.color,
                state == State.WaitingOrder || state == State.WaitingFood ? Patience01 : -1f);
        }

        void OnDestroy()
        {
            UIManager.Instance?.RemoveBubble(this);
            if (Order != null) GM?.RemoveOrder(Order);
            spawner?.FreeSlot(slot, this);
        }

        // ---------------------------------------------------------------- order flow

        void MakeOrder()
        {
            Order = new Order { menu = GM.menu, customer = this };
            int exclusions = Random.Range(0, type.maxExclusions + 1);
            var pool = GM.menu.toppings.OrderBy(_ => Random.value).Take(exclusions);
            foreach (var t in pool) Order.excluded.Add(t);
            patienceMax = patience = type.orderPatience;
            state = State.WaitingOrder;
        }

        void Accept()
        {
            Order.accepted = true;
            GM.AcceptedOrders.Add(Order);
            patienceMax = patience = type.foodPatience;
            state = State.WaitingFood;
            Say("부탁해요!", 1.5f);
        }

        void Reject()
        {
            Say("아... 알겠어요.", 2f);
            UIManager.Instance?.Toast("주문 거절", new Color(1f, 0.8f, 0.5f));
            Leave();
        }

        void Serve(PlayerHands hands)
        {
            Dish dish = hands.Release();
            if (Order.Matches(dish))
            {
                float mult = Names.PriceMultiplier(dish.grade) * type.payMultiplier;
                if (type.picky && dish.grade == CookGrade.Bad) mult *= 0.5f;
                int pay = Mathf.RoundToInt(Order.menu.basePrice * mult / 100f) * 100;
                string reaction = dish.grade == CookGrade.Perfect ? "와, 완벽해요!" : dish.grade == CookGrade.Good ? "맛있겠다!" : "음... 그럭저럭이네요.";
                Say(reaction, 2.5f);
                GM.AddSale(pay, "판매 성공 [" + type.name + "]");
            }
            else
            {
                Say("이거 제가 주문한 거 아닌데요!", 2.5f);
                GM.AddWaste("주문과 달라서 판매 실패");
            }
            Leave();
        }

        void Leave()
        {
            if (Order != null)
            {
                GM.RemoveOrder(Order);
                Order = null;
            }
            state = State.Leaving;
            spawner?.FreeSlot(slot, this);
        }

        // ---------------------------------------------------------------- IInteractable

        public string GetPrompt(PlayerHands hands)
        {
            switch (state)
            {
                case State.WaitingOrder: return "E: 주문 받기";
                case State.WaitingFood: return hands.IsEmpty ? "[" + type.name + "] 음식을 기다리는 중" : "E: 음식 건네기";
                default: return null;
            }
        }

        public string GetAltPrompt(PlayerHands hands) => state == State.WaitingOrder ? "R: 거절" : null;

        public void Interact(PlayerHands hands)
        {
            if (state == State.WaitingOrder) Accept();
            else if (state == State.WaitingFood && !hands.IsEmpty) Serve(hands);
        }

        public void InteractAlt(PlayerHands hands)
        {
            if (state == State.WaitingOrder) Reject();
        }

        // ---------------------------------------------------------------- helpers

        string BubbleText()
        {
            string head = "<b>[" + type.name + "]</b>";
            if (Time.time < moodUntil) return head + "\n" + mood;
            switch (state)
            {
                case State.WaitingOrder: return head + "\n" + Order.menu.name + " 1인분 주세요\n" + Order.RequestText();
                case State.WaitingFood: return head + "\n" + Order.menu.name + " · " + Order.ToppingsRichText();
                default: return null;
            }
        }

        void Say(string text, float seconds)
        {
            mood = text;
            moodUntil = Time.time + seconds;
        }

        bool WalkTo(Vector3 dest)
        {
            Vector3 to = dest - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist < 0.05f)
            {
                SetSpeed(0f);
                return true;
            }
            Vector3 step = to.normalized * Mathf.Min(dist, walkSpeed * Time.deltaTime);
            transform.position += step;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to, Vector3.up), 540f * Time.deltaTime);
            SetSpeed(walkSpeed);
            return false;
        }

        void SetSpeed(float s)
        {
            if (hasSpeedParam) animator.SetFloat("Speed", s, 0.1f, Time.deltaTime);
        }
    }
}
