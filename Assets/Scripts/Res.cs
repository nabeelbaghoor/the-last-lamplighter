using System.Collections.Generic;
using UnityEngine;

namespace Lamplighter
{
    /// <summary>Cached access to everything under Resources/, plus small sprite helpers.</summary>
    public static class Res
    {
        private static readonly Dictionary<string, Object> Cache = new Dictionary<string, Object>();

        private static T Load<T>(string path) where T : Object
        {
            if (Cache.TryGetValue(path, out var o) && o != null) return (T)o;
            var asset = Resources.Load<T>(path);
            if (asset == null) Debug.LogError("Lamplighter: missing resource " + path);
            Cache[path] = asset;
            return asset;
        }

        public static Sprite Sprite(string path) => Load<Sprite>("Art/" + path);
        public static AudioClip Clip(string name) => Load<AudioClip>("Audio/" + name);
        public static Font Font(string name) => Load<Font>("Fonts/" + name);
        public static RuntimeAnimatorController HeroController => Load<RuntimeAnimatorController>("Hero/Hero");

        private static Material _additive, _brush, _composite;
        public static Material Additive => _additive ? _additive : _additive = new Material(Load<Shader>("Shaders/SpriteAdditive"));
        public static Material Brush => _brush ? _brush : _brush = new Material(Load<Shader>("Shaders/LightBrush"));
        public static Material Composite => _composite ? _composite : _composite = new Material(Load<Shader>("Shaders/RevealComposite"));

        public static Font Title => Font("Cinzel-Bold");
        public static Font Body => Font("CormorantGaramond-Medium");
        public static Font BodyBold => Font("CormorantGaramond-Bold");
        public static Font BodyItalic => Font("CormorantGaramond-MediumItalic");

        /// <summary>Creates a sprite object. Sprites are imported at 100 px per unit with a centred pivot.</summary>
        public static SpriteRenderer MakeSprite(string name, Sprite sprite, Transform parent, int order, int layer = 0)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        public static SpriteRenderer MakeGlow(Transform parent, Color tint, float scale, int order = Config.Order.Fx)
        {
            var sr = MakeSprite("glow", Sprite("fx/glow"), parent, order);
            sr.sharedMaterial = Additive;
            sr.color = tint;
            sr.transform.localScale = Vector3.one * scale;
            return sr;
        }

        /// <summary>
        /// Places a centred-pivot sprite like a Phaser image: (x, y) is in design pixels and
        /// (originX, originY) is the anchor inside the image, top-left = (0, 0).
        /// </summary>
        public static void PlacePx(SpriteRenderer sr, float x, float y, float originX, float originY)
        {
            var local = sr.drawMode == SpriteDrawMode.Simple ? (Vector2)sr.sprite.bounds.size : sr.size;
            var size = Vector2.Scale(local, sr.transform.lossyScale);
            var anchor = Config.P(x, y);
            sr.transform.position = new Vector3(anchor.x + (0.5f - originX) * size.x, anchor.y - (0.5f - originY) * size.y, 0f);
        }

        /// <summary>A solid-colour rectangle in design pixels (top-left x, y).</summary>
        public static SpriteRenderer RectPx(string name, Transform parent, float x, float y, float w, float h, Color color, int order, int layer = 0)
        {
            var sr = MakeSprite(name, Sprite("fx/white"), parent, order, layer);
            sr.color = color;
            sr.transform.localScale = new Vector3(Config.U(w) / 0.08f, Config.U(h) / 0.08f, 1f);
            PlacePx(sr, x, y, 0, 0);
            return sr;
        }
    }
}
