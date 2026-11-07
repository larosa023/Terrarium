namespace Terrarium.Core.Rendering;

/// <summary>Hand-placed pixel icons (16×16 and 32×32) for the tray and the .exe.</summary>
public static class IconArt
{
    private static readonly uint Glass = Col.Rgb(206, 238, 232, 150);
    private static readonly uint Edge = Col.Rgb(96, 150, 148);
    private static readonly uint Cork = Col.Rgb(176, 126, 82);
    private static readonly uint CorkDark = Col.Rgb(126, 86, 54);
    private static readonly uint Soil = Col.Rgb(98, 68, 46);
    private static readonly uint Moss = Col.Rgb(104, 160, 76);
    private static readonly uint Leaf = Col.Rgb(126, 196, 96);
    private static readonly uint LeafDark = Col.Rgb(70, 140, 70);
    private static readonly uint Shine = Col.Rgb(255, 255, 255, 200);

    public static PixelCanvas Render(int size)
    {
        var c = new PixelCanvas(size, size);
        if (size >= 32) Draw32(c); else Draw16(c);
        return c;
    }

    private static void Jar(PixelCanvas c, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                bool corner = (y == y0 || y == y1) && (x == x0 || x == x1);
                if (corner) continue;
                bool edge = x == x0 || x == x1 || y == y0 || y == y1;
                c.Blend(x, y, edge ? Edge : Glass);
            }
    }

    private static void Draw16(PixelCanvas c)
    {
        Jar(c, 2, 4, 13, 15);
        c.FillRect(5, 1, 6, 3, Cork);
        c.HLine(5, 10, 3, CorkDark);
        c.HLine(3, 12, 12, Moss);
        c.FillRect(3, 13, 10, 2, Soil);
        // sprout
        c.VLine(8, 8, 11, LeafDark);
        c.Blend(6, 9, Leaf); c.Blend(7, 9, Leaf); c.Blend(7, 8, Leaf);
        c.Blend(9, 7, Leaf); c.Blend(10, 7, Leaf); c.Blend(10, 6, Leaf); c.Blend(9, 8, LeafDark);
        c.VLine(4, 6, 10, Shine);
    }

    private static void Draw32(PixelCanvas c)
    {
        Jar(c, 4, 8, 27, 30);
        // round the shoulders
        foreach (var (x, y) in new[] { (5, 9), (26, 9), (5, 29), (26, 29) }) c.Blend(x, y, Edge);
        c.FillRect(10, 2, 12, 6, Cork);
        c.HLine(10, 21, 2, Col.Rgb(206, 158, 108));
        c.HLine(10, 21, 7, CorkDark);
        c.HLine(5, 26, 24, Moss);
        c.HLine(5, 26, 25, Col.Rgb(86, 136, 64));
        c.FillRect(5, 26, 22, 4, Soil);
        // sprout
        c.VLine(16, 15, 23, LeafDark);
        c.VLine(15, 18, 23, LeafDark);
        c.FillEllipse(11.5, 17.5, 3.2, 1.8, Leaf);
        c.HLine(10, 13, 18, LeafDark);
        c.FillEllipse(20.5, 13.5, 3.4, 2.0, Leaf);
        c.HLine(18, 22, 14, LeafDark);
        c.VLine(7, 12, 22, Shine);
        c.VLine(8, 13, 18, Col.Rgb(255, 255, 255, 110));
    }
}
