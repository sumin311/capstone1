using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CapstoneFoodTruckTest.EditorTools
{
    /// <summary>
    /// Menu: Tools > FoodTruck Test > Rebuild Avatar Animations
    ///
    /// Builds Assets/FoodTruckTest/Animations/FoodTruckAvatar.controller from FBX files (Mixamo etc.):
    ///  - Locomotion: idle / walk / run (+ optional jump) blend tree on "Speed" (0 / 2 / 5 m/s).
    ///    Taken from Animations/Locomotion, or from Assets/VRMTest/Animations if not there.
    ///  - Actions: every FBX in Animations/Actions (sub folders too) becomes one state, named after the file.
    ///      Wave.fbx              -> "Wave", plays once then returns to idle/walk
    ///      Stir_loop.fbx         -> "Stir", loops until Stop() or until the character moves
    ///      CarryTray_upper_loop.fbx -> "CarryTray", upper body only (legs keep walking), loops
    /// The FBX files are switched to Humanoid + in-place automatically.
    /// Adding motions later = drop FBX files in, run this menu again.
    /// </summary>
    public static class FoodTruckAnimatorBuilder
    {
        public const string AnimRoot = "Assets/FoodTruckTest/Animations";
        public const string LocomotionFolder = AnimRoot + "/Locomotion";
        public const string ActionsFolder = AnimRoot + "/Actions";
        public const string ControllerPath = AnimRoot + "/FoodTruckAvatar.controller";
        const string UpperMaskPath = AnimRoot + "/UpperBody.mask";
        static readonly string[] FallbackLocomotionFolders = { "Assets/VRMTest/Animations" };

        // Speeds must match SimpleVrmController.walkSpeed / runSpeed.
        const float WalkSpeed = 2f;
        const float RunSpeed = 5f;

        [MenuItem("Tools/FoodTruck Test/Rebuild Avatar Animations")]
        public static void RebuildMenu()
        {
            AnimatorController controller = Build(out var actions, out string report);
            if (controller == null)
            {
                EditorUtility.DisplayDialog("FoodTruck Test", report, "OK");
                return;
            }
            int assigned = AssignToOpenScene(controller, actions);
            report += assigned > 0
                ? "\n\nApplied to " + assigned + " avatar(s) in the open scene. Press Play!"
                : "\n\nNo avatar in the open scene. Run Tools > FoodTruck Test > Create Test Scene.";
            Debug.Log("[FoodTruckTest] " + report.Replace("\n", " "));
            EditorUtility.DisplayDialog("FoodTruck Test", report, "OK");
        }

        /// <summary>Builds the controller. Returns null (and the reason in report) if idle/walk/run are missing.</summary>
        public static AnimatorController Build(out List<FoodTruckAvatarAnimator.ActionInfo> actions, out string report)
        {
            actions = new List<FoodTruckAvatarAnimator.ActionInfo>();
            EnsureFolders();

            string[] locoSearch = new[] { LocomotionFolder }.Concat(FallbackLocomotionFolders)
                .Where(AssetDatabase.IsValidFolder).ToArray();
            string[] locoFiles = FbxIn(locoSearch);
            string idlePath = Pick(locoFiles, "idle");
            string walkPath = Pick(locoFiles, "walk");
            string runPath = Pick(locoFiles, "run");
            string jumpPath = Pick(locoFiles, "jump");

            if (idlePath == null || walkPath == null)
            {
                report = "Idle / walk animations were not found.\n\n" +
                         "Put FBX files whose names contain 'idle', 'walk' (and 'run') into\n" + LocomotionFolder +
                         "\n(or Assets/VRMTest/Animations), then run Rebuild Avatar Animations.";
                return null;
            }

            AnimationClip idle = PrepareClip(idlePath, true);
            AnimationClip walk = PrepareClip(walkPath, true);
            AnimationClip run = runPath != null ? PrepareClip(runPath, true) : null;
            AnimationClip jump = jumpPath != null ? PrepareClip(jumpPath, false) : null;
            if (idle == null || walk == null)
            {
                report = "Could not read the idle / walk clip. See the Console.";
                return null;
            }

            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);

            // ---- base layer: locomotion
            AnimatorState locomotion = controller.CreateBlendTreeInController(FoodTruckAvatarAnimator.LocomotionState, out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, WalkSpeed);
            tree.AddChild(run != null ? run : walk, RunSpeed);

            AnimatorStateMachine baseSm = controller.layers[0].stateMachine;
            baseSm.defaultState = locomotion;
            var reserved = new HashSet<string> { FoodTruckAvatarAnimator.LocomotionState, FoodTruckAvatarAnimator.UpperBodyEmptyState };

            if (jump != null)
            {
                controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
                AnimatorState jumpState = baseSm.AddState("Jump", new Vector3(300, 0, 0));
                jumpState.motion = jump;
                var toJump = locomotion.AddTransition(jumpState);
                toJump.hasExitTime = false;
                toJump.duration = 0.1f;
                toJump.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
                var land = jumpState.AddTransition(locomotion);
                land.hasExitTime = true;
                land.exitTime = 0.5f;
                land.duration = 0.15f;
                land.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
                reserved.Add("Jump");
            }

            // ---- actions
            var parsed = new List<(FoodTruckAvatarAnimator.ActionInfo info, string path)>();
            foreach (string path in FbxIn(new[] { ActionsFolder }))
            {
                var info = ParseActionName(Path.GetFileNameWithoutExtension(path));
                string name = info.name;
                for (int n = 2; reserved.Contains(name); n++) name = info.name + "_" + n;
                info.name = name;
                reserved.Add(name);
                parsed.Add((info, path));
            }

            AnimatorStateMachine upperSm = null;
            AnimatorState upperEmpty = null;
            if (parsed.Any(p => p.info.upperBody))
            {
                controller.AddLayer(FoodTruckAvatarAnimator.UpperBodyLayer);
                AnimatorControllerLayer[] layers = controller.layers;
                layers[1].avatarMask = CreateUpperBodyMask();
                layers[1].defaultWeight = 1f;
                controller.layers = layers;
                upperSm = controller.layers[1].stateMachine;
                upperEmpty = upperSm.AddState(FoodTruckAvatarAnimator.UpperBodyEmptyState, new Vector3(300, -100, 0));
                upperSm.defaultState = upperEmpty;
            }

            var log = new StringBuilder();
            int row = 0;
            foreach (var (info, path) in parsed)
            {
                AnimationClip clip = PrepareClip(path, info.loop);
                if (clip == null) { log.Append("\n  (skipped, no clip) ").Append(path); continue; }

                AnimatorStateMachine sm = info.upperBody ? upperSm : baseSm;
                AnimatorState back = info.upperBody ? upperEmpty : locomotion;
                AnimatorState state = sm.AddState(info.name, new Vector3(550, row++ * 60 - 100, 0));
                state.motion = clip;
                if (!info.loop)
                {
                    var t = state.AddTransition(back);
                    t.hasExitTime = true;
                    t.exitTime = 0.9f;
                    t.duration = 0.15f;
                }
                actions.Add(info);
                log.Append("\n  ").Append(info.name).Append(info.loop ? " (loop)" : "").Append(info.upperBody ? " [upper]" : "");
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            report = "Animator Controller: " + ControllerPath +
                     "\nIdle: " + idlePath + "\nWalk: " + walkPath + "\nRun: " + (runPath ?? "(none, walk used)") +
                     (jumpPath != null ? "\nJump: " + jumpPath : "") +
                     "\nActions (" + actions.Count + "):" + (actions.Count > 0 ? log.ToString() : "\n  none yet - put FBX files in " + ActionsFolder);
            return controller;
        }

        /// <summary>Sets up the Animator + FoodTruckAvatarAnimator on an avatar root.</summary>
        public static void Apply(GameObject avatar, AnimatorController controller, List<FoodTruckAvatarAnimator.ActionInfo> actions)
        {
            Animator animator = avatar.GetComponentInChildren<Animator>();
            if (animator == null) animator = avatar.AddComponent<Animator>();
            Undo.RecordObject(animator, "Assign FoodTruck animator");
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            EditorUtility.SetDirty(animator);

            var player = avatar.GetComponent<FoodTruckAvatarAnimator>();
            if (player == null) player = Undo.AddComponent<FoodTruckAvatarAnimator>(avatar);
            Undo.RecordObject(player, "Assign FoodTruck actions");
            player.actions = actions.Select(a => new FoodTruckAvatarAnimator.ActionInfo { name = a.name, loop = a.loop, upperBody = a.upperBody }).ToList();
            EditorUtility.SetDirty(player);
        }

        static int AssignToOpenScene(AnimatorController controller, List<FoodTruckAvatarAnimator.ActionInfo> actions)
        {
            // Avatars = scene objects that already have our component, or a CharacterController + Animator (VRMTest avatar).
            var roots = new HashSet<GameObject>();
            foreach (var p in UnityEngine.Object.FindObjectsOfType<FoodTruckAvatarAnimator>()) roots.Add(p.gameObject);
            foreach (var cc in UnityEngine.Object.FindObjectsOfType<CharacterController>())
                if (cc.GetComponentInChildren<Animator>() != null) roots.Add(cc.gameObject);

            foreach (var go in roots)
            {
                Apply(go, controller, actions);
                EditorSceneManager.MarkSceneDirty(go.scene);
            }
            if (roots.Count > 0) EditorSceneManager.SaveOpenScenes();
            return roots.Count;
        }

        // ------------------------------------------------------------------ helpers

        static FoodTruckAvatarAnimator.ActionInfo ParseActionName(string fileName)
        {
            var info = new FoodTruckAvatarAnimator.ActionInfo();
            var tokens = fileName.Split('_').ToList();
            while (tokens.Count > 1)
            {
                string last = tokens[tokens.Count - 1].ToLowerInvariant();
                if (last == "loop") info.loop = true;
                else if (last == "upper") info.upperBody = true;
                else break;
                tokens.RemoveAt(tokens.Count - 1);
            }
            info.name = string.Join("_", tokens);
            return info;
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/FoodTruckTest")) AssetDatabase.CreateFolder("Assets", "FoodTruckTest");
            if (!AssetDatabase.IsValidFolder(AnimRoot)) AssetDatabase.CreateFolder("Assets/FoodTruckTest", "Animations");
            if (!AssetDatabase.IsValidFolder(LocomotionFolder)) AssetDatabase.CreateFolder(AnimRoot, "Locomotion");
            if (!AssetDatabase.IsValidFolder(ActionsFolder)) AssetDatabase.CreateFolder(AnimRoot, "Actions");
        }

        static string[] FbxIn(string[] folders)
        {
            if (folders.Length == 0) return new string[0];
            return AssetDatabase.FindAssets("", folders)
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        static string Pick(string[] paths, string keyword)
        {
            // paths are ordered by folder priority only within FindAssets; prefer our own Locomotion folder.
            return paths.Where(p => Path.GetFileNameWithoutExtension(p).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(p => p.StartsWith(LocomotionFolder + "/", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .FirstOrDefault();
        }

        static AvatarMask CreateUpperBodyMask()
        {
            var mask = new AvatarMask();
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);
            AssetDatabase.DeleteAsset(UpperMaskPath);
            AssetDatabase.CreateAsset(mask, UpperMaskPath);
            return mask;
        }

        /// <summary>Humanoid rig + in-place (root locked) + loop flag, then returns the clip inside the FBX.</summary>
        static AnimationClip PrepareClip(string path, bool loop)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) { Debug.LogError("[FoodTruckTest] Not a model file: " + path); return null; }

            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();
            }

            var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            bool changed = importer.clipAnimations.Length == 0;
            foreach (var c in clips)
            {
                changed |= c.loopTime != loop || !c.lockRootRotation || !c.lockRootHeightY || !c.lockRootPositionXZ;
                c.loopTime = loop;
                c.loopPose = loop;
                c.lockRootRotation = true;
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = true;
                c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalPositionXZ = true;
            }
            if (changed)
            {
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }

            var clip = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(a => !a.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (clip == null) Debug.LogError("[FoodTruckTest] No animation clip inside " + path);
            return clip;
        }
    }
}
