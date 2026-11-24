using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Chef24.EditorTools
{
    /// <summary>
    /// Makes the TextMeshPro font assets the UI uses, from the OFL fonts in Assets/Chef24MVP/Fonts:
    /// Jua (body) and Black Han Sans (titles, big numbers). Dynamic atlases, so any Hangul used at runtime gets added.
    /// Also imports TMP Essential Resources (shaders, settings) the first time.
    /// </summary>
    public static class Chef24Fonts
    {
        const string Folder = "Assets/Chef24MVP/Fonts";

        public struct Set
        {
            public TMP_FontAsset body, title;
            public Material bodyOutline, titleOutline;
            public bool Ok => body != null && title != null;
        }

        public static Set Ensure()
        {
            var set = new Set();
            if (!EnsureEssentials()) return set;
            set.body = FontAsset("Jua-Regular.ttf", "Jua SDF");
            set.title = FontAsset("BlackHanSans-Regular.ttf", "BlackHanSans SDF");
            if (set.body != null) set.bodyOutline = OutlineMaterial(set.body, "Jua SDF Outline", 0.25f, false);
            if (set.title != null) set.titleOutline = OutlineMaterial(set.title, "BlackHanSans SDF Outline", 0.35f, true);
            AssetDatabase.SaveAssets();
            return set;
        }

        static bool EnsureEssentials()
        {
            if (Shader.Find("TextMeshPro/Mobile/Distance Field") != null && AssetDatabase.FindAssets("t:TMP_Settings").Length > 0) return true;
            string pkg = Path.GetFullPath("Packages/com.unity.textmeshpro/Package Resources/TMP Essential Resources.unitypackage");
            if (!File.Exists(pkg))
            {
                Debug.LogError("[Chef24] TMP Essential Resources package not found: " + pkg);
                return false;
            }
            AssetDatabase.ImportPackage(pkg, false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            bool ok = Shader.Find("TextMeshPro/Mobile/Distance Field") != null;
            if (!ok) Debug.LogWarning("[Chef24] TMP Essential Resources are importing. Run Build MVP Scene once more when it finishes.");
            return ok;
        }

        static TMP_FontAsset FontAsset(string ttf, string assetName)
        {
            string path = Folder + "/" + assetName + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null) return existing;

            var font = AssetDatabase.LoadAssetAtPath<Font>(Folder + "/" + ttf);
            if (font == null) { Debug.LogError("[Chef24] Missing font " + Folder + "/" + ttf); return null; }

            var fa = TMP_FontAsset.CreateFontAsset(font, 64, 12, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            fa.name = assetName;
            AssetDatabase.CreateAsset(fa, path);
            fa.atlasTextures[0].name = assetName + " Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = assetName + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            EditorUtility.SetDirty(fa);
            return fa;
        }

        /// <summary>Material preset with a thick brown outline (and a soft drop shadow for titles).</summary>
        static Material OutlineMaterial(TMP_FontAsset fa, string name, float width, bool shadow)
        {
            string path = Folder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var m = new Material(fa.material) { name = name };
            // The full SDF shader draws cleaner thick outlines than the mobile one.
            var full = Shader.Find("TextMeshPro/Distance Field");
            if (full != null) m.shader = full;
            m.EnableKeyword("OUTLINE_ON");
            // Material colours are not converted from sRGB for UI here (Linear project), so pass the linear value.
            m.SetColor("_OutlineColor", Palette.Brown.linear);
            m.SetFloat("_OutlineWidth", width);
            m.SetFloat("_FaceDilate", width * 0.5f);
            m.SetFloat("_OutlineSoftness", 0f);
            if (shadow)
            {
                m.EnableKeyword("UNDERLAY_ON");
                m.SetColor("_UnderlayColor", Palette.WithAlpha(Palette.Brown.linear, 0.55f));
                m.SetFloat("_UnderlayOffsetX", 0f);
                m.SetFloat("_UnderlayOffsetY", -0.9f);
                m.SetFloat("_UnderlayDilate", width);
                m.SetFloat("_UnderlaySoftness", 0.05f);
            }
            ShaderUtilities.UpdateShaderRatios(m);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
