using UnityEngine;

namespace Chef24
{
    /// <summary>
    /// Art bible (single colour source for the UI), following the team's title screen mock-up (글꼴테스트).
    /// Style: cozy food-truck signboard. Cream cards with thick brown outlines, orange pill buttons,
    ///        chunky rounded Korean lettering (Black Han Sans titles, Jua body).
    /// Palette: cream / chocolate brown / takoyaki orange, with leaf green, gold and awning red as signals.
    /// Mood: sunny campus afternoon, warm lamp inside the truck.
    /// </summary>
    public static class Palette
    {
        public static readonly Color Cream = Hex(0xFFF6DF);      // cards, bubbles, title lettering
        public static readonly Color CreamDark = Hex(0xF0E2C2);  // empty chips, bar tracks on cream
        public static readonly Color Brown = Hex(0x4A2A17);      // outlines, borders, text on cream
        public static readonly Color BrownSoft = Hex(0x8A6A52);  // secondary text on cream
        public static readonly Color Orange = Hex(0xFF9F40);     // primary buttons, money
        public static readonly Color Gold = Hex(0xFFC93C);       // perfect, highlight, keys
        public static readonly Color Leaf = Hex(0x6DBE4B);       // success, done, open
        public static readonly Color Red = Hex(0xE0503F);        // danger, excluded, waste
        public static readonly Color Mint = Hex(0x5CC8B0);       // truck body: neutral info

        // Older names still used by gameplay toasts.
        public static Color Amber => Orange;
        public static Color Tomato => Red;

        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        public static string Html(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
