using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Chef24.EditorTools
{
    /// <summary>
    /// Menu: Tools > Chef24 > Build MVP Scene
    /// Builds Assets/Chef24MVP/Chef24MVP.unity: the food truck, a takoyaki station (grill, toppings, trash),
    /// customer points outside the window, the first person player and all game systems, all wired up.
    /// Needs in Assets: foodtruck_basic (.glb/.fbx) and the takoyaki FBX models. A .vrm is used for customers if present.
    /// </summary>
    public static class Chef24MvpSceneBuilder
    {
        const string Root = "Assets/Chef24MVP";
        const string ScenePath = Root + "/Chef24MVP.unity";
        const string MaterialFolder = Root + "/Materials";
        const string CustomerAnimatorPath = "Assets/FoodTruckTest/Animations/FoodTruckAvatar.controller";
        /// <summary>World position of the trash bin (x, z used; it stands on the floor).</summary>
        static readonly Vector3 TrashPosition = new Vector3(0.4f, 0.31f, 0.9f);

        [MenuItem("Tools/Chef24/Build MVP Scene")]
        public static void Build()
        {
            GameObject truckAsset = FindModel("foodtruck_basic");
            string[] needed = { "Takoyaki_Grill", "Takoyaki_Grill_Cooking", "Takoyaki_Boat", "Takoyaki_Sauce_Bottle", "Mayo_Bottle", "GreenOnion_Bowl" };
            string[] missing = needed.Where(n => FindModel(n) == null).ToArray();
            if (truckAsset == null || missing.Length > 0)
            {
                EditorUtility.DisplayDialog("Chef24",
                    "필요한 모델을 Assets에서 찾지 못했어요.\n\n" + (truckAsset == null ? "foodtruck_basic\n" : "") + string.Join("\n", missing) +
                    "\n\nFoodTruck_Models/foodtruck_basic.glb 와 Takoyaki_Models/fbx 폴더를 Assets에 넣어 주세요.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder("Assets", "Chef24MVP");
            EnsureFolder(Root, "Materials");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var defaultCam = GameObject.FindObjectsOfType<Camera>().FirstOrDefault();
            if (defaultCam != null) UnityEngine.Object.DestroyImmediate(defaultCam.gameObject);
            Light sun = GameObject.FindObjectsOfType<Light>().FirstOrDefault(l => l.type == LightType.Directional);

            // ---------------------------------------------------------------- truck + ground
            var truck = (GameObject)PrefabUtility.InstantiatePrefab(truckAsset);
            truck.name = "FoodTruck";
            truck.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var mf in truck.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            foreach (var a in truck.GetComponentsInChildren<Animation>(true)) { a.playAutomatically = false; a.enabled = false; }
            foreach (var a in truck.GetComponentsInChildren<Animator>(true)) a.enabled = false;

            Bounds all = RendererBounds(truck.transform);
            Vector3 roomCenter = all.center;
            float floorY = PartBounds(truck, "Floor", out Bounds fb) ? fb.max.y : all.min.y;
            Vector3 frontDir = PartBounds(truck, "Front_Counter", out Bounds fcb) ? Flat(fcb.center - roomCenter).normalized : Vector3.back;
            // The counter is not centred on the truck, so snap to the nearest axis (the window wall is axis aligned).
            frontDir = Mathf.Abs(frontDir.x) > Mathf.Abs(frontDir.z)
                ? new Vector3(Mathf.Sign(frontDir.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(frontDir.z));

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = new Vector3(roomCenter.x, floorY - 0.03f, roomCenter.z);
            ground.transform.localScale = new Vector3(12f, 1f, 12f);
            ground.GetComponent<Renderer>().sharedMaterial = MakeMaterial("Ground", new Color(0.55f, 0.62f, 0.45f));

            BuildCeilingAndLights(truck, all, floorY);
            if (sun != null)
            {
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.45f;
            }

            // ---------------------------------------------------------------- stations
            var fonts = Chef24Fonts.Ensure();
            var stations = new GameObject("Stations").transform;
            var game = new GameObject("Game");
            var miniGame = game.AddComponent<TimingMiniGame>();

            if (PartBounds(truck, "Stove_Cabinet", out Bounds stove))
            {
                GameObject grill = Place("Takoyaki_Grill", stove, 0.45f, roomCenter, null);
                GameObject cooking = Place("Takoyaki_Grill_Cooking", stove, 0.45f, roomCenter, null);
                cooking.transform.SetPositionAndRotation(grill.transform.position, grill.transform.rotation);
                var station = MakeStation("Grill Station", stations, grill, cooking);
                var gs = station.AddComponent<GrillStation>();
                gs.emptyVisual = grill;
                gs.cookingVisual = cooking;
                gs.miniGame = miniGame;
                Place("Batter_Bowl", stove, 0.2f, roomCenter, stations, BowlScale);
                Place("Octopus_Bowl", stove, 0.3f, roomCenter, stations, BowlScale);
            }

            {
                var bin = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bin.name = "Trash Bin";
                bin.transform.SetParent(stations, false);
                bin.transform.localScale = new Vector3(0.38f, 0.6f, 0.38f);
                // Position chosen by hand in the editor during play testing (2026-11-24).
                bin.transform.position = new Vector3(TrashPosition.x, floorY + 0.3f, TrashPosition.z);
                bin.GetComponent<Renderer>().sharedMaterial = MakeMaterial("Trash", new Color(0.22f, 0.24f, 0.27f));
                bin.AddComponent<TrashBin>();
            }

            // Plate spots on the front counter: put a dish down / pick it up / swap.
            if (PartBounds(truck, "Front_Counter", out Bounds counter))
            {
                Material spotMat = MakeMaterial("Holding Spot", new Color(0.62f, 0.86f, 0.78f));
                GameObject boat = FindModel("Takoyaki_Boat");
                // Toppings first (left of the service window), then the plate spots in front of the window.
                (string model, Topping topping, float t, float scale)[] tops =
                {
                    ("Takoyaki_Sauce_Bottle", Topping.Sauce, 0.08f, 1f),
                    ("Mayo_Bottle", Topping.Mayo, 0.2f, 1f),
                    ("GreenOnion_Bowl", Topping.GreenOnion, 0.33f, BowlScale),
                };
                foreach (var (model, topping, t, scale) in tops)
                {
                    GameObject go = Place(model, counter, t, roomCenter, null, scale);
                    var station = MakeStation(Names.Of(topping) + " Station", stations, go);
                    station.AddComponent<ToppingStation>().topping = topping;
                    AddLabel(station, Names.Full(topping), fonts);
                }

                float[] along = { 0.55f, 0.66f };
                for (int i = 0; i < along.Length; i++)
                {
                    var spot = new GameObject("Holding Spot " + (i + 1));
                    spot.transform.SetParent(stations, false);
                    spot.transform.position = SurfacePoint(counter, along[i]);
                    var box = spot.AddComponent<BoxCollider>();
                    box.isTrigger = true;
                    box.center = new Vector3(0f, 0.15f, 0f);
                    box.size = new Vector3(0.45f, 0.3f, 0.45f);
                    var hs = spot.AddComponent<HoldingSpot>();
                    hs.dishPrefab = boat;
                    hs.dishScale = PropScale;

                    var plate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    plate.name = "Plate Mark";
                    plate.transform.SetParent(spot.transform, false);
                    plate.transform.localPosition = new Vector3(0f, 0.004f, 0f);
                    plate.transform.localScale = new Vector3(0.26f, 0.004f, 0.26f);
                    plate.GetComponent<Renderer>().sharedMaterial = spotMat;
                    UnityEngine.Object.DestroyImmediate(plate.GetComponent<Collider>());
                }
            }

            // ---------------------------------------------------------------- customers
            Vector3 windowCenter = PartBounds(truck, "Service_Window", out Bounds wb) ? wb.center : fcb.center;
            Vector3 side = Vector3.Cross(Vector3.up, frontDir).normalized;
            Vector3 slotBase = new Vector3(windowCenter.x, floorY, windowCenter.z) + frontDir * 0.9f;
            var points = new GameObject("Customer Points").transform;
            Transform Point(string n, Vector3 p) { var t = new GameObject(n).transform; t.SetParent(points, false); t.position = p; return t; }
            Transform[] slots = { Point("Slot 1", slotBase - side * 0.8f), Point("Slot 2", slotBase), Point("Slot 3", slotBase + side * 0.8f) };
            Transform spawn = Point("Spawn", slotBase + frontDir * 6f - side * 8f);
            Transform exit = Point("Exit", slotBase + frontDir * 6f + side * 8f);

            // ---------------------------------------------------------------- player
            var player = new GameObject("Player");
            player.transform.SetPositionAndRotation(new Vector3(roomCenter.x, floorY + 0.05f, roomCenter.z), Quaternion.LookRotation(frontDir, Vector3.up));
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.7f;
            cc.radius = 0.28f;
            cc.center = new Vector3(0f, 0.86f, 0f);
            var pc = player.AddComponent<PlayerController>();
            var camGo = new GameObject("Player Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f;
            cam.fieldOfView = 70f;
            camGo.AddComponent<AudioListener>();
            pc.cameraPivot = camGo.transform;
            var hands = camGo.AddComponent<PlayerHands>();
            hands.heldPrefab = FindModel("Takoyaki_Boat");
            var interactor = camGo.AddComponent<Interactor>();
            interactor.hands = hands;
            interactor.player = pc;

            // ---------------------------------------------------------------- game systems
            var clock = game.AddComponent<DayClock>();
            clock.sun = sun;
            var spawner = game.AddComponent<CustomerSpawner>();
            spawner.customerPrefab = FindVrm();
            spawner.animatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CustomerAnimatorPath);
            spawner.slots = slots;
            spawner.spawnPoint = spawn;
            spawner.exitPoint = exit;
            spawner.faceDirection = -frontDir;
            var ui = game.AddComponent<UIManager>();
            ui.bodyFont = fonts.body;
            ui.titleFont = fonts.title;
            ui.bodyOutline = fonts.bodyOutline;
            ui.titleOutline = fonts.titleOutline;
            if (!fonts.Ok) Debug.LogWarning("[Chef24] UI fonts are not ready yet. Run Tools > Chef24 > Build MVP Scene again.");
            var gm = game.AddComponent<GameManager>();
            gm.clock = clock;
            gm.player = pc;
            gm.hands = hands;
            gm.spawner = spawner;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            Selection.activeGameObject = game;

            string note = (spawner.customerPrefab == null ? "\n· .vrm 이 없어서 손님은 캡슐로 나와요." : "") +
                          (spawner.animatorController == null ? "\n· 손님 걷기 애니메이션이 없어요 (FoodTruckTest > Rebuild Avatar Animations 먼저 실행)." : "");
            Debug.Log("[Chef24] Created " + ScenePath + ". Press Play." + note.Replace("\n", " "));
            if (note.Length > 0) EditorUtility.DisplayDialog("Chef24", "씬을 만들었어요. 참고:" + note, "OK");
        }

        // ---------------------------------------------------------------- ceiling + lights

        /// <summary>
        /// The truck model has no roof. Adds a ceiling slab over the walls and soft interior lights
        /// (no shadows) so the inside is evenly lit instead of showing hard sun shadows.
        /// </summary>
        static void BuildCeilingAndLights(GameObject truck, Bounds all, float floorY)
        {
            Bounds walls = all;
            bool found = false;
            foreach (string w in new[] { "Wall_Back", "Wall_Left", "Wall_Right", "Wall_Front" })
            {
                if (!PartBounds(truck, w, out Bounds b)) continue;
                if (!found) { walls = b; found = true; }
                else walls.Encapsulate(b);
            }
            float top = found ? walls.max.y : floorY + 2.7f;

            var ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceiling.name = "Ceiling";
            ceiling.transform.position = new Vector3(walls.center.x, top + 0.05f, walls.center.z);
            ceiling.transform.localScale = new Vector3(walls.size.x, 0.1f, walls.size.z);
            ceiling.GetComponent<Renderer>().sharedMaterial = MakeMaterial("Ceiling", new Color(0.93f, 0.9f, 0.82f));

            var lights = new GameObject("Interior Lights").transform;
            bool alongX = walls.size.x >= walls.size.z;
            float length = alongX ? walls.size.x : walls.size.z;
            Material fixtureMat = MakeEmissiveMaterial("Ceiling Light", new Color(1f, 0.95f, 0.85f));
            const int count = 3;
            for (int i = 0; i < count; i++)
            {
                float off = (i - (count - 1) * 0.5f) * (length / count);
                Vector3 p = new Vector3(walls.center.x, top - 0.02f, walls.center.z) + (alongX ? Vector3.right : Vector3.forward) * off;

                var fixture = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fixture.name = "Light Panel " + (i + 1);
                fixture.transform.SetParent(lights, false);
                fixture.transform.position = p;
                fixture.transform.localScale = alongX ? new Vector3(0.9f, 0.03f, 0.35f) : new Vector3(0.35f, 0.03f, 0.9f);
                fixture.GetComponent<Renderer>().sharedMaterial = fixtureMat;
                fixture.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                UnityEngine.Object.DestroyImmediate(fixture.GetComponent<Collider>());

                var l = new GameObject("Interior Light " + (i + 1)).AddComponent<Light>();
                l.transform.SetParent(lights, false);
                l.transform.position = p + Vector3.down * 0.25f;
                l.type = LightType.Point;
                l.color = new Color(1f, 0.95f, 0.86f);
                l.range = 4.5f;
                l.intensity = 0.9f;
                l.shadows = LightShadows.None;
            }
        }

        // ---------------------------------------------------------------- placing

        /// <summary>
        /// Props are modelled at real size, which reads too small in first person. Everything placed on the
        /// counters is scaled up by this.
        /// </summary>
        const float PropScale = 2.2f;

        /// <summary>Bowls read small next to the bottles, so they get this on top of PropScale.</summary>
        const float BowlScale = 1.5f;

        /// <summary>A name tag floating above a station, always turned to the camera.</summary>
        static void AddLabel(GameObject station, string text, Chef24Fonts.Set fonts)
        {
            Bounds b = RendererBounds(station.transform);
            var go = new GameObject("Label");
            go.transform.SetParent(station.transform, true);
            go.transform.position = new Vector3(b.center.x, b.max.y + 0.09f, b.center.z);
            var tmp = go.AddComponent<TMPro.TextMeshPro>();
            if (fonts.body != null) tmp.font = fonts.body;
            if (fonts.bodyOutline != null) tmp.fontSharedMaterial = fonts.bodyOutline;
            tmp.text = text;
            tmp.fontSize = 0.8f;
            tmp.color = Palette.Cream;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.rectTransform.sizeDelta = new Vector2(1.2f, 0.2f);
            go.AddComponent<WorldLabel>();
        }

        /// <summary>Puts a model on top of a counter at fraction t along its long side, facing the room.</summary>
        static GameObject Place(string model, Bounds surface, float t, Vector3 roomCenter, Transform parent, float extraScale = 1f)
        {
            GameObject asset = FindModel(model);
            if (asset == null) { Debug.LogWarning("[Chef24] Missing model " + model); return new GameObject(model + " (missing)"); }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            go.name = model;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localScale = go.transform.localScale * PropScale * extraScale;

            bool alongX = surface.size.x >= surface.size.z;
            Vector3 axis = alongX ? Vector3.right : Vector3.forward;
            Vector3 depth = alongX ? Vector3.forward : Vector3.right;
            float toRoom = Vector3.Dot(roomCenter - surface.center, depth) >= 0f ? 1f : -1f;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(depth * toRoom, Vector3.up));

            Bounds b = RendererBounds(go.transform);
            float along = Mathf.Lerp(Vector3.Dot(surface.min, axis) + 0.1f, Vector3.Dot(surface.max, axis) - 0.1f, t);
            float dc = Vector3.Dot(surface.center, depth);
            go.transform.position = axis * (along - Vector3.Dot(b.center, axis)) + depth * (dc - Vector3.Dot(b.center, depth))
                                    + Vector3.up * (surface.max.y - b.min.y);
            return go;
        }

        /// <summary>Top of a counter at fraction t along its long side, centred across its depth.</summary>
        static Vector3 SurfacePoint(Bounds surface, float t)
        {
            bool alongX = surface.size.x >= surface.size.z;
            float a = alongX ? Mathf.Lerp(surface.min.x + 0.15f, surface.max.x - 0.15f, t) : Mathf.Lerp(surface.min.z + 0.15f, surface.max.z - 0.15f, t);
            return alongX ? new Vector3(a, surface.max.y, surface.center.z) : new Vector3(surface.center.x, surface.max.y, a);
        }

        /// <summary>Wraps visuals in a station root with a box collider so the player can aim at it.</summary>
        static GameObject MakeStation(string name, Transform parent, params GameObject[] visuals)
        {
            Bounds b = RendererBounds(visuals[0].transform);
            foreach (var v in visuals.Skip(1)) b.Encapsulate(RendererBounds(v.transform));
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = b.center;
            foreach (var v in visuals) v.transform.SetParent(root.transform, true);
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = Vector3.zero;
            // Generous hitbox: a bit bigger than the model and never smaller than 40 cm, so aiming is easy.
            box.size = Vector3.Max(b.size + new Vector3(0.15f, 0.25f, 0.15f), new Vector3(0.4f, 0.4f, 0.4f));
            return root;
        }

        // ---------------------------------------------------------------- lookup helpers

        static GameObject FindModel(string name)
        {
            string[] exts = { ".fbx", ".glb", ".gltf", ".prefab" };
            foreach (string p in AssetDatabase.FindAssets(name).Select(g => AssetDatabase.GUIDToAssetPath(g))
                         .Where(p => exts.Contains(Path.GetExtension(p).ToLowerInvariant()))
                         .Where(p => string.Equals(Path.GetFileNameWithoutExtension(p), name, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(p => Array.IndexOf(exts, Path.GetExtension(p).ToLowerInvariant())))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go != null) return go;
            }
            return null;
        }

        static GameObject FindVrm()
        {
            string path = AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/") && p.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.IndexOf("restored", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1)
                .FirstOrDefault();
            return path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
        }

        static bool PartBounds(GameObject truck, string name, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (Transform t in truck.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != name) continue;
                foreach (var r in t.GetComponentsInChildren<Renderer>())
                {
                    if (!found) { bounds = r.bounds; found = true; }
                    else bounds.Encapsulate(r.bounds);
                }
            }
            return found;
        }

        static Bounds RendererBounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        static Material MakeMaterial(string name, Color color)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mat = new Material(temp.GetComponent<Renderer>().sharedMaterial) { name = name, color = color };
            UnityEngine.Object.DestroyImmediate(temp);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static Material MakeEmissiveMaterial(string name, Color color)
        {
            Material mat = MakeMaterial(name, color);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 1.2f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void AddToBuildSettings(string path)
        {
            var list = EditorBuildSettings.scenes.ToList();
            if (list.Any(s => s.path == path)) return;
            list.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
