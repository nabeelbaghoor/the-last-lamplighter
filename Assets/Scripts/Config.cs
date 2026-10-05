using UnityEngine;

namespace Lamplighter
{
    /// <summary>
    /// Layout and tuning. The level is authored in "design pixels" (1280x720 view, y down) so it
    /// matches the browser version exactly; <see cref="P"/> converts to Unity units (100 px = 1 unit, y up).
    /// </summary>
    public static class Config
    {
        public const float ViewW = 12.8f;
        public const float ViewH = 7.2f;
        public const float WorldW = 64f;

        public static Vector2 P(float x, float y) => new Vector2(x / 100f, (720f - y) / 100f);
        public static float U(float px) => px / 100f;

        // ---- movement (units / second)
        public const float Gravity = 15f;
        public const float RunSpeed = 2.7f;
        public const float RunAccel = 26f;
        public const float AirAccel = 17f;
        public const float Friction = 24f;
        public const float JumpVelocity = 6.4f;
        public const float JumpCutMultiplier = 0.45f;
        public const float CoyoteTime = 0.11f;
        public const float JumpBufferTime = 0.13f;
        public const float MaxFallSpeed = 9f;

        // ---- the flame is the player's light and health, 0..1
        public const float FlameDrainPerSec = 0.028f;
        public const float FlameRefillPerSec = 0.32f;
        public const float FlameStart = 0.85f;
        public const float FlameRespawn = 0.6f;
        public const float PickupFlame = 0.3f;
        public const float WispHitFlame = 0.22f;
        public const float FlareCost = 0.12f;
        public const float FlareCooldown = 1.1f;
        public const float InvulnTime = 1.1f;

        // ---- light radii (units)
        public const float LanternRadiusMin = 1.2f;
        public const float LanternRadiusMax = 2.5f;
        public const float LampRadius = 4.3f;
        public const float FlareRadius = 3.3f;

        public const float WispSenseRadius = 4.2f;
        public const float WispSpeed = 1.05f;
        public const float WispRespawnTime = 9f;

        // ---- sorting orders (gloom/lit stacks first, then the composite, then everything that is always lit)
        public static class Order
        {
            public const int Sky = 0, Far = 1, Houses = 2, GroundFill = 3, Ground = 4, Platforms = 5;
            public const int Composite = 10;
            public const int Stilts = 20, Water = 21;
            public const int Lamps = 40, Pickups = 45, Dust = 49, Player = 50, WispTrail = 54, Wisps = 55;
            public const int Fx = 60, Motes = 70, Vignette = 71, Bloom = 72;
        }

        public const int LightMapLayer = 8;
        public const int SolidLayer = 9;
        public const int LitLayer = 10;

        public static readonly Color Ink = new Color32(5, 6, 12, 255);
        public static Color Hex(uint rgb, float a = 1f) =>
            new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);
    }

    public enum PlatformKind { Ledge, Roof, Crate, Bridge }

    public struct PlatformDef
    {
        public float X, Y, W;
        public PlatformKind Kind;
        public PlatformDef(float x, float y, float w, PlatformKind kind) { X = x; Y = y; W = w; Kind = kind; }
    }

    public struct LampDef
    {
        public float X, Y;
        public bool Final;
        public LampDef(float x, float y, bool final = false) { X = x; Y = y; Final = final; }
    }

    public struct HintDef
    {
        public float X;
        public string Text;
        public HintDef(float x, string text) { X = x; Text = text; }
    }

    /// <summary>Hand-authored town, in design pixels. Platforms are described by their top surface.</summary>
    public static class Level
    {
        public const float GroundY = 640;

        /// <summary>Ground runs as (startX, endX); the gaps between them are canal pits.</summary>
        public static readonly Vector2[] Ground = { new Vector2(0, 2000), new Vector2(2120, 4200), new Vector2(5000, 6400) };

        public static readonly PlatformDef[] Platforms =
        {
            // Cottage lane
            new PlatformDef(940, 540, 190, PlatformKind.Crate),
            // Market street
            new PlatformDef(1580, 560, 150, PlatformKind.Crate),
            new PlatformDef(1770, 470, 170, PlatformKind.Crate),
            // Rooftops
            new PlatformDef(2950, 560, 210, PlatformKind.Roof),
            new PlatformDef(3200, 470, 210, PlatformKind.Roof),
            new PlatformDef(3450, 380, 220, PlatformKind.Roof),
            new PlatformDef(3720, 300, 260, PlatformKind.Roof),
            // Canal bridges
            new PlatformDef(4250, 560, 180, PlatformKind.Bridge),
            new PlatformDef(4500, 520, 210, PlatformKind.Bridge),
            new PlatformDef(4790, 560, 180, PlatformKind.Bridge),
            // Bell tower
            new PlatformDef(5690, 560, 170, PlatformKind.Ledge),
            new PlatformDef(5880, 470, 170, PlatformKind.Ledge),
            new PlatformDef(6060, 380, 170, PlatformKind.Ledge),
            new PlatformDef(6200, 290, 190, PlatformKind.Ledge),
        };

        public static readonly LampDef[] Lamps =
        {
            new LampDef(720, GroundY),
            new LampDef(2330, GroundY),
            new LampDef(3860, 300),
            new LampDef(4605, 520),
            new LampDef(5440, GroundY),
            new LampDef(6300, 290, final: true),
        };

        public static readonly Vector2[] Pickups =
        {
            new Vector2(1035, 490), new Vector2(2060, 520), new Vector2(2560, 580), new Vector2(3300, 420),
            new Vector2(3560, 330), new Vector2(4340, 500), new Vector2(4880, 500), new Vector2(5960, 420),
        };

        public static readonly Vector2[] Wisps =
        {
            new Vector2(1760, 380), new Vector2(2500, 420), new Vector2(3330, 260), new Vector2(3620, 200),
            new Vector2(4080, 460), new Vector2(4420, 380), new Vector2(4720, 330), new Vector2(4960, 410),
            new Vector2(5820, 300), new Vector2(6110, 210),
        };

        public static readonly HintDef[] Hints =
        {
            new HintDef(0, "Move with  A D  or the arrow keys      Jump with  Space"),
            new HintDef(470, "Touch a lamp with your lantern to light it"),
            new HintDef(1380, "Shadow wisps hunt your glow.  Press  X  to flare and burn them away"),
            new HintDef(2680, "Your flame fades in the dark.  Lamplight and embers restore it"),
            new HintDef(5200, "The bell tower.  Light every lamp to wake the town"),
        };

        public static readonly string[] LampBanners =
        {
            "The first light", "A street remembers its colour", "Warmth returns", "The canal glitters again", "Only the tower is left",
        };

        public static readonly Vector2 PlayerStart = new Vector2(170, GroundY - 80);
    }
}
