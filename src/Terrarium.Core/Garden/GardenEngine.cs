using Terrarium.Core.Activity;
using Terrarium.Core.Model;

namespace Terrarium.Core.Garden;

public enum GardenEventKind { StageUp, Unlocked, SlotOpened, Wilting, Recovered }

public sealed record GardenEvent(GardenEventKind Kind, string Title, string Message);

/// <summary>
/// The rules of the garden. Pure and deterministic: feed it time slices tagged with an
/// <see cref="ActivityKind"/> and it updates <see cref="GardenState"/> and reports events.
/// </summary>
public sealed class GardenEngine
{
    /// <summary>Longest slice accepted per tick. Bigger gaps (sleep, hibernate, a hung UI) count as idle.</summary>
    public const double MaxTickSeconds = 10;

    /// <summary>Minutes of distraction (after the grace period) to go from full health to the floor.</summary>
    public double WiltMinutes { get; set; } = 90;
    /// <summary>Minutes of focus to recover from the floor to full health.</summary>
    public double RecoverMinutes { get; set; } = 30;
    public const double HealthFloor = 0.1;
    public const double WiltNoticeThreshold = 0.6;
    public const double RecoveredThreshold = 0.9;

    /// <summary>Multiplier on plant growth (not on unlock progress). Cozy mode uses 0.5 so plants still take days.</summary>
    public double GrowthRate { get; set; } = 1.0;

    public GardenState State { get; }
    public double GraceSeconds { get; set; }

    private readonly Random _rng;

    public GardenEngine(GardenState state, double graceSeconds, int? randomSeed = null)
    {
        State = state;
        State.Normalize();
        GraceSeconds = Math.Max(0, graceSeconds);
        _rng = randomSeed is int s ? new Random(s) : new Random();

        // Items available from the start (or already earned) shouldn't be announced as new.
        foreach (var item in Catalog.All)
            if (Catalog.IsUnlocked(item, State.LifetimeFocusHours) && !State.AnnouncedUnlocks.Contains(item.Id))
                State.AnnouncedUnlocks.Add(item.Id);
        EnsureSlotsPlanted(null);
    }

    public bool IsWilting => State.DistractionStreakSeconds > GraceSeconds;

    public IReadOnlyList<GardenEvent> Tick(DateTime localNow, double seconds, ActivityKind kind)
    {
        var events = new List<GardenEvent>();
        if (double.IsNaN(seconds) || seconds <= 0) return events;
        if (seconds > MaxTickSeconds) kind = ActivityKind.Idle;
        seconds = Math.Min(seconds, MaxTickSeconds);

        switch (kind)
        {
            case ActivityKind.Focus:
                ApplyFocus(localNow, seconds, events);
                break;
            case ActivityKind.Distraction:
                ApplyDistraction(localNow, seconds, events);
                break;
            case ActivityKind.Idle:
            case ActivityKind.Paused:
                State.DistractionStreakSeconds = 0;
                break;
            case ActivityKind.Neutral:
                // Neutral apps neither help nor hurt, and don't reset a distraction streak,
                // so hopping between YouTube and Explorer doesn't dodge the wilt.
                break;
        }
        return events;
    }

    private void ApplyFocus(DateTime localNow, double seconds, List<GardenEvent> events)
    {
        double hoursBefore = State.LifetimeFocusHours;
        State.LifetimeFocusSeconds += seconds;
        State.Day(localNow).FocusSeconds += seconds;
        State.DistractionStreakSeconds = 0;

        double heal = seconds / (RecoverMinutes * 60.0);
        foreach (var p in State.Slots)
        {
            if (p is null) continue;
            int stageBefore = Catalog.StageFor(p.GrowthHours);
            p.GrowthSeconds += seconds * GrowthRate * GrowthMultiplier(p.Health);
            p.Health = Math.Min(1.0, p.Health + heal);
            int stageAfter = Catalog.StageFor(p.GrowthHours);
            if (stageAfter > stageBefore)
            {
                var sp = Catalog.SpeciesOf(p.SpeciesId);
                events.Add(new(GardenEventKind.StageUp, $"{sp.Name}: {Catalog.StageNames[stageAfter]}",
                    StageMessage(sp.Name, stageAfter)));
            }
        }

        if (State.WiltNoticeShown && State.Slots.All(p => p is null || p.Health >= RecoveredThreshold))
        {
            State.WiltNoticeShown = false;
            events.Add(new(GardenEventKind.Recovered, "Perking back up", "Your plants have recovered. Nice work."));
        }

        CheckUnlocks(hoursBefore, events);
    }

