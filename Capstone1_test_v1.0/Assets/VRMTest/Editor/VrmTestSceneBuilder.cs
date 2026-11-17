using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CapstoneVrmTest.EditorTools
{
    /// <summary>
    /// Menu: Tools > VRM Test > Create Test Scene
    /// Builds Assets/VRMTest/VRMTestScene.unity with ground, props, the VRM avatar,
    /// a third-person controller, an orbit camera and an on-screen help panel.
    /// Select a .vrm in the Project window first to choose which avatar to use;
    /// otherwise the first .vrm found in Assets is used.
    /// </summary>
    public static class VrmTestSceneBuilder
    {
        const string Root = "Assets/VRMTest";
        const string ScenePath = Root + "/VRMTestScene.unity";
        const string MaterialFolder = Root + "/Materials";

        [MenuItem("Tools/VRM Test/Create Test Scene")]
        public static void CreateTestScene()
        {
            string vrmPath = FindVrmPath();
            if (vrmPath == null)
            {
                EditorUtility.DisplayDialog("VRM Test",
                    "No .vrm file was found in Assets.\n\n" +
                    "1. Install UniVRM.\n2. Drag your .vrm file into the Assets folder.\n3. Run this menu again.",
                    "OK");
                return;
            }

            GameObject avatarAsset = AssetDatabase.LoadAssetAtPath<GameObject>(vrmPath);
            if (avatarAsset == null)
            {
                EditorUtility.DisplayDialog("VRM Test",
                    "Found " + vrmPath + " but it was not imported as a model.\n\n" +
                    "Check that UniVRM is installed and that the Console shows no import errors.",
                    "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder("Assets", "VRMTest");
            EnsureFolder(Root, "Materials");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Ground and reference props
            Material groundMat = MakeMaterial("Ground", new Color(0.55f, 0.58f, 0.62f));
            Material propMat = MakeMaterial("Prop", new Color(0.95f, 0.65f, 0.3f));
            Material markerMat = MakeMaterial("Marker", new Color(0.3f, 0.6f, 0.95f));

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground (50m x 50m)";
            ground.transform.localScale = new Vector3(5f, 1f, 5f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;

            var props = new GameObject("Props");
            MakeBox("Box 1m", new Vector3(3f, 0.5f, 3f), Vector3.one, Quaternion.identity, propMat, props.transform);
            MakeBox("Box 2m (height reference)", new Vector3(-3f, 1f, 3f), new Vector3(1f, 2f, 1f), Quaternion.identity, markerMat, props.transform);
            MakeBox("Ramp", new Vector3(0f, 0.6f, 7f), new Vector3(3f, 0.2f, 5f), Quaternion.Euler(-15f, 0f, 0f), propMat, props.transform);
            for (int i = 0; i < 4; i++)
            {
                float h = 0.2f * (i + 1);
                MakeBox("Step " + (i + 1), new Vector3(6f, h * 0.5f, 2f + i * 0.6f), new Vector3(2f, h, 0.6f), Quaternion.identity, markerMat, props.transform);
            }

            // Avatar
            var avatar = (GameObject)PrefabUtility.InstantiatePrefab(avatarAsset);
            avatar.name = avatarAsset.name;
            avatar.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var cc = avatar.AddComponent<CharacterController>();
            cc.height = 1.7f;
            cc.radius = 0.25f;
            cc.center = new Vector3(0f, 0.86f, 0f);
            cc.stepOffset = 0.3f;

            var player = avatar.AddComponent<SimpleVrmController>();
            var expressions = avatar.AddComponent<VrmExpressionTester>();

            // Camera
            Camera cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            var orbit = cam.gameObject.AddComponent<OrbitFollowCamera>();
            orbit.target = avatar.transform;
            cam.transform.position = new Vector3(0f, 1.8f, -3f);
            cam.transform.LookAt(new Vector3(0f, 1.4f, 0f));
            player.cameraTransform = cam.transform;

            var hud = cam.gameObject.AddComponent<VrmTestHud>();
            hud.player = player;
            hud.expressions = expressions;

            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = avatar;
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
            Debug.Log("[VRMTest] Created " + ScenePath + " using " + vrmPath + ". Press Play to test.");
        }

        static string FindVrmPath()
        {
            string selected = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : null;
            if (!string.IsNullOrEmpty(selected) && selected.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase)) return selected;

            return AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/") && p.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.IndexOf("restored", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1)
                .ThenBy(p => p)
                .FirstOrDefault();
        }

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

        static void MakeBox(string name, Vector3 pos, Vector3 scale, Quaternion rot, Material mat, Transform parent)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.SetPositionAndRotation(pos, rot);
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }
}
