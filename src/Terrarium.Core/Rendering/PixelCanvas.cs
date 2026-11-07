namespace Terrarium.Core.Rendering;

/// <summary>Packed colors are 0xAARRGGBB, straight (non-premultiplied) alpha.</summary>
public static class Col
{
    public static uint Rgb(int r, int g, int b, int a = 255) =>
        ((uint)Math.Clamp(a, 0, 255) << 24) | ((uint)Math.Clamp(r, 0, 255) << 16) | ((uint)Math.Clamp(g, 0, 255) << 8) | (uint)Math.Clamp(b, 0, 255);

    public static int A(uint c) => (int)(c >> 24);
    public static int R(uint c) => (int)((c >> 16) & 255);
    public static int G(uint c) => (int)((c >> 8) & 255);
    public static int B(uint c) => (int)(c & 255);

    public static uint WithAlpha(uint c, int a) => (c & 0x00FFFFFFu) | ((uint)Math.Clamp(a, 0, 255) << 24);

    public static uint Lerp(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Rgb(
            (int)Math.Round(R(a) + (R(b) - R(a)) * t),
            (int)Math.Round(G(a) + (G(b) - G(a)) * t),
            (int)Math.Round(B(a) + (B(b) - B(a)) * t),
            (int)Math.Round(A(a) + (A(b) - A(a)) * t));
    }

    public static uint Scale(uint c, double f) =>
        Rgb((int)(R(c) * f), (int)(G(c) * f), (int)(B(c) * f), A(c));
}

/// <summary>
/// A tiny software canvas. Pixels are stored premultiplied as 0xAARRGGBB, which in little-endian memory
/// is exactly WPF's Pbgra32 layout, so the buffer can be copied straight into a WriteableBitmap.
/// </summary>
public sealed class PixelCanvas
{
    public int Width { get; }
    public int Height { get; }
    public uint[] Pixels { get; }

    public PixelCanvas(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new uint[width * height];
    }

    public void Clear() => Array.Clear(Pixels);

    public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    /// <summary>Alpha-composites a straight-alpha color over the pixel ("source over").</summary>
    public void Blend(int x, int y, uint color)
    {
        if (!InBounds(x, y)) return;
        uint a = color >> 24;
        if (a == 0) return;
        int i = y * Width + x;
        uint r = (color >> 16) & 255, g = (color >> 8) & 255, b = color & 255;
        if (a == 255)
        {
            Pixels[i] = color;
            return;
        }
        uint d = Pixels[i];
        uint inv = 255 - a;
        uint da = d >> 24, dr = (d >> 16) & 255, dg = (d >> 8) & 255, db = d & 255;
        uint na = a + (da * inv + 127) / 255;
        uint nr = (r * a + 127) / 255 + (dr * inv + 127) / 255;
        uint ng = (g * a + 127) / 255 + (dg * inv + 127) / 255;
        uint nb = (b * a + 127) / 255 + (db * inv + 127) / 255;
        Pixels[i] = (Math.Min(na, 255u) << 24) | (Math.Min(nr, 255u) << 16) | (Math.Min(ng, 255u) << 8) | Math.Min(nb, 255u);
    }

    public void Blend(double x, double y, uint color) => Blend((int)Math.Round(x), (int)Math.Round(y), color);

    /// <summary>Adds light (premultiplied additive), used for glows.</summary>
    public void Add(int x, int y, uint color)
    {
        if (!InBounds(x, y)) return;
        uint a = color >> 24;
        if (a == 0) return;
        int i = y * Width + x;
        uint d = Pixels[i];
        uint r = ((color >> 16) & 255) * a / 255, g = ((color >> 8) & 255) * a / 255, b = (color & 255) * a / 255;
        uint na = Math.Min(255u, (d >> 24) + a / 2);
        uint nr = Math.Min(na, ((d >> 16) & 255) + r);
        uint ng = Math.Min(na, ((d >> 8) & 255) + g);
        uint nb = Math.Min(na, (d & 255) + b);
        Pixels[i] = (na << 24) | (nr << 16) | (ng << 8) | nb;
    }

    /// <summary>Multiplies the pixel's color channels (alpha unchanged). Used for time-of-day light.</summary>
    public void Multiply(int i, double mr, double mg, double mb)
    {
        uint d = Pixels[i];
        if (d >> 24 == 0) return;
        uint r = (uint)Math.Min(255, ((d >> 16) & 255) * mr);
        uint g = (uint)Math.Min(255, ((d >> 8) & 255) * mg);
        uint b = (uint)Math.Min(255, (d & 255) * mb);
        Pixels[i] = (d & 0xFF000000u) | (r << 16) | (g << 8) | b;
    }

    public void FillRect(int x, int y, int w, int h, uint color)
    {
        for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
                Blend(xx, yy, color);
    }

    public void HLine(int x0, int x1, int y, uint color)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        for (int x = x0; x <= x1; x++) Blend(x, y, color);
    }

    public void VLine(int x, int y0, int y1, uint color)
    {
        if (y1 < y0) (y0, y1) = (y1, y0);
        for (int y = y0; y <= y1; y++) Blend(x, y, color);
    }

    public void Line(double fx0, double fy0, double fx1, double fy1, uint color)
    {
        int x0 = (int)Math.Round(fx0), y0 = (int)Math.Round(fy0), x1 = (int)Math.Round(fx1), y1 = (int)Math.Round(fy1);
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            Blend(x0, y0, color);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    public void FillCircle(double cx, double cy, double r, uint color)
    {
        if (r < 0.75)
        {
            Blend(cx, cy, color);
            return;
        }
        int x0 = (int)Math.Floor(cx - r), x1 = (int)Math.Ceiling(cx + r);
        int y0 = (int)Math.Floor(cy - r), y1 = (int)Math.Ceiling(cy + r);
        double rr = r * r + r * 0.4;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy <= rr) Blend(x, y, color);
            }
    }

    public void FillEllipse(double cx, double cy, double rx, double ry, uint color)
    {
        if (rx < 0.6 || ry < 0.6)
        {
            Blend(cx, cy, color);
            return;
        }
        int x0 = (int)Math.Floor(cx - rx), x1 = (int)Math.Ceiling(cx + rx);
        int y0 = (int)Math.Floor(cy - ry), y1 = (int)Math.Ceiling(cy + ry);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double dx = (x - cx) / (rx + 0.3), dy = (y - cy) / (ry + 0.3);
                if (dx * dx + dy * dy <= 1.0) Blend(x, y, color);
            }
    }

    /// <summary>Returns the pixel as straight-alpha 0xAARRGGBB (for PNG export and tests).</summary>
    public uint GetStraight(int x, int y)
    {
        uint d = Pixels[y * Width + x];
        uint a = d >> 24;
        if (a == 0) return 0;
        if (a == 255) return d;
        uint r = Math.Min(255u, ((d >> 16) & 255) * 255 / a);
        uint g = Math.Min(255u, ((d >> 8) & 255) * 255 / a);
        uint b = Math.Min(255u, (d & 255) * 255 / a);
        return (a << 24) | (r << 16) | (g << 8) | b;
    }
}
