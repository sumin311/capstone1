using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Chef24.EditorTools
{
    /// <summary>
    /// Converts the scene's materials to the Chef24/Toon shader (one toon family for the whole frame).
    /// Keeps each material's colour and texture. Big flat objects (walls, floor, counters, ground) get no outline,
    /// because hull outlines look wrong on large planes. Transparent and emissive materials are left alone.
    /// Customers keep their VRM MToon shader, which is already toon shaded.
    /// </summary>
    public static class Chef24Toonify
    {
        const string Folder = "Assets/Chef24MVP/Materials/Toon";
        const string MapPath = Folder + "/Toon Material Map.asset";
        const float FlatSize = 2.2f;

        public static ToonMaterialMap Apply(IEnumerable<GameObject> roots, IEnumerable<GameObject> extraModels)
        {
            var shader = Shader.Find("Chef24/Toon");
            if (shader == null)
            {
                Debug.LogWarning("[Chef24] Chef24/Toon shader not found; materials left as they are.");
                return null;
            }
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Chef24MVP/Materials", "Toon");

            var map = AssetDatabase.LoadAssetAtPath<ToonMaterialMap>(MapPath);
            if (map == null)
            {
                map = ScriptableObject.CreateInstance<ToonMaterialMap>();
                AssetDatabase.CreateAsset(map, MapPath);
            }

            int converted = 0;
            foreach (var root in roots)
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (Skip(r)) continue;
                Vector3 size = r.bounds.size;
                bool flat = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) > FlatSize;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var toon = ToonFor(mats[i], flat, shader, map);
                    if (toon != null) { mats[i] = toon; converted++; }
                }
                r.sharedMaterials = mats;
            }

            // Models that only appear at runtime (held dish): register their materials so the map can swap them.
            foreach (var model in extraModels)
            {
                if (model == null) continue;
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials) ToonFor(m, false, shader, map);
            }

            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();
            Debug.Log("[Chef24] Toon materials applied to " + converted + " material slots.");
            return map;
        }

        static bool Skip(Renderer r)
        {
            if (r is ParticleSystemRenderer || r is SkinnedMeshRenderer) return true;
            if (r.GetComponent<TMPro.TMP_Text>() != null) return true;
            return false;
        }

        static Material ToonFor(Material src, bool flat, Shader shader, ToonMaterialMap map)
        {
            if (src == null || src.shader == shader) return null;
            if (src.renderQueue >= 2450) return null;                      // transparent / cut-out
            if (src.IsKeywordEnabled("_EMISSION")) return null;            // light panels stay glowing
            if (!flat)
            {
                var known = map.Find(src);
                if (known != null) return known;
            }

            string key = Sanitize(src.name) + "_" + Hash(AssetDatabase.GetAssetPath(src) + "/" + src.name) + (flat ? " Flat" : "");
            string path = Folder + "/" + key + ".mat";
            var toon = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (toon == null)
            {
                toon = new Material(shader) { name = key };
                AssetDatabase.CreateAsset(toon, path);
            }
            toon.shader = shader;
            toon.SetColor("_Color", src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white);
            toon.SetTexture("_MainTex", src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null);
            if (src.HasProperty("_MainTex"))
            {
                toon.SetTextureScale("_MainTex", src.GetTextureScale("_MainTex"));
                toon.SetTextureOffset("_MainTex", src.GetTextureOffset("_MainTex"));
            }
            toon.SetColor("_OutlineColor", Palette.Brown);
            toon.SetFloat("_OutlineWidth", flat ? 0f : 2f);
            EditorUtility.SetDirty(toon);
            if (!flat) map.Set(src, toon);
            return toon;
        }

        /// <summary>A soft palette sky instead of the default procedural blue (the default breaks the style).</summary>
        public static void ApplySky()
        {
            string path = Folder + "/Toon Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (sky == null)
            {
                var sh = Shader.Find("Skybox/Procedural");
                if (sh == null) return;
                sky = new Material(sh) { name = "Toon Sky" };
                AssetDatabase.CreateAsset(sky, path);
            }
            sky.SetFloat("_SunDisk", 0f);
            sky.SetFloat("_SunSize", 0f);
            sky.SetFloat("_AtmosphereThickness", 0.55f);
            sky.SetColor("_SkyTint", new Color(0.55f, 0.78f, 0.95f));
            sky.SetColor("_GroundColor", new Color(0.86f, 0.8f, 0.68f));
            sky.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
        }

        static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Length > 40 ? s.Substring(0, 40) : s;
        }

        static string Hash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in s) h = (h ^ c) * 16777619;
                return h.ToString("x8").Substring(0, 6);
            }
        }
    }
}
