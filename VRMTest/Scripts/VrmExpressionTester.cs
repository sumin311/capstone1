using System;
using System.Reflection;
using UnityEngine;

namespace CapstoneVrmTest
{
    /// <summary>
    /// Number keys switch VRM 1.0 expressions (1 happy, 2 angry, 3 sad, 4 relaxed,
    /// 5 surprised, 6 blink, 7 "aa" mouth, 0 reset).
    /// Uses reflection so this file compiles even before UniVRM is installed.
    /// </summary>
    public class VrmExpressionTester : MonoBehaviour
    {
        static readonly string[] Presets = { "happy", "angry", "sad", "relaxed", "surprised", "blink", "aa" };

        public string Current { get; private set; } = "neutral";
        public bool Available => ready;

        Component vrmInstance;
        MethodInfo createFromPreset;
        MethodInfo setWeight;
        Type presetEnum;
        bool ready;

        void Start()
        {
            foreach (var c in GetComponentsInChildren<Component>(true))
            {
                if (c != null && c.GetType().Name == "Vrm10Instance") { vrmInstance = c; break; }
            }
            if (vrmInstance == null)
            {
                Debug.LogWarning("[VRMTest] Vrm10Instance not found, so the expression test is off. (Is this a VRM 0.x model, or is UniVRM missing?)");
                return;
            }

            Type keyType = FindType("UniVRM10.ExpressionKey");
            presetEnum = FindType("UniVRM10.ExpressionPreset");
            if (keyType == null || presetEnum == null)
            {
                Debug.LogWarning("[VRMTest] UniVRM10 expression types not found. Expression test is off.");
                return;
            }

            createFromPreset = keyType.GetMethod("CreateFromPreset", BindingFlags.Public | BindingFlags.Static, null, new[] { presetEnum }, null);
            ready = createFromPreset != null;
            if (!ready) Debug.LogWarning("[VRMTest] ExpressionKey.CreateFromPreset not found in this UniVRM version.");
        }

        void Update()
        {
            if (!ready) return;
            for (int i = 0; i < Presets.Length; i++)
                if (TestInput.DigitDown(i + 1)) Apply(Presets[i]);
            if (TestInput.DigitDown(0)) Apply(null);
        }

        void Apply(string preset)
        {
            try
            {
                object runtime = vrmInstance.GetType().GetProperty("Runtime", BindingFlags.Public | BindingFlags.Instance)?.GetValue(vrmInstance);
                object expression = runtime?.GetType().GetProperty("Expression", BindingFlags.Public | BindingFlags.Instance)?.GetValue(runtime);
                if (expression == null) { Debug.LogWarning("[VRMTest] VRM runtime is not ready yet."); return; }

                if (setWeight == null)
                    setWeight = expression.GetType().GetMethod("SetWeight", new[] { createFromPreset.ReturnType, typeof(float) });
                if (setWeight == null) { Debug.LogWarning("[VRMTest] SetWeight not found in this UniVRM version."); ready = false; return; }

                foreach (var p in Presets)
                {
                    try
                    {
                        object key = createFromPreset.Invoke(null, new[] { Enum.Parse(presetEnum, p, true) });
                        setWeight.Invoke(expression, new object[] { key, p == preset ? 1f : 0f });
                    }
                    catch (Exception) { /* model may not define this expression */ }
                }
                Current = preset ?? "neutral";
            }
            catch (Exception e)
            {
                Debug.LogWarning("[VRMTest] Expression change failed: " + e.Message);
            }
        }

        static Type FindType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }
    }
}
