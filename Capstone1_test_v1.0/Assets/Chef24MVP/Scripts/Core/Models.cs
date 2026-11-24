using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Chef24
{
    public enum Topping { Sauce, Mayo, GreenOnion }

    public enum CookGrade { Perfect, Good, Bad, Burnt }

    public static class Names
    {
        public static readonly Topping[] AllToppings = { Topping.Sauce, Topping.Mayo, Topping.GreenOnion };

        /// <summary>Short name for order chips, bubbles and the held dish ("타코 소스", "마요", "파").</summary>
        public static string Of(Topping t)
        {
            switch (t)
            {
                case Topping.Sauce: return "타코 소스";
                case Topping.Mayo: return "마요";
                default: return "파";
            }
        }

        /// <summary>Full name for name tags and prompts ("타코야끼 소스", "마요네즈", "파").</summary>
        public static string Full(Topping t)
        {
            switch (t)
            {
                case Topping.Sauce: return "타코야끼 소스";
                case Topping.Mayo: return "마요네즈";
                default: return "파";
            }
        }

        public static string Of(CookGrade g)
        {
            switch (g)
            {
                case CookGrade.Perfect: return "완벽";
                case CookGrade.Good: return "좋음";
                case CookGrade.Bad: return "아쉬움";
                default: return "탐";
            }
        }

        /// <summary>Price multiplier for how well it was cooked.</summary>
        public static float PriceMultiplier(CookGrade g)
        {
            switch (g)
            {
                case CookGrade.Perfect: return 1.2f;
                case CookGrade.Good: return 1f;
                case CookGrade.Bad: return 0.7f;
                default: return 0f;
            }
        }

        public static string Won(int amount) => amount.ToString("N0", CultureInfo.InvariantCulture) + "원";
    }

    /// <summary>
    /// One menu. MVP only has takoyaki; the other three menus are added as more MenuData entries later (DAR-003).
    /// </summary>
    [Serializable]
    public class MenuData
    {
        public string name = "타코야끼";
        public int basePrice = 6000;
        public Topping[] toppings = { Topping.Sauce, Topping.Mayo, Topping.GreenOnion };
    }

    /// <summary>Per customer type tuning (SFR-007, 개발계획서 핵심기능 3).</summary>
    [Serializable]
    public class CustomerTypeData
    {
        public string name = "학생";
        public Color color = Color.white;
        [Tooltip("Seconds the customer waits for you to take the order.")]
        public float orderPatience = 25f;
        [Tooltip("Seconds the customer waits for the food after you took the order.")]
        public float foodPatience = 60f;
        public float payMultiplier = 1f;
        [Tooltip("Pays only half when the takoyaki was cooked badly (picky).")]
        public bool picky;
        public float spawnWeight = 1f;
        [Tooltip("Spawn weight is multiplied by this around lunch time (11:30 - 13:30).")]
        public float lunchWeightMultiplier = 1f;
        [Range(0, 3)] public int maxExclusions = 1;
    }

    /// <summary>A cooked dish the player is carrying.</summary>
    public class Dish
    {
        public MenuData menu;
        public CookGrade grade;
        public readonly HashSet<Topping> toppings = new HashSet<Topping>();

        public string Describe()
        {
            string t = toppings.Count == 0 ? "토핑 없음" : string.Join(", ", Names.AllToppings.Where(toppings.Contains).Select(Names.Of));
            return menu.name + "(" + Names.Of(grade) + ") + " + t;
        }
    }

    /// <summary>One customer's order: a menu, with some toppings excluded on request.</summary>
    public class Order
    {
        public MenuData menu;
        public Customer customer;
        public readonly HashSet<Topping> excluded = new HashSet<Topping>();
        public bool accepted;

        public IEnumerable<Topping> Required => menu.toppings.Where(t => !excluded.Contains(t));

        public bool Matches(Dish dish)
        {
            if (dish == null || dish.menu != menu || dish.grade == CookGrade.Burnt) return false;
            return dish.toppings.SetEquals(Required);
        }

        /// <summary>"파 빼주세요" style request text.</summary>
        public string RequestText()
        {
            if (excluded.Count == 0) return "다 넣어 주세요";
            return string.Join(", ", menu.toppings.Where(excluded.Contains).Select(Names.Of)) + " 빼주세요";
        }

        /// <summary>Rich text for the order list: needed toppings, excluded ones in red.</summary>
        public string ToppingsRichText()
        {
            return string.Join(",  ", menu.toppings.Select(t =>
                excluded.Contains(t) ? "<color=" + Palette.Html(Palette.Red) + ">" + Names.Of(t) + " 빼기</color>" : Names.Of(t)));
        }
    }
}
