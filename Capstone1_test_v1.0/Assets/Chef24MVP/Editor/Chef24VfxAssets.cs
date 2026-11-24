using UnityEditor;
using UnityEngine;

namespace Chef24.EditorTools
{
    /// <summary>
    /// Makes the particle materials from the Kenney CC0 sprites in Assets/Chef24MVP/Vfx/Resources
    /// (steam / smoke sheets with 2x2 frames). Kept in Resources so <see cref="Vfx"/> can load them at runtime.
    /// </summary>
    public static class Chef24VfxAssets
    {
        const string Folder = "Assets/Chef24MVP/Vfx/Resources";

        public static void Ensure()
        {
            Make("Vfx Steam", "VfxSteamSheet.png");
            Make("Vfx Smoke", "VfxSmokeSheet.png");
            AssetDatabase.SaveAssets();
        }

        static void Make(string name, string texture)
        {
            string path = Folder + "/" + name + ".mat";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/" + texture);
            if (tex == null) { Debug.LogWarning("[Chef24] Missing VFX texture " + texture); return; }

            var importer = (TextureImporter)AssetImporter.GetAtPath(Folder + "/" + texture);
            if (importer != null && (importer.wrapMode != TextureWrapMode.Clamp || !importer.alphaIsTransparency))
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
                if (shader == null) { Debug.LogWarning("[Chef24] Particle shader not found"); return; }
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.mainTexture = tex;
            // Neutral tint for this shader (it doubles the tint): the colour must reach the shader as 0.5 in linear space,
            // and SetColor converts from sRGB in a Linear project, so pass the sRGB value of linear 0.5.
            float g = Mathf.LinearToGammaSpace(0.5f);
            mat.SetColor("_TintColor", new Color(g, g, g, 0.5f));
            EditorUtility.SetDirty(mat);
        }
    }
}
