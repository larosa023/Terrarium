using Terrarium.Core.Garden;
using Terrarium.Core.Model;

namespace Terrarium.Core.Rendering;

/// <summary>What one plant looks like right now. Growth is continuous 0..5 (see <see cref="Catalog.GrowthValue"/>).</summary>
public readonly record struct PlantView(string SpeciesId, double Growth, double Health, int Seed);

public sealed class SceneInput
{
    public PlantView?[] Plants { get; init; } = new PlantView?[Catalog.SlotCount];
    public string PotId { get; init; } = "terracotta";
    public IReadOnlyCollection<string> Decorations { get; init; } = [];
    /// <summary>Local time of day in hours, 0..24. Drives lighting.</summary>
    public double HourOfDay { get; init; } = 12;
    /// <summary>Animation clock in seconds.</summary>
    public double Time { get; init; }

    public static SceneInput FromState(GardenState s, double hourOfDay, double time) => new()
    {
        Plants = s.Slots.Select(p => p is null
            ? (PlantView?)null
            : new PlantView(p.SpeciesId, Catalog.GrowthValue(p.GrowthHours), p.Health, p.Seed)).ToArray(),
        PotId = s.PotId,
        Decorations = s.Decorations.ToHashSet(),
        HourOfDay = hourOfDay,
        Time = time,
    };
}

/// <summary>
/// Procedurally paints the terrarium as 64×80 pixel art. No image assets: every plant, pot and
/// decoration is drawn in code, so growth is smooth and every plant is slightly unique (seeded).
/// A frame is ~5k pixels and allocates almost nothing, so it costs well under a millisecond.
/// </summary>
public sealed class TerrariumRenderer
{
    public const int Width = 64;
    public const int Height = 80;

    // Layout
    private const int JarX0 = 4, JarX1 = 59, JarY0 = 16, JarY1 = 73;
    private const int NeckX0 = 20, NeckX1 = 43, NeckY0 = 10;
    private const int MossTop = 64;
    private const int PotTop = 56;
    public static readonly int[] SlotX = [17, 32, 47];

    private readonly bool[] _interior = new bool[Width * Height];
    private readonly bool[] _outline = new bool[Width * Height];

