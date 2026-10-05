using UnityEngine;

namespace Lamplighter
{
    /// <summary>Builds the town: parallax backdrop, ground and platforms (each as a gloom/lit pair), colliders, water.</summary>
    public static class World
    {
        private const float SkySF = 0.05f, FarSF = 0.2f, MidSF = 0.5f;
        private static readonly string[] Houses = { "house_a", "house_b", "house_c" };

        public static Transform Build(CameraRig rig)
        {
            var root = new GameObject("World").transform;
            BuildBackdrop(root, rig);
            BuildSolids(root);
            return root;
        }

        /// <summary>
        /// Adds a lit/gloom pair of the same painting at the same spot. The gloom copy renders in the
        /// main view; the lit copy lives on the Lit layer and only shows where the light map lets it through.
        /// </summary>
        private static void Pair(Transform parent, string key, float x, float y, int order, float originX = 0f, float originY = 0f,
            float scale = 1f, bool flipX = false, float w = 0f, float h = 0f)
        {
            foreach (var (suffix, layer) in new[] { ("_gloom", 0), ("_lit", Config.LitLayer) })
            {
                var sprite = Res.Sprite(key + suffix);
                var sr = Res.MakeSprite(key + suffix, sprite, parent, order, layer);
                if (w > 0f && h > 0f)
                {
                    var size = sprite.bounds.size;
                    sr.transform.localScale = new Vector3(Config.U(w) / size.x, Config.U(h) / size.y, 1f);
                }
                else sr.transform.localScale = new Vector3(scale, scale, 1f);
                sr.flipX = flipX;
                Res.PlacePx(sr, x, y, originX, originY);
            }
        }

        private static Transform Layer(Transform root, string name, CameraRig rig, float factor)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            if (factor < 1f) rig.AddParallax(t, factor);
            return t;
        }

        private static void BuildBackdrop(Transform root, CameraRig rig)
        {
            const float viewW = 1280f, viewH = 720f, worldW = 6400f;

            // Sky: one painting with slow parallax.
            var sky = Layer(root, "Sky", rig, SkySF);
            var skyTex = Res.Sprite("bg/sky_lit").rect.size;
            float skyNeedW = viewW + (worldW - viewW) * SkySF;
            float skyScale = Mathf.Max(skyNeedW / skyTex.x, viewH / skyTex.y);
            Pair(sky, "bg/sky", 0, viewH, Config.Order.Sky, 0f, 1f, skyScale);

            // Far town: mirrored copies side by side, so the seams always match.
            var far = Layer(root, "FarTown", rig, FarSF);
            var farTex = Res.Sprite("bg/far_lit").rect.size;
            float farScale = viewH * 0.82f / farTex.y;
            float farW = farTex.x * farScale;
            float farNeedW = viewW + (worldW - viewW) * FarSF;
            int i = 0;
            for (float x = 0; x < farNeedW; x += farW, i++)
                Pair(far, "bg/far", x, viewH + 10, Config.Order.Far, 0f, 1f, farScale, i % 2 == 1);

            // Mid-ground houses, laid out with a fixed seed so the town is the same every run.
            var mid = Layer(root, "Houses", rig, MidSF);
            var rng = new System.Random(1789);
            float midNeedW = viewW + (worldW - viewW) * MidSF;
            float hx = -60f;
            for (int k = 0; hx < midNeedW; k++)
            {
                string house = Houses[k % Houses.Length];
                var tex = Res.Sprite($"bg/{house}_lit").rect.size;
                float h = rng.Next(330, 431);
                float s = h / tex.y;
                Pair(mid, "bg/" + house, hx, Level.GroundY + 34, Config.Order.Houses, 0f, 1f, s, rng.NextDouble() < 0.5);
                hx += tex.x * s * (0.72f + (float)rng.NextDouble() * 0.23f);
            }

            // Canal water in the pits between ground runs (always drawn, above the reveal).
            var water = Layer(root, "Water", rig, 1f);
            for (int k = 0; k < Level.Ground.Length - 1; k++)
            {
                float x0 = Level.Ground[k].y, x1 = Level.Ground[k + 1].x;
                var sr = Res.MakeSprite("water", Res.Sprite("fx/water"), water, Config.Order.Water);
                var size = sr.sprite.bounds.size;
                sr.transform.localScale = new Vector3(Config.U(x1 - x0) / size.x, Config.U(viewH - Level.GroundY) / size.y, 1f);
                Res.PlacePx(sr, x0, Level.GroundY + 40, 0f, 0f);
                for (float sx = x0 + 10; sx < x1; sx += 46)
                    Res.RectPx("ripple", water, sx, Level.GroundY + 52 + (sx * 7) % 13, 22, 2, Config.Hex(0x6f8cc9, 0.18f), Config.Order.Water);
            }
        }

