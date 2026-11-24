using UnityEngine;

namespace Chef24
{
    /// <summary>
    /// Art bible (single colour source for the UI).
    /// Style: warm, friendly food-truck street stall. Rounded cards, receipt-paper order tickets, chunky readable text.
    /// Palette: taken from the truck itself (mint body, cream interior, red/white awning, warm lamp light).
    /// Mood: late afternoon street, warm lamp inside the truck, cool shade outside.
    /// </summary>
    public static class Palette
    {
        public static readonly Color Ink = Hex(0x1F2430);        // panels, scrims (never pure black)
        public static readonly Color InkSoft = Hex(0x2E3442);    // bar tracks, inner panels
        public static readonly Color Paper = Hex(0xFFF4DE);      // order tickets, speech bubbles
        public static readonly Color PaperDim = Hex(0xE9DCC2);   // empty chips on paper
        public static readonly Color TextDark = Hex(0x2B2320);   // text on paper
        public static readonly Color TextLight = Hex(0xFFF8EC);  // text on ink
        public static readonly Color Muted = Hex(0x9A8F86);      // secondary text
        public static readonly Color Mint = Hex(0x5CC8B0);       // truck body: neutral highlight
        public static readonly Color Amber = Hex(0xF5B83D);      // accent: money, keys, perfect
        public static readonly Color Leaf = Hex(0x7BCB5A);       // success, good, topping done
        public static readonly Color Tomato = Hex(0xE8553D);     // awning red: danger, excluded, waste

        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        public static string Html(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
