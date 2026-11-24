using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Chef24.EditorTools
{
    /// <summary>
    /// Batch-mode helper: rebuilds the MVP scene, then renders the main UI states (title, HUD with orders,
    /// cooking bar, result) from the player camera into PNGs so the layout can be checked without playing.
    /// Usage: Unity -batchmode -projectPath ... -executeMethod Chef24.EditorTools.Chef24UiPreview.Run -previewOut DIR -quit
    /// Bubble positions are approximate here (the canvas is switched to camera space for capturing).
    /// </summary>
    public static class Chef24UiPreview
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        public static void Run()
        {
            string outDir = ArgAfter("-previewOut") ?? "Temp/UiPreview";
            Directory.CreateDirectory(outDir);

            Chef24MvpSceneBuilder.Build();
            EditorSceneManager.OpenScene("Assets/Chef24MVP/Chef24MVP.unity");

            var ui = Object.FindObjectOfType<UIManager>();
            var cam = GameObject.Find("Player Camera").GetComponent<Camera>();
            typeof(UIManager).GetMethod("Awake", Any).Invoke(ui, null);
            var canvas = ui.GetComponentInChildren<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 0.2f;

            // 1. Title
            ui.ShowStart("기숙사", 60000, null);
            ShowScreen(ui, "startScreen", true);
            Shot(cam, outDir + "/1_title.png");
            ShowScreen(ui, "startScreen", false);
            GetField<CanvasGroup>(ui, "hudGroup").alpha = 1f; // HideStart fades this in during play

            // 2. HUD with orders, bubbles, prompt, held dish, toast
            var spawner = Object.FindObjectOfType<CustomerSpawner>();
            var menu = new MenuData();
            var orders = new List<Order>();
            for (int i = 0; i < 3; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.transform.position = spawner.slots[i].position + Vector3.up;
                var c = go.AddComponent<Customer>();
                c.type = spawner.types[i];
                if (i == 2) continue; // third customer is still waiting to order (bubble only)
                var o = new Order { menu = menu, customer = c, accepted = true };
                if (i == 0) o.excluded.Add(Topping.GreenOnion);
                if (i == 1) { o.excluded.Add(Topping.Mayo); o.excluded.Add(Topping.Sauce); }
                orders.Add(o);
            }
            ui.SetMoney(18000, 60000, 2000);
            ui.SetMoney(24000, 60000, 2000);
            ui.SetClock("오후 12:10", true);
            ui.SetOrders(orders);
            foreach (Transform t in ((RectTransform)GetField<RectTransform>(ui, "ticketColumn")))
                if (t.name == "Ticket") ((RectTransform)t).anchoredPosition = new Vector2(0f, ((RectTransform)t).anchoredPosition.y);
            ui.SetPrompt("E: 건네기     R: 거절");
            ui.SetHeld("타코야끼(완벽) + 소스, 마요");
            ui.Toast("타코야끼 완성! (완벽)", Palette.Mint);
            GetField<CanvasGroup>(ui, "toastGroup").alpha = 1f;
            foreach (var o in orders) ui.SetBubble(o.customer, "<b>[" + o.customer.type.name + "]</b>\n타코야끼 · " + o.ToppingsRichText(), o.customer.type.color, 0.6f);
            Shot(cam, outDir + "/2_hud.png");

            // 3. Cooking bar
            ui.ShowCookBar("굽고 뒤집기", 0.62f, 0.55f, 0.28f, null);
            Shot(cam, outDir + "/3_cook.png");
            ui.ShowCookBar("굽고 뒤집기", 0.55f, 0.55f, 0.28f, CookGrade.Perfect);
            Shot(cam, outDir + "/4_cook_result.png");
            ui.HideCookBar();

            // 4. Result
            ui.ShowResult(true, "기숙사", 72000, 4000, 68000, 60000, 12, 2);
            ShowScreen(ui, "resultScreen", true);
            GetField<Text>(ui, "resultProfit").text = Names.Won(68000);
            Shot(cam, outDir + "/5_result.png");

            Debug.Log("[Chef24] UI preview written to " + Path.GetFullPath(outDir));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static void ShowScreen(UIManager ui, string field, bool show)
        {
            var g = GetField<CanvasGroup>(ui, field);
            g.gameObject.SetActive(show);
            g.alpha = show ? 1f : 0f;
        }

        static T GetField<T>(object o, string name) => (T)o.GetType().GetField(name, Any).GetValue(o);

        static void Shot(Camera cam, string path)
        {
            var rt = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }

        static string ArgAfter(string flag)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, flag);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