        private static void BuildSolids(Transform root)
        {
            var solids = new GameObject("Solids").transform;
            solids.SetParent(root, false);
            var art = new GameObject("Street").transform;
            art.SetParent(root, false);

            var groundTex = Res.Sprite("props/ground_lit").rect.size;
            float gScale = 96f / groundTex.y;
            foreach (var run in Level.Ground)
            {
                float x0 = run.x, w = run.y - run.x;
                foreach (var (suffix, layer) in new[] { ("_gloom", 0), ("_lit", Config.LitLayer) })
                {
                    // A tiled street strip.
                    var sr = Res.MakeSprite("ground" + suffix, Res.Sprite("props/ground" + suffix), art, Config.Order.Ground, layer);
                    sr.drawMode = SpriteDrawMode.Tiled;
                    sr.tileMode = SpriteTileMode.Continuous;
                    sr.transform.localScale = new Vector3(gScale, gScale, 1f);
                    sr.size = new Vector2(Config.U(w) / gScale, Config.U(groundTex.y));
                    Res.PlacePx(sr, x0, Level.GroundY - 10, 0f, 0f);
                    // Fill below the street so nothing shows through at the bottom edge.
                    Res.RectPx("fill" + suffix, art, x0, Level.GroundY + 80, w, 200,
                        suffix == "_lit" ? Config.Hex(0x2a1d17) : Config.Hex(0x0b0d16), Config.Order.GroundFill, layer);
                }
                AddSolid(solids, x0, Level.GroundY, w, 720 - Level.GroundY + 400, false);
            }

            foreach (var p in Level.Platforms) BuildPlatform(root, art, solids, p);
        }

        private static void AddSolid(Transform parent, float x, float y, float w, float h, bool oneWay)
        {
            var go = new GameObject(oneWay ? "OneWay" : "Solid") { layer = Config.SolidLayer };
            go.transform.SetParent(parent, false);
            var c = Config.P(x + w / 2f, y + h / 2f);
            go.transform.position = c;
            var box = go.AddComponent<BoxCollider2D>();
            box.size = new Vector2(Config.U(w), Config.U(h));
            if (oneWay)
            {
                box.usedByEffector = true;
                var eff = go.AddComponent<PlatformEffector2D>();
                eff.useOneWay = true;
                eff.surfaceArc = 160f;
                eff.useSideFriction = false;
                eff.useSideBounce = false;
            }
        }

        private static void BuildPlatform(Transform root, Transform art, Transform solids, PlatformDef p)
        {
            if (p.Kind == PlatformKind.Crate)
            {
                // Crates are stacked down to the street and are solid on every side.
                const float size = 76f;
                int cols = Mathf.Max(1, Mathf.RoundToInt(p.W / size));
                int rows = Mathf.Max(1, Mathf.RoundToInt((Level.GroundY - p.Y) / size));
                float cw = p.W / cols, ch = (Level.GroundY - p.Y) / rows;
                for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    Pair(art, "props/plat_crate", p.X + c * cw, p.Y + r * ch, Config.Order.Platforms, 0f, 0f, 1f, (r + c) % 2 == 1, cw + 1, ch + 1);
                AddSolid(solids, p.X, p.Y, p.W, Level.GroundY - p.Y, false);
                return;
            }

            string key = "props/plat_" + p.Kind.ToString().ToLowerInvariant();
            var tex = Res.Sprite(key + "_lit").rect.size;
            float scale = p.W / tex.x;
            // The top 18% of a platform painting is its walkable surface.
            float top = p.Y - tex.y * scale * 0.18f;
            Pair(art, key, p.X, top, Config.Order.Platforms, 0f, 0f, scale);

            if (p.Kind == PlatformKind.Roof || p.Kind == PlatformKind.Ledge)
            {
                // Wooden stilts down to the street keep high walkways from floating.
                float legTop = top + tex.y * scale * 0.7f;
                foreach (var lx in new[] { p.X + p.W * 0.18f, p.X + p.W * 0.82f })
                    Res.RectPx("stilt", art, lx - 5, legTop, 10, Level.GroundY - legTop + 6, Config.Hex(0x140f12), Config.Order.Stilts);
            }
            AddSolid(solids, p.X, p.Y, p.W, 22, true);
        }
    }
}
