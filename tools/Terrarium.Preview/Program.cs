using Terrarium.Core.Garden;
using Terrarium.Core.Rendering;
using Terrarium.Preview;

// Usage: dotnet run --project tools/Terrarium.Preview -- <outDir> [--icon <path.ico>]
var outDir = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "preview";
Directory.CreateDirectory(outDir);
var renderer = new TerrariumRenderer();
string[] allDeco = ["pebbles", "moss", "mushroom", "lantern", "snail", "fireflies"];

// 1) Growth sheet: every species through every stage (columns), healthy then wilted (rows).
{
    var species = Catalog.Species.Select(s => s.Id).ToArray();
    double[] growth = [0.1, 0.7, 1.5, 2.6, 3.6, 4.5, 5.0];
    var sheet = new Sheet(growth.Length, species.Length * 2);
    for (int si = 0; si < species.Length; si++)
        for (int gi = 0; gi < growth.Length; gi++)
            for (int w = 0; w < 2; w++)
            {
                var c = new PixelCanvas(TerrariumRenderer.Width, TerrariumRenderer.Height);
                renderer.Render(c, new SceneInput
                {
                    Plants = [null, new PlantView(species[si], growth[gi], w == 0 ? 1.0 : 0.15, 1234 + si), null],
                    HourOfDay = 12,
                    Time = 1.0,
                });
                sheet.Put(gi, si * 2 + w, c);
            }
    File.WriteAllBytes(Path.Combine(outDir, "growth-sheet.png"), ImageWriters.Png(sheet.Canvas, 3));
}

// 2) A mature, fully decorated garden across the day.
{
    double[] hours = [6.5, 10, 13, 18.5, 20.5, 23];
    string[] pots = ["terracotta", "sage", "indigo", "gold", "terracotta", "indigo"];
    var sheet = new Sheet(hours.Length, 1);
    for (int i = 0; i < hours.Length; i++)
    {
        var c = new PixelCanvas(TerrariumRenderer.Width, TerrariumRenderer.Height);
        renderer.Render(c, new SceneInput
        {
            Plants = [new PlantView("fern", 4.2, 1, 11), new PlantView("bonsai", 5, 1, 22), new PlantView("daisy", 5, 1, 33)],
            PotId = pots[i],
            Decorations = allDeco,
            HourOfDay = hours[i],
            Time = 3.0 + i * 7,
        });
        sheet.Put(i, 0, c);
    }
    File.WriteAllBytes(Path.Combine(outDir, "day-cycle.png"), ImageWriters.Png(sheet.Canvas, 4));
}

// 3) A few hero shots at widget size.
{
    var scenes = new (string Name, SceneInput Scene)[]
    {
        ("first-day", new SceneInput { Plants = [new PlantView("fern", 1.4, 1, 5), null, null], HourOfDay = 11, Time = 2 }),
        ("week-two", new SceneInput
        {
            Plants = [new PlantView("fern", 3.4, 1, 5), new PlantView("succulent", 2.4, 1, 8), new PlantView("cactus", 1.2, 1, 9)],
            PotId = "sage", Decorations = ["pebbles", "moss"], HourOfDay = 15, Time = 4,
        }),
        ("wilting", new SceneInput
        {
            Plants = [new PlantView("fern", 3.6, 0.25, 5), new PlantView("succulent", 3.2, 0.25, 8), new PlantView("cactus", 3.9, 0.25, 9)],
            PotId = "sage", Decorations = ["pebbles", "moss"], HourOfDay = 14, Time = 4,
        }),
        ("night-garden", new SceneInput
        {
            Plants = [new PlantView("daisy", 5, 1, 5), new PlantView("bonsai", 5, 1, 8), new PlantView("cactus", 5, 1, 9)],
            PotId = "indigo", Decorations = allDeco, HourOfDay = 22, Time = 9,
        }),
    };
    foreach (var (name, scene) in scenes)
    {
        var c = new PixelCanvas(TerrariumRenderer.Width, TerrariumRenderer.Height);
        renderer.Render(c, scene);
        File.WriteAllBytes(Path.Combine(outDir, $"{name}.png"), ImageWriters.Png(c, 4));
    }
}

// 4) Sway animation strip (8 frames, 8 fps) to sanity-check motion.
{
    var sheet = new Sheet(8, 1);
    for (int f = 0; f < 8; f++)
    {
        var c = new PixelCanvas(TerrariumRenderer.Width, TerrariumRenderer.Height);
        renderer.Render(c, new SceneInput
        {
            Plants = [new PlantView("fern", 4, 1, 5), new PlantView("daisy", 5, 1, 8), new PlantView("fern", 2.5, 1, 9)],
            HourOfDay = 12,
            Time = f * 0.4,
        });
        sheet.Put(f, 0, c);
    }
    File.WriteAllBytes(Path.Combine(outDir, "sway-frames.png"), ImageWriters.Png(sheet.Canvas, 3));
}

// 5) App icon
{
    var p16 = IconArt.Render(16);
    var p32 = IconArt.Render(32);
    File.WriteAllBytes(Path.Combine(outDir, "icon-32.png"), ImageWriters.Png(p32, 8));
    int iconArg = Array.IndexOf(args, "--icon");
    if (iconArg >= 0 && iconArg + 1 < args.Length)
    {
        var ico = ImageWriters.Ico([
            (16, ImageWriters.Png(p16, 1)),
            (32, ImageWriters.Png(p32, 1)),
            (48, ImageWriters.Png(p16, 3)),
            (64, ImageWriters.Png(p32, 2)),
            (256, ImageWriters.Png(p32, 8)),
        ]);
        File.WriteAllBytes(args[iconArg + 1], ico);
        Console.WriteLine($"Wrote icon {args[iconArg + 1]}");
    }
}

Console.WriteLine($"Previews written to {Path.GetFullPath(outDir)}");

internal sealed class Sheet
{
    private const int Gap = 2;
    public PixelCanvas Canvas { get; }
    public Sheet(int cols, int rows)
    {
        Canvas = new PixelCanvas(cols * (TerrariumRenderer.Width + Gap) + Gap, rows * (TerrariumRenderer.Height + Gap) + Gap);
        for (int i = 0; i < Canvas.Pixels.Length; i++) Canvas.Pixels[i] = 0xFF2B2A33; // dark desktop-ish backdrop
    }

    public void Put(int col, int row, PixelCanvas tile)
    {
        int ox = Gap + col * (tile.Width + Gap), oy = Gap + row * (tile.Height + Gap);
        for (int y = 0; y < tile.Height; y++)
            for (int x = 0; x < tile.Width; x++)
            {
                uint p = tile.GetStraight(x, y);
                if (p >> 24 != 0) Canvas.Blend(ox + x, oy + y, p);
            }
    }
}
