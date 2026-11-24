using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;

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

            // 0. The front counter (toppings, name tags, plate spots), seen from inside the truck, before any UI exists.
            {
                var targets = new List<Transform>();
                foreach (var s in Object.FindObjectsOfType<ToppingStation>()) targets.Add(s.transform);
                foreach (var s in Object.FindObjectsOfType<HoldingSpot>()) targets.Add(s.transform);
                if (targets.Count > 0)
                {
                    Vector3 c = Vector3.zero;
                    foreach (var t in targets) c += t.position;
                    c /= targets.Count;
                    Vector3 p0 = cam.transform.position;
                    Quaternion r0 = cam.transform.rotation;
                    cam.transform.position = c + new Vector3(0f, 0.75f, 1.5f);
                    cam.transform.LookAt(c);
                    foreach (var l in Object.FindObjectsOfType<WorldLabel>())
                        l.transform.rotation = Quaternion.LookRotation(l.transform.position - cam.transform.position, Vector3.up);
                    Shot(cam, outDir + "/0_counter.png");
                    cam.transform.SetPositionAndRotation(p0, r0);
                }
            }

            // 0b. The grill and its bowls.
            {
                var grill = Object.FindObjectOfType<GrillStation>();
                if (grill != null)
                {
                    Vector3 c = grill.transform.position;
                    Vector3 p0 = cam.transform.position;
                    Quaternion r0 = cam.transform.rotation;
                    cam.transform.position = c + new Vector3(-0.5f, 0.8f, -1.6f);
                    cam.transform.LookAt(c + new Vector3(-0.5f, 0f, 0f));
                    Shot(cam, outDir + "/0b_grill.png");

                    // Effects: run the grill's own Start (creates its steam / smoke), then simulate them for a moment.
                    typeof(GrillStation).GetMethod("Start", Any).Invoke(grill, null);
                    var steam = (ParticleSystem)typeof(GrillStation).GetField("steam", Any).GetValue(grill);
                    var smoke = (ParticleSystem)typeof(GrillStation).GetField("smoke", Any).GetValue(grill);
                    Vfx.SetColumn(steam, true, 6f, Vfx.SteamColor);
                    steam.Simulate(2.5f, true, true);
                    Shot(cam, outDir + "/0c_steam.png");
                    steam.Clear();
                    Vfx.SetColumn(steam, false, 0f, Vfx.SteamColor);
                    Vfx.SetColumn(smoke, true, 10f, Vfx.BurntSmokeColor);
                    smoke.Simulate(2.5f, true, true);
                    Shot(cam, outDir + "/0d_smoke.png");
                    smoke.Clear();
                    cam.transform.SetPositionAndRotation(p0, r0);
                }
            }

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
            GetField<TMP_Text>(ui, "resultProfit").text = Names.Won(68000);
            Shot(cam, outDir + "/5_result.png");

            Debug.Log("[Chef24] UI preview written to " + Path.GetFullPath(outDir));
            // Reload the saved game scene (drops the preview-only objects) so the editor reopens on it next time.
            EditorSceneManager.OpenScene("Assets/Chef24MVP/Chef24MVP.unity");
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
            foreach (var t in Object.FindObjectsOfType<TMP_Text>()) t.ForceMeshUpdate();
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
