namespace Terrarium.Core.Garden;

public enum UnlockKind { Species, Pot, Decoration, Slot }

public sealed record UnlockItem(string Id, string Name, UnlockKind Kind, double Hours, string Blurb);

/// <summary>Everything that can be grown, placed, or unlocked, and when (in lifetime focused hours).</summary>
public static class Catalog
{
    public const int SlotCount = 3;

    /// <summary>Lifetime focus hours at which each slot opens.</summary>
    public static readonly double[] SlotHours = [0, 3, 10];

    /// <summary>Focused hours a plant needs to reach each stage.</summary>
    public static readonly double[] StageHours = [0, 1.0 / 3.0, 1.5, 4, 9, 16];
    public static readonly string[] StageNames = ["Seed", "Sprout", "Young", "Leafy", "Mature", "Blooming"];
    public static int MaxStage => StageHours.Length - 1;

    public static readonly UnlockItem[] Species =
    [
        new("fern", "Fern", UnlockKind.Species, 0, "Soft fronds that unfurl with every focused hour."),
        new("succulent", "Succulent", UnlockKind.Species, 2, "A patient little rosette."),
        new("cactus", "Cactus", UnlockKind.Species, 6, "Grows arms, then a flower, if you stay on task."),
        new("daisy", "Daisy", UnlockKind.Species, 12, "Leafy stems that finish in white blooms."),
        new("bonsai", "Bonsai", UnlockKind.Species, 25, "A tiny tree for the long haul."),
    ];

    public static readonly UnlockItem[] Pots =
    [
        new("terracotta", "Terracotta", UnlockKind.Pot, 0, "Classic clay."),
        new("sage", "Sage Ceramic", UnlockKind.Pot, 4, "Matte green glaze."),
        new("indigo", "Indigo Glaze", UnlockKind.Pot, 15, "Deep blue with a cream band."),
        new("gold", "Gilded", UnlockKind.Pot, 40, "For the truly dedicated."),
    ];

    public static readonly UnlockItem[] Decorations =
    [
        new("pebbles", "River Pebbles", UnlockKind.Decoration, 1, "Smooth stones on the moss."),
        new("moss", "Moss Mound", UnlockKind.Decoration, 5, "A soft green hill in the corner."),
        new("mushroom", "Red Mushroom", UnlockKind.Decoration, 8, "A tiny toadstool."),
        new("lantern", "Lantern", UnlockKind.Decoration, 20, "Glows after dark."),
        new("snail", "Snail", UnlockKind.Decoration, 30, "Takes its time, like you."),
        new("fireflies", "Fireflies", UnlockKind.Decoration, 50, "Drift about at night."),
    ];

    public static IEnumerable<UnlockItem> All =>
        Species.Concat(Pots).Concat(Decorations)
            .Concat(SlotHours.Select((h, i) => new UnlockItem($"slot{i}", $"Pot #{i + 1}", UnlockKind.Slot, h, "Room for another plant.")));

    public static UnlockItem? Find(string id) => All.FirstOrDefault(i => i.Id == id);
    public static UnlockItem SpeciesOf(string id) => Species.FirstOrDefault(s => s.Id == id) ?? Species[0];

    public static bool IsUnlocked(UnlockItem item, double lifetimeHours) => lifetimeHours + 1e-9 >= item.Hours;

    public static int StageFor(double growthHours)
    {
        int s = 0;
        for (int i = 0; i < StageHours.Length; i++)
            if (growthHours + 1e-9 >= StageHours[i]) s = i;
        return s;
    }

    /// <summary>Continuous growth value 0..MaxStage, so plants visibly grow between stages.</summary>
    public static double GrowthValue(double growthHours)
    {
        int s = StageFor(growthHours);
        if (s >= MaxStage) return MaxStage;
        double span = StageHours[s + 1] - StageHours[s];
        return s + Math.Clamp((growthHours - StageHours[s]) / span, 0, 1);
    }

    /// <summary>The next thing to unlock, or null when everything is unlocked.</summary>
    public static UnlockItem? NextUnlock(double lifetimeHours) =>
        All.Where(i => !IsUnlocked(i, lifetimeHours)).OrderBy(i => i.Hours).FirstOrDefault();
}