    public TerrariumRenderer()
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                _interior[y * Width + x] = InJarShape(x, y);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                _outline[y * Width + x] = _interior[y * Width + x] &&
                    (!Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1));
    }

    private bool Inside(int x, int y) => (uint)x < Width && (uint)y < Height && _interior[y * Width + x];

    private static bool InJarShape(int x, int y)
    {
        bool neck = x >= NeckX0 && x <= NeckX1 && y >= NeckY0 && y <= JarY0;
        return neck || InRoundRect(x, y, JarX0, JarY0, JarX1, JarY1, 8, 4);
    }

    private static bool InRoundRect(int x, int y, int x0, int y0, int x1, int y1, int rTop, int rBottom)
    {
        if (x < x0 || x > x1 || y < y0 || y > y1) return false;
        int r;
        double cy;
        if (y < y0 + rTop) { r = rTop; cy = y0 + rTop; }
        else if (y > y1 - rBottom) { r = rBottom; cy = y1 - rBottom; }
        else return true;
        double cx;
        if (x < x0 + r) cx = x0 + r;
        else if (x > x1 - r) cx = x1 - r;
        else return true;
        double dx = x - cx, dy = y - cy;
        return dx * dx + dy * dy <= r * r + r * 0.6;
    }

    public void Render(PixelCanvas c, SceneInput s)
    {
        if (c.Width != Width || c.Height != Height) throw new ArgumentException("Canvas must be 64x80.");
        c.Clear();
        var t = s.Time;
        var deco = s.Decorations;
        double night = NightAmount(s.HourOfDay);

        DrawGlassBack(c);
        DrawFloor(c);

        bool hasMoss = deco.Contains("moss");
        if (hasMoss) DrawMossMound(c);
        if (deco.Contains("mushroom")) DrawMushroom(c, hasMoss);
        if (deco.Contains("pebbles")) DrawPebbles(c);

        for (int i = 0; i < SlotX.Length && i < s.Plants.Length; i++)
        {
            if (s.Plants[i] is not PlantView p) continue;
            DrawFallenLeaves(c, SlotX[i], p);
            DrawPot(c, SlotX[i], s.PotId, t);
            DrawPlant(c, SlotX[i], PotTop, p, t);
        }

        if (deco.Contains("lantern")) DrawLantern(c, night);
        if (deco.Contains("snail")) DrawSnail(c, t);

        DrawSunBeams(c, s.HourOfDay, t);
        DrawGlassFront(c);
        DrawCorkAndStand(c);

        ApplyTimeOfDay(c, s.HourOfDay);

        // Light sources go on after tinting so they stay bright at night.
        if (night > 0.05)
        {
            if (deco.Contains("lantern")) DrawLanternGlow(c, night, t);
            if (deco.Contains("fireflies")) DrawFireflies(c, night, t);
        }
    }

    // ───────────────────────────── lighting ─────────────────────────────

    /// <summary>0 in daytime, 1 at night, ramps at dawn and dusk.</summary>
    public static double NightAmount(double h)
    {
        h = ((h % 24) + 24) % 24;
        if (h >= 7.5 && h <= 18.5) return 0;
        if (h >= 20.5 || h <= 5.5) return 1;
        if (h < 7.5) return (7.5 - h) / 2.0;      // 5.5..7.5 dawn
        return (h - 18.5) / 2.0;                  // 18.5..20.5 dusk
    }

    private static readonly (double H, double R, double G, double B, double S)[] Light =
    [
        (0, 60, 80, 170, 0.72),
        (5, 60, 80, 170, 0.72),
        (6.5, 255, 170, 120, 0.30),
        (8, 255, 255, 255, 0.0),
        (16.5, 255, 255, 255, 0.0),
        (18.5, 255, 160, 100, 0.30),
        (20, 110, 100, 180, 0.55),
        (22, 60, 80, 170, 0.72),
        (24, 60, 80, 170, 0.72),
    ];

    public static (double R, double G, double B) LightMultiplier(double hour)
    {
        hour = ((hour % 24) + 24) % 24;
        int k = 0;
        while (k < Light.Length - 2 && hour > Light[k + 1].H) k++;
        var a = Light[k];
        var b = Light[k + 1];
        double u = Math.Clamp((hour - a.H) / (b.H - a.H), 0, 1);
        double r = a.R + (b.R - a.R) * u, g = a.G + (b.G - a.G) * u, bl = a.B + (b.B - a.B) * u, s = a.S + (b.S - a.S) * u;
        return (1 + (r / 255.0 - 1) * s, 1 + (g / 255.0 - 1) * s, 1 + (bl / 255.0 - 1) * s);
    }

    private static void ApplyTimeOfDay(PixelCanvas c, double hour)
    {
        var (mr, mg, mb) = LightMultiplier(hour);
        if (Math.Abs(mr - 1) < 0.001 && Math.Abs(mg - 1) < 0.001 && Math.Abs(mb - 1) < 0.001) return;
        for (int i = 0; i < c.Pixels.Length; i++) c.Multiply(i, mr, mg, mb);
    }

    private void DrawSunBeams(PixelCanvas c, double hour, double t)
    {
        double day = 1 - NightAmount(hour);
        if (hour < 7 || hour > 18 || day <= 0) return;
        double strength = day * Math.Clamp(Math.Min(hour - 7, 18 - hour) / 2.0, 0, 1);
        double offset = (hour - 7) * 2.2 + Math.Sin(t * 0.25) * 0.6;
        for (int y = JarY0; y < MossTop; y++)
            for (int x = JarX0; x <= JarX1; x++)
            {
                if (!_interior[y * Width + x]) continue;
                double band = ((x + y * 0.55 - offset) % 24 + 24) % 24;
                if (band < 3.5) c.Blend(x, y, Col.Rgb(255, 246, 210, (int)(26 * strength)));
            }
    }

    // ───────────────────────────── glass & frame ─────────────────────────────

    private void DrawGlassBack(PixelCanvas c)
    {
        uint glass = Col.Rgb(196, 232, 226, 40);
        for (int i = 0; i < _interior.Length; i++)
            if (_interior[i]) c.Blend(i % Width, i / Width, glass);
    }

    private void DrawGlassFront(PixelCanvas c)
    {
        uint edge = Col.Rgb(118, 168, 166, 200);
        uint edgeLight = Col.Rgb(220, 246, 240, 90);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                if (_outline[i]) c.Blend(x, y, edge);
                else if (_interior[i] && (_outline[i - 1] || _outline[i + 1]) && y > JarY0 + 2 && y < JarY1 - 2) c.Blend(x, y, edgeLight);
            }

        // Reflections
        uint hi = Col.Rgb(255, 255, 255, 120), hiSoft = Col.Rgb(255, 255, 255, 55);
        c.VLine(7, 25, 56, hi);
        c.VLine(8, 28, 48, hiSoft);
        c.VLine(7, 59, 61, hiSoft);
        c.Blend(53, 21, Col.Rgb(255, 255, 255, 150));
        c.Blend(54, 22, Col.Rgb(255, 255, 255, 120));
        c.Blend(54, 23, hiSoft);
        c.HLine(14, 22, JarY1 - 1, Col.Rgb(255, 255, 255, 40));
        c.VLine(22, 12, 15, hiSoft);

        // Rim of the jar mouth
        c.HLine(NeckX0 - 1, NeckX1 + 1, NeckY0 + 1, Col.Rgb(150, 196, 194, 210));
        c.HLine(NeckX0, NeckX1, NeckY0 + 2, Col.Rgb(230, 250, 246, 80));
    }

    private static void DrawCorkAndStand(PixelCanvas c)
    {
        // Cork
        uint cork = Col.Rgb(168, 120, 78), corkLight = Col.Rgb(198, 152, 104), corkDark = Col.Rgb(122, 84, 54);
        for (int y = 4; y <= 10; y++)
            for (int x = 21; x <= 42; x++)
            {
                if (y == 4 && (x == 21 || x == 42)) continue;
                uint col = y == 4 ? corkLight : y == 10 ? corkDark : cork;
                if (y > 4 && y < 10 && Hash(x, y, 7) % 7 == 0) col = corkDark;
                if (x == 21 || x == 42) col = Col.Scale(col, 0.85);
                c.Blend(x, y, col);
            }

        // Wooden stand
        uint wTop = Col.Rgb(178, 126, 84), wMid = Col.Rgb(146, 98, 62), wDark = Col.Rgb(108, 70, 44);
        for (int y = 74; y <= 77; y++)
            for (int x = 1; x <= 62; x++)
            {
                if ((x == 1 || x == 62) && (y == 74 || y == 77)) continue;
                uint col = y == 74 ? wTop : y == 77 ? wDark : wMid;
                if (y is 75 or 76 && Hash(x / 3, y, 11) % 5 == 0) col = Col.Scale(wMid, 0.88);
                c.Blend(x, y, col);
            }
        c.FillRect(4, 78, 4, 2, wDark);
        c.FillRect(56, 78, 4, 2, wDark);
        // soft shadow where the glass meets the stand
        c.HLine(6, 57, 74, Col.Rgb(70, 46, 30, 70));
    }

    // ───────────────────────────── floor ─────────────────────────────

    private void DrawFloor(PixelCanvas c)
    {
        for (int y = MossTop - 1; y <= JarY1; y++)
            for (int x = JarX0; x <= JarX1; x++)
            {
                if (!_interior[y * Width + x]) continue;
                uint h = Hash(x, y, 3);
                int mossSurface = MossTop - (int)(Hash(x / 2, 0, 5) % 2);
                if (y < mossSurface) continue;
                uint col;
                if (y <= 65)
                {
                    col = (h % 3) switch
                    {
                        0 => Col.Rgb(104, 150, 74),
                        1 => Col.Rgb(86, 132, 64),
                        _ => Col.Rgb(120, 166, 84),
                    };
                    if (y == mossSurface && h % 4 == 0) col = Col.Rgb(146, 186, 96);
                }
                else if (y <= 69)
                {
                    col = h % 5 == 0 ? Col.Rgb(118, 84, 58) : h % 3 == 0 ? Col.Rgb(78, 54, 38) : Col.Rgb(94, 66, 46);
                }
                else
                {
                    col = (h % 4) switch
                    {
                        0 => Col.Rgb(170, 160, 146),
                        1 => Col.Rgb(140, 132, 122),
                        2 => Col.Rgb(196, 184, 164),
                        _ => Col.Rgb(120, 112, 104),
                    };
                }
                c.Blend(x, y, col);
            }
    }

    // ───────────────────────────── pots ─────────────────────────────

    private static (uint Body, uint Rim, uint Light, uint Shade) PotColors(string id) => id switch
    {
        "sage" => (Col.Rgb(132, 164, 128), Col.Rgb(152, 184, 146), Col.Rgb(184, 210, 172), Col.Rgb(98, 128, 96)),
        "indigo" => (Col.Rgb(58, 74, 140), Col.Rgb(74, 92, 162), Col.Rgb(108, 128, 196), Col.Rgb(40, 52, 104)),
        "gold" => (Col.Rgb(206, 166, 58), Col.Rgb(232, 196, 86), Col.Rgb(255, 236, 150), Col.Rgb(158, 120, 36)),
        _ => (Col.Rgb(196, 104, 64), Col.Rgb(214, 126, 84), Col.Rgb(232, 152, 110), Col.Rgb(156, 78, 48)),
    };

    private static void DrawPot(PixelCanvas c, int cx, string potId, double t)
    {
        var (body, rim, light, shade) = PotColors(potId);
        uint soil = Col.Rgb(70, 48, 34);
        int y0 = PotTop;
        // Rim (two rows) with soil showing inside
        for (int x = cx - 6; x <= cx + 5; x++)
        {
            bool edge = x == cx - 6 || x == cx + 5;
            c.Blend(x, y0, edge ? rim : soil);
            c.Blend(x, y0 + 1, x == cx - 5 ? light : x == cx + 5 ? shade : rim);
        }
        // Tapered body
        for (int y = y0 + 2; y <= y0 + 9; y++)
        {
            int k = (y - (y0 + 2)) / 4;
            int xl = cx - 5 + k, xr = cx + 4 - k;
            for (int x = xl; x <= xr; x++)
            {
                uint col = x == xl ? light : x >= xr - 1 ? shade : body;
                if (y == y0 + 2) col = Col.Scale(col, 0.82); // shadow under the rim
                if (y == y0 + 9) col = Col.Scale(col, 0.85);
                if (potId == "indigo" && (y == y0 + 4 || y == y0 + 5)) col = x == xl ? Col.Rgb(250, 242, 220) : Col.Rgb(232, 222, 196);
                c.Blend(x, y, col);
            }
        }
        if (potId == "gold")
        {
            double tw = (Math.Sin(t * 1.7 + cx) + 1) * 0.5;
            c.Blend(cx - 3, y0 + 4, Col.Rgb(255, 255, 230, (int)(120 + 120 * tw)));
        }
    }

    // ───────────────────────────── plants ─────────────────────────────

    private static readonly uint WiltTarget = Col.Rgb(164, 140, 66);

    private static uint Wilt(uint healthy, double health)
    {
        double w = Math.Clamp((1 - health) * 1.15, 0, 0.9);
        return Col.Lerp(healthy, Col.Scale(WiltTarget, 0.6 + 0.4 * (Col.G(healthy) / 200.0)), w);
    }

    private static double Sway(double t, int seed, double frac, double health) =>
        (Math.Sin(t * 0.9 + (seed % 100) * 0.13) * 1.1 + Math.Sin(t * 2.3 + (seed % 37)) * 0.25)
        * frac * (0.6 + 0.4 * health);

    private void DrawPlant(PixelCanvas c, int cx, int baseY, PlantView p, double t)
    {
        double g = Math.Clamp(p.Growth, 0, Catalog.MaxStage);
        double health = Math.Clamp(p.Health, 0, 1);
        switch (p.SpeciesId)
        {
            case "succulent": DrawSucculent(c, cx, baseY, g, health, p.Seed, t); break;
            case "cactus": DrawCactus(c, cx, baseY, g, health, p.Seed, t); break;
            case "daisy": DrawDaisy(c, cx, baseY, g, health, p.Seed, t); break;
            case "bonsai": DrawBonsai(c, cx, baseY, g, health, p.Seed, t); break;
            default: DrawFern(c, cx, baseY, g, health, p.Seed, t); break;
        }
    }

    private static void DrawSeedling(PixelCanvas c, int cx, int baseY, double g, double health, int seed, double t, uint stem, uint leaf)
    {
        // Stage 0: a seed in the soil, then a sprout poking out.
        if (g < 0.2)
        {
            c.Blend(cx, baseY - 1, Col.Rgb(120, 86, 56));
            c.Blend(cx - 1, baseY - 1, Col.Rgb(92, 64, 44));
            return;
        }
        int h = 1 + (int)((g - 0.2) / 0.8 * 3);
        double sw = Sway(t, seed, 0.4, health);
        for (int i = 0; i < h; i++) c.Blend(cx + (int)Math.Round(sw * i / Math.Max(1, h)), baseY - 1 - i, Wilt(stem, health));
        int tx = cx + (int)Math.Round(sw);
        int ty = baseY - h;
        c.Blend(tx - 1, ty, Wilt(leaf, health));
        c.Blend(tx + 1, ty, Wilt(leaf, health));
        if (h >= 3) { c.Blend(tx - 2, ty + (health < 0.5 ? 1 : 0), Wilt(leaf, health)); c.Blend(tx + 2, ty - (health < 0.5 ? 0 : 1), Wilt(leaf, health)); }
    }

    private static void DrawFern(PixelCanvas c, int cx, int baseY, double g, double health, int seed, double t)
    {
        uint light = Col.Rgb(128, 190, 94), mid = Col.Rgb(86, 152, 72), dark = Col.Rgb(54, 110, 56), rachis = Col.Rgb(66, 114, 52);
        if (g < 1)
        {
            DrawSeedling(c, cx, baseY, g, health, seed, t, rachis, light);
            return;
        }
        var rnd = new Random(seed);
        int n = Math.Min(7, 2 + (int)((g - 1) * 1.3));
        double maxLen = (8 + (g - 1) * 5.5) * (0.82 + 0.18 * health);
        double droop = 1 - health;
        for (int k = 0; k < n; k++)
        {
            double a = n == 1 ? 0 : -1 + 2.0 * k / (n - 1);
            a += (rnd.NextDouble() - 0.5) * 0.25;
            double absA = Math.Abs(a);
            double len = maxLen * (0.62 + 0.38 * (1 - absA)) * (0.9 + rnd.NextDouble() * 0.2);
            double dx = a * len * 0.65;
            double ctrlY = -len * (1 - 0.35 * absA) * (1 - 0.45 * droop);
            double tipY = ctrlY * (0.5 + 0.5 * (1 - absA)) + droop * len * (0.35 + 0.4 * absA);
            double sw = Sway(t, seed + k * 17, len / 30.0, health);
            double x0 = cx, y0 = baseY;
            double x1 = cx + dx * 0.2, y1 = baseY + ctrlY;
            double x2 = cx + dx + sw, y2 = baseY + tipY;
            int steps = Math.Max(4, (int)(len * 1.6));
            double px = x0, py = y0;
            for (int s = 1; s <= steps; s++)
            {
                double u = (double)s / steps;
                double om = 1 - u;
                double bx = om * om * x0 + 2 * om * u * x1 + u * u * x2;
                double by = om * om * y0 + 2 * om * u * y1 + u * u * y2;
                double tx = bx - px, ty = by - py;
                double tl = Math.Sqrt(tx * tx + ty * ty);
                if (tl > 0.001) { tx /= tl; ty /= tl; }
                c.Blend(bx, by, Wilt(rachis, health));
                if (s % 2 == 0 && u > 0.12)
                {
                    double leafLen = (0.6 + 2.6 * Math.Sin(Math.PI * Math.Min(1, u * 1.1))) * Math.Min(1, g / 3.5);
                    double nx = -ty, ny = tx;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        uint lc = Wilt(((s / 2 + side) & 1) == 0 ? mid : light, health - (side > 0 ? 0.03 : 0));
                        for (int j = 1; j <= (int)Math.Round(leafLen); j++)
                        {
                            double lx = bx + side * nx * j + tx * j * 0.45;
                            double ly = by + side * ny * j + ty * j * 0.45 + droop * j * 0.4;
                            c.Blend(lx, ly, j == 1 ? Wilt(dark, health) : lc);
                        }
                    }
                }
                px = bx;
                py = by;
            }
            // curled fiddlehead tip on young fronds
            if (g < 3.2 && k == n / 2)
            {
                c.Blend(x2 + 1, y2, Wilt(light, health));
                c.Blend(x2 + 1, y2 + 1, Wilt(mid, health));
            }
        }
    }

    private static void Capsule(PixelCanvas c, double x0, double y0, double x1, double y1, double r0, double r1, uint col)
    {
        double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        int steps = Math.Max(1, (int)(len * 2));
        for (int s = 0; s <= steps; s++)
        {
            double u = (double)s / steps;
            c.FillCircle(x0 + (x1 - x0) * u, y0 + (y1 - y0) * u, r0 + (r1 - r0) * u, col);
        }
    }

    private static void DrawSucculent(PixelCanvas c, int cx, int baseY, double g, double health, int seed, double t)
    {
        uint sage = Col.Rgb(140, 186, 152), light = Col.Rgb(180, 216, 184), dark = Col.Rgb(94, 140, 112), blush = Col.Rgb(226, 138, 150);
        if (g < 1)
        {
            if (g < 0.2) { DrawSeedling(c, cx, baseY, g, health, seed, t, dark, sage); return; }
            double rr = 0.8 + g * 1.4;
            c.FillEllipse(cx - 0.5, baseY - 1, rr, rr * 0.6, Wilt(sage, health));
            c.Blend(cx - 0.5, baseY - 1 - rr * 0.6, Wilt(light, health));
            return;
        }
        double R = (3 + (g - 1) * 1.55) * (0.85 + 0.15 * health);
        double droop = 1 - health;
        var rnd = new Random(seed);
        double ccx = cx - 0.5, ccy = baseY - 1.5;
        // three rings, back to front
        double[] ringScale = [1.0, 0.72, 0.42];
        int[] ringCount = [7, 5, 3];
        for (int ring = 0; ring < 3; ring++)
        {
            double r = R * ringScale[ring];
            if (r < 1.2) continue;
            int m = ringCount[ring];
            for (int k = 0; k < m; k++)
            {
                double u = m == 1 ? 0.5 : (double)k / (m - 1);
                double ang = -Math.PI * (0.06 + 0.88 * u) + (rnd.NextDouble() - 0.5) * 0.15; // upper half
                // wilting splays leaves outward and down
                double side = Math.Cos(ang);
                ang += droop * 0.7 * Math.Sign(side == 0 ? 1 : side) * (ring == 2 ? 0.4 : 1.0);
                double ex = ccx + Math.Cos(ang) * r, ey = ccy + Math.Sin(ang) * r * 0.85;
                uint col = ring == 0 ? dark : ring == 1 ? sage : light;
                Capsule(c, ccx, ccy, ex, ey, Math.Max(0.8, r * 0.32), 0.5, Wilt(col, health));
                if (g >= 3.5) c.Blend(ex, ey, Wilt(blush, health));
            }
        }
        c.FillCircle(ccx, ccy - R * 0.2, Math.Max(0.6, R * 0.18), Wilt(light, health));

        if (g >= 4.5)
        {
            double h = 6 + (g - 4.5) * 8;
            double sw = Sway(t, seed, 1.0, health);
            double tipX = ccx + 3 + sw, tipY = ccy - R * 0.4 - h + droop * h * 0.5;
            int steps = (int)(h * 1.5);
            for (int s = 0; s <= steps; s++)
            {
                double u = (double)s / steps;
                double om = 1 - u;
                double bx = om * om * ccx + 2 * om * u * (ccx + 1) + u * u * tipX;
                double by = om * om * (ccy - R * 0.3) + 2 * om * u * (tipY - 1) + u * u * tipY;
                c.Blend(bx, by, Wilt(Col.Rgb(150, 160, 104), health));
            }
            uint fl = Wilt(Col.Rgb(250, 146, 96), health), fl2 = Wilt(Col.Rgb(255, 196, 120), health);
            c.Blend(tipX, tipY - 1, fl);
            c.Blend(tipX + 1, tipY, fl2);
            c.Blend(tipX - 1, tipY, fl);
            c.Blend(tipX + 1, tipY - 2, fl2);
        }
    }

    private static void DrawCactus(PixelCanvas c, int cx, int baseY, double g, double health, int seed, double t)
    {
        uint body = Col.Rgb(88, 160, 96), light = Col.Rgb(132, 198, 122), dark = Col.Rgb(56, 116, 72), rib = Col.Rgb(72, 140, 82);
        uint spine = Col.Rgb(236, 232, 196);
        if (g < 1)
        {
            if (g < 0.2) { DrawSeedling(c, cx, baseY, g, health, seed, t, dark, body); return; }
            int nh = 1 + (int)(g * 3);
            for (int i = 0; i < nh; i++)
            {
                c.Blend(cx - 1, baseY - 1 - i, Wilt(light, health));
                c.Blend(cx, baseY - 1 - i, Wilt(body, health));
                if (i < nh - 1) c.Blend(cx + 1, baseY - 1 - i, Wilt(dark, health));
            }
            return;
        }
        double droop = 1 - health;
        int H = (int)((5 + (g - 1) * 5.6) * (0.9 + 0.1 * health));
        int halfW = g >= 3 ? 3 : 2;
        double lean = droop * 3 * ((seed & 1) == 0 ? 1 : -1);

        // arms first so the trunk overlaps their joints
        if (g >= 3)
            DrawCactusArm(c, cx, baseY, H, halfW, lean, -1, 0.42, (int)(3 + (g - 3) * 2.5), droop, health, body, light, dark, spine);
        if (g >= 3.8)
            DrawCactusArm(c, cx, baseY, H, halfW, lean, +1, 0.6, (int)(2 + (g - 3.8) * 3), droop, health, body, light, dark, spine);

        for (int yy = 0; yy < H; yy++)
        {
            double f = (double)yy / H;
            int xo = (int)Math.Round(lean * f);
            int y = baseY - 1 - yy;
            int hw = halfW;
            if (yy >= H - 2) hw = Math.Max(1, halfW - (yy - (H - 3)));
            for (int x = cx - hw; x <= cx + hw; x++)
            {
                uint col = x == cx - hw ? light : x == cx + hw ? dark : (x == cx - 1 || x == cx + 1) && halfW >= 3 ? rib : body;
                c.Blend(x + xo, y, Wilt(col, health));
            }
            if (yy % 3 == 1 && yy < H - 1)
            {
                c.Blend(cx - hw - 1 + xo, y, Col.WithAlpha(spine, 200));
                c.Blend(cx + hw + 1 + xo, y - 1, Col.WithAlpha(spine, 200));
            }
        }
        if (g >= 4.6)
        {
            int topX = cx + (int)Math.Round(lean);
            int topY = baseY - 1 - H;
            uint petal = Wilt(Col.Rgb(240, 120, 162), health), petal2 = Wilt(Col.Rgb(255, 166, 196), health);
            double open = Math.Clamp((g - 4.6) / 0.4, 0, 1);
            c.Blend(topX, topY, petal);
            c.Blend(topX - 1, topY, petal2);
            c.Blend(topX + 1, topY, petal2);
            if (open > 0.5)
            {
                c.Blend(topX - 1, topY - 1, petal);
                c.Blend(topX + 1, topY - 1, petal);
                c.Blend(topX, topY - 1, Col.Rgb(250, 214, 90));
                c.Blend(topX - 2, topY, petal);
                c.Blend(topX + 2, topY, petal);
            }
        }
    }

    private static void DrawCactusArm(PixelCanvas c, int cx, int baseY, int H, int halfW, double lean, int dir, double at,
        int upLen, double droop, double health, uint body, uint light, uint dark, uint spine)
    {
        int jy = baseY - 1 - (int)(H * at);
        int jx = cx + (int)Math.Round(lean * at);
        int outLen = 3;
        int startX = jx + dir * halfW;
        // horizontal segment
        for (int i = 0; i <= outLen; i++)
        {
            c.Blend(startX + dir * i, jy, Wilt(body, health));
            c.Blend(startX + dir * i, jy + 1, Wilt(dark, health));
        }
        int ex = startX + dir * outLen;
        // the vertical part goes up when healthy, sags down when wilted
        int vdir = droop > 0.55 ? 1 : -1;
        int len = droop > 0.55 ? Math.Max(1, upLen / 2) : upLen;
        for (int i = 1; i <= len; i++)
        {
            int y = jy + vdir * i;
            c.Blend(ex - 1, y, Wilt(dir < 0 ? light : body, health));
            c.Blend(ex, y, Wilt(body, health));
            c.Blend(ex + 1, y, Wilt(dir < 0 ? body : dark, health));
            if (i % 3 == 0) c.Blend(ex + dir * 2, y, Col.WithAlpha(spine, 200));
        }
        int tipY = jy + vdir * (len + 1);
        c.Blend(ex, tipY, Wilt(body, health));
    }

    private static void DrawLeaf(PixelCanvas c, double x, double y, int dir, int len, double droop, uint col, uint colDark)
    {
        double slope = -0.75 + 1.4 * droop; // negative = up
        for (int i = 1; i <= len; i++)
        {
            double lx = x + dir * i;
            double ly = y + slope * i + (i * i) * 0.06 * droop;
            c.Blend(lx, ly, i == 1 ? colDark : col);
            if (i > 1 && i < len) c.Blend(lx, ly - 1, col);
        }
    }

    private static void DrawDaisy(PixelCanvas c, int cx, int baseY, double g, double health, int seed, double t)
    {
        uint stem = Col.Rgb(92, 150, 70), leaf = Col.Rgb(112, 178, 86), leafDark = Col.Rgb(70, 128, 60);
        if (g < 1)
        {
            DrawSeedling(c, cx, baseY, g, health, seed, t, stem, leaf);
            return;
        }
        double droop = 1 - health;
        int stems = 1 + (g >= 2.5 ? 1 : 0) + (g >= 3.5 ? 1 : 0);
        double[] scale = [1.0, 0.8, 0.68];
        double[] leanArr = [-0.12, 0.28, -0.36];
        var rnd = new Random(seed);
        for (int k = stems - 1; k >= 0; k--)
        {
            double h = (8 + (g - 1) * 6) * scale[k] * (0.85 + 0.15 * health) * (0.95 + rnd.NextDouble() * 0.1);
            double lean = leanArr[k] * h;
            double sw = Sway(t, seed + k * 31, h / 26.0, health);
            double x0 = cx - 0.5 + (k == 1 ? 1 : k == 2 ? -1 : 0), y0 = baseY;
            double tipX = x0 + lean + sw + droop * lean * 0.6;
            double tipY = baseY - h + droop * h * 0.25;
            double x1 = x0 + lean * 0.1, y1 = baseY - h * 0.6;
            int steps = (int)(h * 1.6);
            uint sc = Wilt(stem, health);
            double lastX = x0, lastY = y0;
            for (int s = 1; s <= steps; s++)
            {
                double u = (double)s / steps;
                double om = 1 - u;
                double bx = om * om * x0 + 2 * om * u * x1 + u * u * tipX;
                double by = om * om * y0 + 2 * om * u * y1 + u * u * tipY;
                c.Blend(bx, by, sc);
                if (s == (int)(steps * 0.3) || s == (int)(steps * 0.55) || (s == (int)(steps * 0.75) && g > 3))
                {
                    int dir = (s / 3 + k) % 2 == 0 ? -1 : 1;
                    DrawLeaf(c, bx, by, dir, 2 + (int)Math.Min(2, g / 2), droop, Wilt(leaf, health), Wilt(leafDark, health));
                }
                lastX = bx;
                lastY = by;
            }
            // head
            double hx = lastX, hy = lastY - 1 + droop * 2;
            if (g >= 4.7)
            {
                double open = Math.Clamp((g - 4.7) / 0.3, 0.3, 1);
                uint petal = Col.Lerp(Col.Rgb(252, 250, 242), Col.Rgb(196, 176, 130), Math.Clamp(droop * 1.2, 0, 0.8));
                double pr = 1.4 + open;
                for (int a = 0; a < 8; a++)
                {
                    double ang = a * Math.PI / 4 + droop * 0.6;
                    c.Blend(hx + Math.Cos(ang) * pr, hy + Math.Sin(ang) * pr * (1 - droop * 0.4), petal);
                }
                c.Blend(hx, hy, Wilt(Col.Rgb(246, 196, 60), health));
                c.Blend(hx + 1, hy, Wilt(Col.Rgb(222, 160, 40), health));
            }
            else if (g >= 4.0)
            {
                c.Blend(hx, hy, Wilt(Col.Rgb(130, 180, 90), health));
                c.Blend(hx, hy - 1, Col.Rgb(236, 236, 220));
                c.Blend(hx + 1, hy, Wilt(leafDark, health));
            }
        }
    }

    private static void DrawBonsai(PixelCanvas c, int cx, int baseY, double g, double health, int seed, double t)
    {
        uint trunk = Col.Rgb(122, 86, 58), trunkLight = Col.Rgb(156, 112, 76), trunkDark = Col.Rgb(90, 62, 42);
        uint pad = Col.Rgb(72, 132, 72), padLight = Col.Rgb(114, 172, 92), padDark = Col.Rgb(46, 96, 58);
        if (g < 1)
        {
            DrawSeedling(c, cx, baseY, g, health, seed, t, trunk, padLight);
            return;
        }
        double droop = 1 - health;
        int Ht = (int)(5 + (g - 1) * 4);
        double thick = 1.0 + g * 0.35;
        double phase = (seed % 10) * 0.3;
        double sw = Sway(t, seed, 0.5, health);
        double Tx(double u) => cx - 0.5 + Math.Sin(u * Math.PI * 1.25 + phase) * 2.2 * u + sw * u;

        for (int i = 0; i <= Ht; i++)
        {
            double u = (double)i / Ht;
            double x = Tx(u);
            int y = baseY - i;
            double w = Math.Max(0.5, thick * (1 - u * 0.6));
            for (int dx = -(int)Math.Ceiling(w); dx <= (int)Math.Ceiling(w); dx++)
            {
                if (Math.Abs(dx) > w + 0.3) continue;
                uint col = dx < 0 ? trunkLight : dx > 0 ? trunkDark : trunk;
                c.Blend(x + dx, y, col);
            }
        }

        // branch tips and pad centres
        Span<(double X, double Y, double S)> pads = stackalloc (double, double, double)[3];
        int padCount = 0;
        double baseSize = 2.2 + g * 0.75;
        pads[padCount++] = (Tx(1), baseY - Ht - 1, 1.0);
        if (g >= 1.8)
        {
            double bu = 0.55, bx = Tx(bu), by = baseY - Ht * bu;
            double len = 3 + g * 0.9;
            c.Line(bx, by, bx - len, by - 1 + droop * 2, trunk);
            pads[padCount++] = (bx - len, by - 2 + droop * 2, 0.75);
        }
        if (g >= 2.8)
        {
            double bu = 0.78, bx = Tx(bu), by = baseY - Ht * bu;
            double len = 2 + g * 0.8;
            c.Line(bx, by, bx + len, by - 1 + droop * 2, trunk);
            pads[padCount++] = (bx + len, by - 2 + droop * 2, 0.65);
        }

        for (int i = 0; i < padCount; i++)
        {
            var (px, py, sc) = pads[i];
            double rx = baseSize * sc * 1.4, ry = baseSize * sc * 0.7;
            py += droop * 1.5;
            int x0 = (int)Math.Floor(px - rx - 1), x1 = (int)Math.Ceiling(px + rx + 1);
            int y0 = (int)Math.Floor(py - ry - 1), y1 = (int)Math.Ceiling(py + ry + 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    double nx = (x - px) / (rx + 0.3), ny = (y - py) / (ry + 0.3);
                    double d = nx * nx + ny * ny;
                    if (d > 1) continue;
                    uint h = Hash(x, y, seed);
                    // wilted pads thin out
                    if (h % 100 < droop * 45) continue;
                    uint col = ny < -0.25 && nx < 0.4 ? padLight : ny > 0.35 ? padDark : pad;
                    if (h % 9 == 0) col = padDark;
                    c.Blend(x, y, Wilt(col, health));
                }
            if (g >= 4.7)
            {
                uint blossom = Col.Lerp(Col.Rgb(250, 190, 214), WiltTarget, droop);
                for (int b = 0; b < 4; b++)
                {
                    uint h = Hash(i * 7 + b, b * 3, seed);
                    double bx = px + ((h % 100) / 100.0 - 0.5) * rx * 1.6;
                    double by = py + (((h / 100) % 100) / 100.0 - 0.6) * ry * 1.4;
                    c.Blend(bx, by, blossom);
                }
            }
        }
    }

    private static void DrawFallenLeaves(PixelCanvas c, int cx, PlantView p)
    {
        if (p.Health >= 0.45 || p.Growth < 1.5) return;
        int count = Math.Min(4, (int)((0.45 - p.Health) * 14) + 1);
        for (int i = 0; i < count; i++)
        {
            uint h = Hash(i, 99, p.Seed);
            int side = (h & 1) == 0 ? -1 : 1;
            int x = cx + side * (7 + (int)(h % 3));
            int y = MossTop - 1 + (int)((h >> 3) % 2);
            uint col = (h >> 5) % 2 == 0 ? Col.Rgb(186, 150, 70) : Col.Rgb(150, 112, 56);
            c.Blend(x, y, col);
            c.Blend(x + side, y, Col.Scale(col, 0.85));
        }
    }

    // ───────────────────────────── decorations ─────────────────────────────

    private static void DrawMossMound(PixelCanvas c)
    {
        for (int y = 57; y <= 64; y++)
            for (int x = 4; x <= 13; x++)
            {
                double nx = (x - 7.5) / 6.0, ny = (y - 64.5) / 7.0;
                if (nx * nx + ny * ny > 1) continue;
                uint h = Hash(x, y, 21);
                uint col = h % 3 == 0 ? Col.Rgb(92, 140, 66) : h % 3 == 1 ? Col.Rgb(108, 158, 76) : Col.Rgb(128, 174, 86);
                if (y <= 58 || ny < -0.75) col = Col.Rgb(150, 192, 100);
                c.Blend(x, y, col);
            }
    }

    private static void DrawMushroom(PixelCanvas c, bool onMoss)
    {
        int baseY = onMoss ? 58 : 63;
        int x = 8;
        uint stem = Col.Rgb(240, 230, 206), stemShade = Col.Rgb(206, 194, 170);
        c.VLine(x, baseY - 3, baseY, stem);
        c.VLine(x + 1, baseY - 3, baseY, stemShade);
        uint cap = Col.Rgb(206, 62, 54), capLight = Col.Rgb(232, 96, 80), capDark = Col.Rgb(160, 44, 40);
        int cy = baseY - 4;
        c.HLine(x - 2, x + 3, cy, cap);
        c.HLine(x - 1, x + 2, cy - 1, capLight);
        c.HLine(x, x + 1, cy - 2, capLight);
        c.Blend(x - 2, cy, capDark);
        c.Blend(x + 3, cy, capDark);
        c.Blend(x - 1, cy - 1, Col.Rgb(255, 250, 240));
        c.Blend(x + 2, cy, Col.Rgb(255, 250, 240));
        c.Blend(x + 1, cy - 2, Col.Rgb(255, 250, 240));
    }

    private static void DrawPebbles(PixelCanvas c)
    {
        ReadOnlySpan<int> px = [24, 39, 54, 29, 11];
        ReadOnlySpan<int> py = [64, 64, 64, 66, 65];
        for (int i = 0; i < px.Length; i++)
        {
            uint h = Hash(i, 5, 77);
            uint col = (h % 3) switch
            {
                0 => Col.Rgb(158, 160, 170),
                1 => Col.Rgb(186, 176, 160),
                _ => Col.Rgb(140, 150, 156),
            };
            c.Blend(px[i], py[i], col);
            c.Blend(px[i] + 1, py[i], Col.Scale(col, 0.85));
            c.Blend(px[i], py[i] - 1, Col.Lerp(col, Col.Rgb(255, 255, 255), 0.35));
        }
    }

    private static void DrawLantern(PixelCanvas c, double night)
    {
        uint frame = Col.Rgb(58, 50, 46), frameLight = Col.Rgb(96, 84, 76);
        uint glowDay = Col.Rgb(230, 206, 140), glowNight = Col.Rgb(255, 226, 130);
        uint glass = Col.Lerp(glowDay, glowNight, night);
        int x0 = 53, x1 = 57;
        c.Blend(55, 54, frameLight);       // handle
        c.HLine(54, 56, 55, frameLight);
        c.HLine(x0, x1, 56, frame);        // cap
        for (int y = 57; y <= 61; y++)
        {
            c.Blend(x0, y, frame);
            c.Blend(x1, y, frame);
            for (int x = x0 + 1; x < x1; x++) c.Blend(x, y, x == 55 && y == 59 ? Col.Rgb(255, 250, 220) : glass);
        }
        c.HLine(x0, x1, 62, frame);        // base
        c.HLine(x0 + 1, x1 - 1, 63, frameLight);
    }

    private static void DrawLanternGlow(PixelCanvas c, double night, double t)
    {
        double flicker = 0.85 + 0.15 * Math.Sin(t * 7.3) * Math.Sin(t * 3.1);
        for (int y = 50; y <= 70; y++)
            for (int x = 46; x <= 62; x++)
            {
                double dx = x - 55, dy = (y - 59) * 1.1;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d > 8) continue;
                int a = (int)(110 * night * flicker * (1 - d / 8));
                c.Add(x, y, Col.Rgb(255, 200, 110, a));
            }
        c.Add(55, 59, Col.Rgb(255, 240, 200, (int)(200 * night)));
    }

    private static void DrawSnail(PixelCanvas c, double t)
    {
        // Wanders back and forth along the front of the moss, very slowly.
        double span = 46;
        double pos = (t * 0.35) % (span * 2);
        int dir = pos < span ? 1 : -1;
        int x = 7 + (int)(pos < span ? pos : span * 2 - pos);
        int y = 66;
        uint body = Col.Rgb(206, 190, 160), bodyDark = Col.Rgb(170, 154, 128);
        uint shell = Col.Rgb(156, 100, 62), shellLight = Col.Rgb(200, 140, 86), shellDark = Col.Rgb(112, 70, 44);
        // body (foot)
        for (int i = -1; i <= 3; i++) c.Blend(x + dir * i, y, i == 3 ? bodyDark : body);
        // head and eye stalk
        c.Blend(x + dir * 3, y - 1, body);
        c.Blend(x + dir * 4, y - 2, bodyDark);
        // shell
        c.HLine(x - 1, x + 1, y - 1, shell);
        c.HLine(x - 1, x + 1, y - 2, shellLight);
        c.Blend(x, y - 3, shellLight);
        c.Blend(x - dir, y - 3, shell);
        c.Blend(x, y - 2, shellDark);
    }

    private void DrawFireflies(PixelCanvas c, double night, double t)
    {
        for (int i = 0; i < 4; i++)
        {
            double x = 32 + Math.Sin(t * 0.31 + i * 1.7) * 19 + Math.Sin(t * 0.73 + i) * 4;
            double y = 40 + Math.Sin(t * 0.23 + i * 2.3) * 13 + Math.Cos(t * 0.51 + i * 0.7) * 3;
            double blink = Math.Max(0, Math.Sin(t * 1.6 + i * 2.9));
            int ix = (int)Math.Round(x), iy = (int)Math.Round(y);
            if (!Inside(ix, iy)) continue;
            int a = (int)(255 * night * blink);
            if (a < 8) continue;
            c.Add(ix, iy, Col.Rgb(240, 255, 150, a));
            c.Add(ix + 1, iy, Col.Rgb(200, 255, 120, a / 4));
            c.Add(ix - 1, iy, Col.Rgb(200, 255, 120, a / 4));
            c.Add(ix, iy + 1, Col.Rgb(200, 255, 120, a / 4));
            c.Add(ix, iy - 1, Col.Rgb(200, 255, 120, a / 4));
        }
    }

    // ───────────────────────────── utils ─────────────────────────────

    public static uint Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)seed * 0x9E3779B1u;
            h ^= (uint)x * 0x85EBCA77u;
            h = (h << 13) | (h >> 19);
            h ^= (uint)y * 0xC2B2AE3Du;
            h *= 0x27D4EB2Fu;
            h ^= h >> 15;
            return h;
        }
    }
}
