using UnityEngine;

namespace Chef24
{
    /// <summary>
    /// Particle effects: steam and smoke.
    /// Uses the Kenney CC0 sprites in Assets/Chef24MVP/Vfx/Resources (materials "Vfx Steam", "Vfx Smoke",
    /// made by the scene builder). If those are missing it falls back to cartoon shapes drawn in code.
    /// </summary>
    public static class Vfx
    {
        static Material puffMaterial;

        public static readonly Color SteamColor = new Color(1f, 1f, 1f, 0.9f);
        public static readonly Color WarnSmokeColor = new Color(1f, 1f, 1f, 0.45f);    // black smoke sheet, see-through
        public static readonly Color BurntSmokeColor = new Color(1f, 1f, 1f, 0.95f);

        /// <summary>A looping column of cloud puffs rising from a rectangle (width x depth). Starts stopped.</summary>
        public static ParticleSystem Column(string name, Transform parent, Vector3 worldPos, Vector2 area, bool smoke = false)
        {
            Material sheet = Resources.Load<Material>(smoke ? "Vfx Smoke" : "Vfx Steam");
            var go = new GameObject(name);
            go.transform.SetParent(parent, true);
            go.transform.position = worldPos;
            go.transform.rotation = Quaternion.identity;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.3f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.24f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;
            main.startColor = SteamColor;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(area.x, area.y, 0.01f);
            shape.rotation = new Vector3(-90f, 0f, 0f);

            // Rise with a little sideways wobble.
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            vel.y = new ParticleSystem.MinMaxCurve(0.22f, 0.34f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            if (sheet != null)
            {
                // Kenney puffs: 2x2 sheet, a random frame each, grow and fade out softly.
                main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.32f);
                var frames = ps.textureSheetAnimation;
                frames.enabled = true;
                frames.mode = ParticleSystemAnimationMode.Grid;
                frames.numTilesX = 2;
                frames.numTilesY = 2;
                frames.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                frames.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(1f, 1.4f)));
                var col = ps.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                Setup(go.GetComponent<ParticleSystemRenderer>(), sheet);
            }
            else
            {
                // Fallback: pop in, swell, then shrink to nothing.
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 0.2f), new Keyframe(0.15f, 0.9f), new Keyframe(0.6f, 1.15f), new Keyframe(1f, 0f)));
                Setup(go.GetComponent<ParticleSystemRenderer>(), PuffMaterial());
            }
            return ps;
        }

        /// <summary>Turns a column on or off and sets how many puffs and what colour.</summary>
        public static void SetColumn(ParticleSystem ps, bool on, float rate, Color color)
        {
            if (ps == null) return;
            var main = ps.main;
            main.startColor = color;
            var emission = ps.emission;
            emission.rateOverTime = on ? rate : 0f;
            if (on && !ps.isPlaying) ps.Play();
        }

        static void Setup(ParticleSystemRenderer r, Material m)
        {
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.Distance;
        }

        // ---------------------------------------------------------------- textures (white fill, brown outline; tinted per particle)

        static readonly Color32 Outline = new Color32(74, 42, 23, 255);   // Palette.Brown
        const int TexSize = 128;
        const float OutlinePx = 7f;

        static Material PuffMaterial()
        {
            if (puffMaterial == null) puffMaterial = MakeMaterial("Vfx Puff", MakePuff());
            return puffMaterial;
        }

        static Material MakeMaterial(string name, Texture2D tex) =>
            new Material(Shader.Find("Sprites/Default")) { name = name, mainTexture = tex };

        /// <summary>
        /// Lumpy cartoon cloud (union of overlapping circles): white fill, a light shadow along the bottom, brown outline.
        /// </summary>
        static Texture2D MakePuff()
        {
            // (x, y, radius) in 0..1 texture units.
            Vector3[] blobs =
            {
                new Vector3(0.30f, 0.44f, 0.19f),
                new Vector3(0.50f, 0.58f, 0.27f),
                new Vector3(0.71f, 0.46f, 0.20f),
                new Vector3(0.50f, 0.36f, 0.20f),
            };
            var px = new Color32[TexSize * TexSize];
            var shadowCenter = new Vector2(0.47f, 0.62f) * TexSize;
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float edge = float.MinValue;                            // > 0 inside the union
                foreach (var b in blobs)
                    edge = Mathf.Max(edge, b.z * TexSize - Vector2.Distance(p, new Vector2(b.x, b.y) * TexSize));
                bool shaded = Vector2.Distance(p, shadowCenter) > 0.34f * TexSize && p.y < 0.5f * TexSize;
                Color fill = shaded ? new Color(0.82f, 0.82f, 0.86f) : Color.white;
                px[y * TexSize + x] = Shade(fill, edge);
            }
            return MakeTexture(px, "VfxPuff");
        }

        /// <summary>
        /// Colour for a pixel at signed distance <paramref name="edge"/> from the shape's border (positive = inside):
        /// fill in the middle, a brown band near the border, transparent outside, all anti-aliased.
        /// </summary>
        static Color32 Shade(Color fill, float edge)
        {
            float alpha = Mathf.Clamp01(edge + 0.5f);
            float ring = 1f - Mathf.Clamp01(edge - OutlinePx + 0.5f);   // 1 inside the outline band
            Color col = Color.Lerp(fill, Outline, ring);
            col.a = alpha;
            return col;
        }

        static Texture2D MakeTexture(Color32[] px, string name)
        {
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = name };
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