    private void ApplyDistraction(DateTime localNow, double seconds, List<GardenEvent> events)
    {
        State.LifetimeDistractionSeconds += seconds;
        State.Day(localNow).DistractionSeconds += seconds;

        double before = State.DistractionStreakSeconds;
        State.DistractionStreakSeconds += seconds;
        double wiltingSeconds = State.DistractionStreakSeconds - Math.Max(before, GraceSeconds);
        if (wiltingSeconds <= 0) return;

        double loss = wiltingSeconds / (WiltMinutes * 60.0);
        foreach (var p in State.Slots)
            if (p is not null) p.Health = Math.Max(HealthFloor, p.Health - loss);

        if (!State.WiltNoticeShown && State.Slots.Any(p => p is not null && p.Health < WiltNoticeThreshold))
        {
            State.WiltNoticeShown = true;
            events.Add(new(GardenEventKind.Wilting, "Your plants are drooping",
                "They'll perk up once you're back at work."));
        }
    }

    public static double GrowthMultiplier(double health) => 0.4 + 0.6 * Math.Clamp(health, 0, 1);

    private static string StageMessage(string name, int stage) => stage switch
    {
        1 => $"Your {name.ToLowerInvariant()} just sprouted.",
        2 => $"Your {name.ToLowerInvariant()} is putting out new leaves.",
        3 => $"Your {name.ToLowerInvariant()} is filling out nicely.",
        4 => $"Your {name.ToLowerInvariant()} is fully grown.",
        5 => $"Your {name.ToLowerInvariant()} is blooming!",
        _ => $"Your {name.ToLowerInvariant()} grew.",
    };

    private void CheckUnlocks(double hoursBefore, List<GardenEvent> events)
    {
        double now = State.LifetimeFocusHours;
        foreach (var item in Catalog.All)
        {
            if (State.AnnouncedUnlocks.Contains(item.Id)) continue;
            if (!Catalog.IsUnlocked(item, now)) continue;
            State.AnnouncedUnlocks.Add(item.Id);
            if (item.Kind == UnlockKind.Slot)
            {
                int slot = int.Parse(item.Id.AsSpan(4));
                var planted = PlantSlot(slot, null);
                events.Add(new(GardenEventKind.SlotOpened, "Room for another plant",
                    $"A new pot appeared with a {Catalog.SpeciesOf(planted.SpeciesId).Name.ToLowerInvariant()} seed."));
            }
            else
            {
                if (item.Kind == UnlockKind.Decoration && !State.Decorations.Contains(item.Id))
                    State.Decorations.Add(item.Id); // new decorations show up automatically; can be toggled off
                string what = item.Kind switch
                {
                    UnlockKind.Species => "New plant",
                    UnlockKind.Pot => "New pot style",
                    _ => "New decoration",
                };
                events.Add(new(GardenEventKind.Unlocked, $"{what}: {item.Name}", item.Blurb));
            }
        }
    }

    private void EnsureSlotsPlanted(List<GardenEvent>? events)
    {
        for (int i = 0; i < Catalog.SlotCount; i++)
            if (State.Slots[i] is null && Catalog.IsUnlocked(Catalog.All.First(x => x.Id == $"slot{i}"), State.LifetimeFocusHours))
                PlantSlot(i, null);
    }

    /// <summary>Plants a fresh seed in a slot. With no species given, picks an unlocked one not already growing.</summary>
    public PlantState PlantSlot(int slot, string? speciesId)
    {
        if (slot < 0 || slot >= Catalog.SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
        if (speciesId is null)
        {
            var unlocked = Catalog.Species.Where(s => Catalog.IsUnlocked(s, State.LifetimeFocusHours)).ToList();
            var fresh = unlocked.Where(s => State.Slots.All(p => p?.SpeciesId != s.Id)).ToList();
            var pool = fresh.Count > 0 ? fresh : unlocked;
            speciesId = pool[_rng.Next(pool.Count)].Id;
        }
        var plant = new PlantState { SpeciesId = speciesId, Seed = _rng.Next(1, int.MaxValue), PlantedUtc = DateTime.UtcNow };
        State.Slots[slot] = plant;
        return plant;
    }

    public bool IsSlotOpen(int slot) => Catalog.IsUnlocked(Catalog.All.First(x => x.Id == $"slot{slot}"), State.LifetimeFocusHours);
}
