using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CapstoneVrmTest.EditorTools
{
    /// <summary>
    /// Menu: Tools > VRM Test > Setup Mixamo Animations
    /// 1. Put Mixamo FBX files whose names contain "idle", "walk" and "run"
    ///    (and optionally "jump") into Assets/VRMTest/Animations.
    /// 2. Open VRMTestScene and run this menu.
    /// It sets the FBX files to Humanoid + loop, builds an Animator Controller
    /// with a "Speed" blend tree (idle 0 / walk 2 / run 5 m/s) and assigns it to the avatar.
    /// </summary>
    public static class VrmTestAnimationSetup
    {
        const string AnimFolder = "Assets/VRMTest/Animations";
        const string ControllerPath = "Assets/VRMTest/VRMTestAnimator.controller";

        [MenuItem("Tools/VRM Test/Setup Mixamo Animations")]
        public static void Setup()
        {
            if (!AssetDatabase.IsValidFolder(AnimFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/VRMTest")) AssetDatabase.CreateFolder("Assets", "VRMTest");
                AssetDatabase.CreateFolder("Assets/VRMTest", "Animations");
            }

            string[] fbxPaths = AssetDatabase.FindAssets("", new[] { AnimFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            string idlePath = Pick(fbxPaths, "idle");
            string runPath = Pick(fbxPaths, "run");
            string walkPath = Pick(fbxPaths, "walk");
            string jumpPath = Pick(fbxPaths, "jump"); // optional

            if (idlePath == null || walkPath == null || runPath == null)
            {
                EditorUtility.DisplayDialog("VRM Test",
                    "Put three Mixamo FBX files into Assets/VRMTest/Animations.\n" +
                    "Their file names must contain:\n  idle\n  walk\n  run\n\n" +
                    "Found:\n  idle: " + (idlePath ?? "(missing)") +
                    "\n  walk: " + (walkPath ?? "(missing)") +
                    "\n  run: " + (runPath ?? "(missing)"),
                    "OK");
                return;
            }

            AnimationClip idle = PrepareClip(idlePath, true);
            AnimationClip walk = PrepareClip(walkPath, true);
            AnimationClip run = PrepareClip(runPath, true);
            AnimationClip jump = jumpPath != null ? PrepareClip(jumpPath, false) : null;
            if (idle == null || walk == null || run == null)
            {
                EditorUtility.DisplayDialog("VRM Test", "Could not read an animation clip from one of the FBX files. See the Console.", "OK");
                return;
            }

            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

            BlendTree tree;
            AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 2f);
            tree.AddChild(run, 5f);

            if (jump != null)
            {
                controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
                controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
                var sm = controller.layers[0].stateMachine;
                AnimatorState jumpState = sm.AddState("Jump");
                jumpState.motion = jump;

                AnimatorStateTransition toJump = locomotion.AddTransition(jumpState);
                toJump.hasExitTime = false;
                toJump.duration = 0.1f;
                toJump.AddCondition(AnimatorConditionMode.If, 0f, "Jump");

                AnimatorStateTransition land = jumpState.AddTransition(locomotion);
                land.hasExitTime = true;   // let the take-off play first
                land.exitTime = 0.5f;
                land.duration = 0.15f;
                land.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            int assigned = 0;
            foreach (var player in Resources.FindObjectsOfTypeAll<SimpleVrmController>())
            {
                if (EditorUtility.IsPersistent(player) || !player.gameObject.scene.IsValid()) continue;
                var animator = player.GetComponentInChildren<Animator>();
                if (animator == null) continue;
                Undo.RecordObject(animator, "Assign VRM test animator");
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                EditorUtility.SetDirty(animator);
                EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
                assigned++;
            }
            if (assigned > 0) EditorSceneManager.SaveOpenScenes();

            Selection.activeObject = controller;
            string msg = "Animator Controller created: " + ControllerPath +
                         (jump != null ? "\nJump animation: " + jumpPath : "\n(No jump FBX found - jump animation skipped)") +
                         (assigned > 0 ? "\nAssigned to the avatar in the open scene. Press Play!"
                                       : "\nNo avatar found in the open scene. Open VRMTestScene and run this menu again.");
            Debug.Log("[VRMTest] " + msg.Replace("\n", " "));
            EditorUtility.DisplayDialog("VRM Test", msg, "OK");
        }

        static string Pick(string[] paths, string keyword)
        {
            return paths.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static AnimationClip PrepareClip(string path, bool loop)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) { Debug.LogError("[VRMTest] Not a model file: " + path); return null; }

            // Pass 1: make it Humanoid so it can drive the VRM avatar.
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();
            }

            // Pass 2: loop it and keep the character in place (the script moves it).
            var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.loopTime = loop;
                c.loopPose = loop;
                c.lockRootRotation = true;
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = true;
                c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();

            var clip = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(a => !a.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (clip == null) Debug.LogError("[VRMTest] No animation clip inside " + path);
            return clip;
        }
    }
}
