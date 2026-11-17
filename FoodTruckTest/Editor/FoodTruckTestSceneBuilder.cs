using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CapstoneFoodTruckTest.EditorTools
{
    /// <summary>
    /// Menu: Tools > FoodTruck Test > Create Test Scene
    /// Builds Assets/FoodTruckTest/FoodTruckTestScene.unity: loads foodtruck_basic (.glb/.fbx),
    /// then places every food model (Takoyaki / Sandwich / Yakitori / Crepe) on the counters and shelves.
    /// If the VRMTest scripts and a .vrm are in the project, the avatar is added so you can walk around;
    /// otherwise (or with the "Free Camera" menu) a fly camera is used.
    /// </summary>
    public static class FoodTruckTestSceneBuilder
    {
        const string Root = "Assets/FoodTruckTest";
        const string ScenePath = Root + "/FoodTruckTestScene.unity";
        const string MaterialFolder = Root + "/Materials";
        const string TruckAssetName = "foodtruck_basic";

        // Every item is turned to face the middle of the truck, then rotated by this many degrees.
        // If the fronts / labels end up facing the wall, change this to 180.
        const float ItemYawOffset = 0f;

        // Where each group of models goes. Names are the FBX file names (without extension).
        static readonly string[] Stove =
        {
            "Takoyaki_Grill_Cooking", "Sandwich_Press", "Yakitori_Grill_Cooking", "Crepe_Maker",
            "Takoyaki_Grill", "Yakitori_Grill",
        };

        static readonly string[] Prep =
        {
            "Batter_Bowl", "Octopus_Bowl", "GreenOnion_Bowl", "Chicken_Bowl",
            "Crepe_Batter_Bowl", "Whipped_Cream_Bowl", "Strawberry_Bowl",
        };

        static readonly string[] Front =
        {
            "Takoyaki_Boat", "Takoyaki_Sauce_Bottle", "Mayo_Bottle",
            "Sandwich_Plate", "Ketchup_Bottle", "Mustard_Bottle",
            "Yakitori_Plate", "Tare_Bottle", "Shichimi_Shaker",
            "Crepe_Plate", "Crepe_Cone", "Choco_Spread_Jar", "Piping_Bag",
        };

        static readonly string[] Storage = { "Bread_Stack", "Bananas", "Butter_Bowl" };

        // Shelf boards (height of the board's top above the floor) and what goes on each.
        static readonly float[] ShelfHeights = { 0.53f, 0.99f, 1.45f };

        static readonly string[][] ShelfItems =
        {
            new[] { "Ham_Slices", "Cheese_Slices", "Lettuce", "Bread_Slice", "Green_Onion" },
            new[] { "Raw_Chicken_Skewer", "Raw_Negi_Skewer", "Yakitori_Skewer_Raw", "Yakitori_Skewer" },
            new[] { "Takoyaki_Ball", "Takoyaki_Ball_Raw", "Green_Onion_Pieces" },
        };

        // Items that sit on top of a machine: (machine, height of its cooking plate above the machine's pivot).
        static readonly (string item, string host, float height)[] OnTopOf =
        {
            ("Toasted_Sandwich", "Sandwich_Press", 0.081f),
            ("Crepe_Sheet", "Crepe_Maker", 0.064f),
        };

        [MenuItem("Tools/FoodTruck Test/Create Test Scene")]
        public static void CreateTestScene() => Build(true);

        [MenuItem("Tools/FoodTruck Test/Create Test Scene (Free Camera)")]
        public static void CreateTestSceneFreeCamera() => Build(false);

        static void Build(bool useAvatar)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder("Assets", "FoodTruckTest");
            EnsureFolder(Root, "Materials");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            GameObject truck = SpawnTruck(out bool placeholder);
            Bounds truckBounds = RendererBounds(truck.transform);
            Vector3 roomCenter = truckBounds.center;
            float floorY = TryPartBounds(truck, "Floor", out Bounds floorBounds) ? floorBounds.max.y : truckBounds.min.y;

            var foods = new GameObject("Foods").transform;
            var placed = new Dictionary<string, GameObject>();
            var missing = new List<string>();
            var notes = new List<string>();

            PlaceOnPart(truck, "Stove_Cabinet", "Stove", Stove, 0.12f, foods, roomCenter, placed, missing, notes);
            PlaceOnPart(truck, "Prep_Cabinet", "Prep", Prep, 0.04f, foods, roomCenter, placed, missing, notes);
            PlaceOnPart(truck, "Front_Counter", "Front Counter", Front, 0.08f, foods, roomCenter, placed, missing, notes);
            PlaceOnPart(truck, "Storage_Cabinet", "Storage", Storage, 0.05f, foods, roomCenter, placed, missing, notes);

            if (TryPartBounds(truck, "Shelves", out Bounds shelf))
            {
                for (int i = 0; i < ShelfHeights.Length; i++)
                {
                    PlaceRow(shelf, floorY + ShelfHeights[i], "Shelf " + (i + 1), ShelfItems[i], 0.05f,
                        foods, roomCenter, placed, missing, notes);
                }
            }
            else
            {
                notes.Add("'Shelves' was not found in the truck, shelf items were skipped.");
                foreach (var row in ShelfItems) missing.AddRange(row);
            }

            foreach (var (item, host, height) in OnTopOf)
            {
                if (!placed.TryGetValue(host, out GameObject hostGo))
                {
                    notes.Add(item + " was skipped because " + host + " is missing.");
                    missing.Add(item);
                    continue;
                }
                GameObject go = Spawn(item, hostGo.transform.parent);
                if (go == null) { missing.Add(item); continue; }
                go.transform.SetPositionAndRotation(hostGo.transform.position + Vector3.up * height, hostGo.transform.rotation);
                placed[item] = go;
            }

            if (!placeholder) AddMeshColliders(truck);

            // Lights: one inside the truck (the scene already has a directional light).
            var lamp = new GameObject("Truck Lamp").AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.range = 10f;
            lamp.intensity = 2.5f;
            lamp.transform.position = new Vector3(roomCenter.x, floorY + 2.3f, roomCenter.z);

            // Camera: high above the front half of the truck (the model has no roof), looking down into it.
            Camera cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            Vector3 frontDir = TryPartBounds(truck, "Front_Counter", out Bounds fc)
                ? Flat(fc.center - roomCenter).normalized
                : Vector3.back;
            if (frontDir == Vector3.zero) frontDir = Vector3.back;
            Vector3 floorCenter = new Vector3(roomCenter.x, floorY, roomCenter.z);
            cam.transform.position = floorCenter + frontDir * 1.3f + Vector3.up * 4.6f;
            cam.transform.LookAt(floorCenter - frontDir * 0.4f + Vector3.up * 0.6f);
            cam.nearClipPlane = 0.03f;

            Vector3 spawn = new Vector3(roomCenter.x, floorY + 0.02f, roomCenter.z);
            bool avatar = useAvatar && TrySpawnAvatar(spawn, cam);
            if (!avatar) cam.gameObject.AddComponent<FoodTruckFlyCamera>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = foods.gameObject;
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));

            Debug.Log("[FoodTruckTest] Created " + ScenePath + ": " + placed.Count + " food models placed" +
                      (placeholder ? " (truck model not found, using placeholder boxes)" : "") +
                      (avatar ? ", avatar added." : ", fly camera added.") + " Press Play to test.");
            foreach (string n in notes) Debug.LogWarning("[FoodTruckTest] " + n);

            if (placeholder || missing.Count > 0)
            {
                string msg = "";
                if (placeholder)
                {
                    msg += "foodtruck_basic was not found (or could not be imported), so simple boxes were used instead.\n" +
                           "Put foodtruck_basic.glb in Assets (needs UniGLTF from UniVRM or glTFast), or export it as .fbx.\n\n";
                }
                if (missing.Count > 0)
                {
                    msg += "These models were not found in Assets (" + missing.Count + "):\n" +
                           string.Join(", ", missing.Distinct()) + "\n\nCopy the fbx folders from each *_Models folder into Assets.";
                }
                EditorUtility.DisplayDialog("FoodTruck Test", msg, "OK");
            }
        }

        // ------------------------------------------------------------------ placing

        static void PlaceOnPart(GameObject truck, string part, string label, string[] names, float gap, Transform parent,
            Vector3 roomCenter, Dictionary<string, GameObject> placed, List<string> missing, List<string> notes)
        {
            if (!TryPartBounds(truck, part, out Bounds b))
            {
                notes.Add("'" + part + "' was not found in the truck, " + label + " items were skipped.");
                missing.AddRange(names);
                return;
            }
            PlaceRow(b, b.max.y, label, names, gap, parent, roomCenter, placed, missing, notes);
        }

        /// <summary>Lines items up along the long side of a surface, centred on its depth, all facing the room.</summary>
        static void PlaceRow(Bounds surface, float topY, string label, string[] names, float gap, Transform parent,
            Vector3 roomCenter, Dictionary<string, GameObject> placed, List<string> missing, List<string> notes)
        {
            bool alongX = surface.size.x >= surface.size.z;
            Vector3 axis = alongX ? Vector3.right : Vector3.forward;
            Vector3 depthAxis = alongX ? Vector3.forward : Vector3.right;
            float depthCenter = Vector3.Dot(surface.center, depthAxis);
            float start = Vector3.Dot(surface.min, axis) + 0.08f;
            float end = Vector3.Dot(surface.max, axis) - 0.08f;

            float toRoom = Vector3.Dot(roomCenter - surface.center, depthAxis) >= 0f ? 1f : -1f;
            Quaternion rot = Quaternion.LookRotation(depthAxis * toRoom, Vector3.up) * Quaternion.Euler(0f, ItemYawOffset, 0f);

            var group = new GameObject(label).transform;
            group.SetParent(parent, false);

            float cursor = start;
            foreach (string name in names)
            {
                GameObject go = Spawn(name, group);
                if (go == null) { missing.Add(name); continue; }

                go.transform.SetPositionAndRotation(Vector3.zero, rot);
                Bounds b = RendererBounds(go.transform);
                float width = Vector3.Dot(b.size, axis);
                float along = cursor + width * 0.5f;
                go.transform.position =
                    axis * (along - Vector3.Dot(b.center, axis)) +
                    depthAxis * (depthCenter - Vector3.Dot(b.center, depthAxis)) +
                    Vector3.up * (topY - b.min.y);

                cursor += width + gap;
                placed[name] = go;
            }

            if (cursor - gap > end)
            {
                notes.Add(label + ": items run " + (cursor - gap - end).ToString("0.00") + "m past the end of the surface.");
            }
        }

        static GameObject Spawn(string name, Transform parent)
        {
            GameObject asset = FindModel(name);
            if (asset == null) return null;
            var go = PrefabUtility.InstantiatePrefab(asset) as GameObject;
            if (go == null) go = UnityEngine.Object.Instantiate(asset);
            go.name = name;
            go.transform.SetParent(parent, false);
            return go;
        }

        static GameObject FindModel(string name)
        {
            string[] exts = { ".fbx", ".glb", ".gltf", ".prefab" };
            var paths = AssetDatabase.FindAssets(name)
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .Where(p => exts.Contains(Path.GetExtension(p).ToLowerInvariant()))
                .Where(p => string.Equals(Path.GetFileNameWithoutExtension(p), name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => Array.IndexOf(exts, Path.GetExtension(p).ToLowerInvariant()));
            foreach (string p in paths)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go != null) return go;
            }
            return null;
        }

        // ------------------------------------------------------------------ truck

        static GameObject SpawnTruck(out bool placeholder)
        {
            GameObject asset = FindModel(TruckAssetName);
            if (asset != null)
            {
                var go = PrefabUtility.InstantiatePrefab(asset) as GameObject;
                if (go == null) go = UnityEngine.Object.Instantiate(asset);
                go.name = "FoodTruck";
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                if (go.GetComponentsInChildren<Renderer>().Length > 0)
                {
                    placeholder = false;
                    return go;
                }
                UnityEngine.Object.DestroyImmediate(go);
            }
            placeholder = true;
            return BuildPlaceholderTruck();
        }

        /// <summary>Simple boxes with the same names and sizes as the real truck (Blender X,Y,Z -> Unity X,Z,Y).</summary>
        static GameObject BuildPlaceholderTruck()
        {
            var root = new GameObject("FoodTruck (Placeholder)");
            Material floor = MakeMaterial("PH_Floor", new Color(0.85f, 0.82f, 0.76f));
            Material wall = MakeMaterial("PH_Wall", new Color(0.96f, 0.91f, 0.82f));
            Material cab = MakeMaterial("PH_Cabinet", new Color(0.27f, 0.56f, 0.54f));
            Material steel = MakeMaterial("PH_Steel", new Color(0.80f, 0.82f, 0.84f));
            Material fridge = MakeMaterial("PH_Fridge", new Color(0.80f, 0.92f, 0.90f));

            Box("Floor", -0.1f, 6.5f, -0.1f, 3.7f, -0.1f, 0f, floor, root.transform);
            Box("Wall_Back", -0.1f, 6.5f, 3.6f, 3.7f, 0f, 2.7f, wall, root.transform);
            Box("Wall_Left", -0.1f, 0f, 0f, 3.6f, 0f, 2.7f, wall, root.transform);
            Box("Wall_Right", 6.4f, 6.5f, 0f, 3.6f, 0f, 2.7f, wall, root.transform);
            Box("Stove_Cabinet", 0f, 4.0f, 2.9f, 3.6f, 0f, 0.95f, steel, root.transform);
            Box("Prep_Cabinet", 4.0f, 5.65f, 2.9f, 3.6f, 0f, 0.95f, cab, root.transform);
            Box("Front_Counter", 0f, 5.65f, 0f, 0.6f, 0f, 0.95f, cab, root.transform);
            Box("Storage_Cabinet", 5.65f, 6.4f, 0f, 0.6f, 0f, 0.95f, cab, root.transform);
            Box("Fridge", 5.65f, 6.4f, 2.9f, 3.6f, 0f, 1.95f, fridge, root.transform);

            var shelves = new GameObject("Shelves").transform;
            shelves.SetParent(root.transform, false);
            foreach (float z in new[] { 0.04f, 0.50f, 0.96f, 1.42f, 1.88f })
                Box("Board", 5.65f, 6.4f, 0.6f, 2.1f, z, z + 0.03f, steel, shelves);
            foreach (float y in new[] { 0.6f, 2.1f })
            {
                Box("Post", 5.65f, 5.69f, y - 0.02f, y + 0.02f, 0f, 1.91f, cab, shelves);
                Box("Post", 6.36f, 6.4f, y - 0.02f, y + 0.02f, 0f, 1.91f, cab, shelves);
            }
            return root;
        }

        static void Box(string name, float x0, float x1, float y0, float y1, float z0, float z1, Material mat, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3((x0 + x1) * 0.5f, (z0 + z1) * 0.5f, (y0 + y1) * 0.5f);
            go.transform.localScale = new Vector3(x1 - x0, z1 - z0, y1 - y0);
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        static void AddMeshColliders(GameObject truck)
        {
            foreach (var mf in truck.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }
        }

        // ------------------------------------------------------------------ avatar (optional, uses VRMTest)

        static bool TrySpawnAvatar(Vector3 spawn, Camera cam)
        {
            Type controller = FindType("CapstoneVrmTest.SimpleVrmController");
            Type orbit = FindType("CapstoneVrmTest.OrbitFollowCamera");
            if (controller == null || orbit == null) return false;

            string vrmPath = AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/") && p.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.IndexOf("restored", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1)
                .ThenBy(p => p)
                .FirstOrDefault();
            if (vrmPath == null) return false;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(vrmPath);
            if (asset == null) return false;

            var avatar = PrefabUtility.InstantiatePrefab(asset) as GameObject;
            if (avatar == null) return false;
            avatar.name = asset.name;
            avatar.transform.SetPositionAndRotation(spawn, Quaternion.identity);

            var cc = avatar.AddComponent<CharacterController>();
            cc.height = 1.7f;
            cc.radius = 0.25f;
            cc.center = new Vector3(0f, 0.86f, 0f);
            cc.stepOffset = 0.3f;

            Component player = avatar.AddComponent(controller);
            Component follow = cam.gameObject.AddComponent(orbit);
            SetField(follow, "target", avatar.transform);
            SetField(follow, "distance", 2.2f);
            SetField(player, "cameraTransform", cam.transform);
            cam.transform.position = spawn + new Vector3(0f, 1.8f, -2.2f);
            cam.transform.LookAt(spawn + Vector3.up * 1.3f);

            Type expressions = FindType("CapstoneVrmTest.VrmExpressionTester");
            Type hud = FindType("CapstoneVrmTest.VrmTestHud");
            if (expressions != null && hud != null)
            {
                Component expr = avatar.AddComponent(expressions);
                Component h = cam.gameObject.AddComponent(hud);
                SetField(h, "player", player);
                SetField(h, "expressions", expr);
            }
            return true;
        }

        static Type FindType(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName)).FirstOrDefault(t => t != null);

        static void SetField(object target, string name, object value)
        {
            FieldInfo f = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null && (value == null || f.FieldType.IsInstanceOfType(value))) f.SetValue(target, value);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Union of renderer bounds of every object called <paramref name="name"/> inside the truck.</summary>
        static bool TryPartBounds(GameObject truck, string name, out Bounds bounds)
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

            // Copy the pipeline's default material so this works in Built-in, URP and HDRP.
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mat = new Material(temp.GetComponent<Renderer>().sharedMaterial) { name = name, color = color };
            UnityEngine.Object.DestroyImmediate(temp);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
