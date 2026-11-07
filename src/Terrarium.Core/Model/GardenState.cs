namespace Terrarium.Core.Model;

public sealed class PlantState
{
    public string SpeciesId { get; set; } = "fern";
    /// <summary>Effective focused seconds this plant has absorbed (scaled by its health).</summary>
    public double GrowthSeconds { get; set; }
    /// <summary>0 (badly wilted) .. 1 (thriving). Plants never die; they just droop.</summary>
    public double Health { get; set; } = 1.0;
    public DateTime PlantedUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Per-plant random seed so two ferns don't look identical.</summary>
    public int Seed { get; set; }

    public double GrowthHours => GrowthSeconds / 3600.0;
}

public sealed class DayStat
{
    public double FocusSeconds { get; set; }
    public double DistractionSeconds { get; set; }
}

public sealed class GardenState
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Fixed-length list (one per slot). Null = empty or not yet unlocked.</summary>
    public List<PlantState?> Slots { get; set; } = [];

    public string PotId { get; set; } = "terracotta";
    public List<string> Decorations { get; set; } = [];

    public double LifetimeFocusSeconds { get; set; }
    public double LifetimeDistractionSeconds { get; set; }

    /// <summary>Continuous distraction time in the current stretch.</summary>
    public double DistractionStreakSeconds { get; set; }

    /// <summary>Whether a "plants are wilting" note has been shown for the current slump.</summary>
    public bool WiltNoticeShown { get; set; }

    /// <summary>Ids already announced, so unlock notifications fire once.</summary>
    public List<string> AnnouncedUnlocks { get; set; } = [];

    /// <summary>Keyed by local date "yyyy-MM-dd".</summary>
    public Dictionary<string, DayStat> Days { get; set; } = [];

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public double LifetimeFocusHours => LifetimeFocusSeconds / 3600.0;

    public static string DayKey(DateTime local) => local.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public DayStat Day(DateTime local)
    {
        var key = DayKey(local);
        if (!Days.TryGetValue(key, out var d))
        {
            d = new DayStat();
            Days[key] = d;
        }
        return d;
    }

    public static GardenState CreateNew(int seed)
    {
        var s = new GardenState();
        s.Normalize();
        s.Slots[0] = new PlantState { SpeciesId = "fern", Seed = seed };
        return s;
    }

    public void Normalize()
    {
        Slots ??= [];
        while (Slots.Count < Garden.Catalog.SlotCount) Slots.Add(null);
        if (Slots.Count > Garden.Catalog.SlotCount) Slots.RemoveRange(Garden.Catalog.SlotCount, Slots.Count - Garden.Catalog.SlotCount);
        foreach (var p in Slots)
        {
            if (p is null) continue;
            p.Health = Math.Clamp(p.Health, 0, 1);
            p.GrowthSeconds = Math.Max(0, p.GrowthSeconds);
            if (Garden.Catalog.Species.All(sp => sp.Id != p.SpeciesId)) p.SpeciesId = "fern";
        }
        Decorations ??= [];
        AnnouncedUnlocks ??= [];
        Days ??= [];
        if (Garden.Catalog.Pots.All(p => p.Id != PotId)) PotId = "terracotta";

        // Keep about four months of daily history.
        if (Days.Count > 130)
        {
            foreach (var k in Days.Keys.OrderBy(k => k, StringComparer.Ordinal).Take(Days.Count - 120).ToList())
                Days.Remove(k);
        }
    }
}
