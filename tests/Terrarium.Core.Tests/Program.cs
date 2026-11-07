using Terrarium.Core.Activity;
using Terrarium.Core.Garden;
using Terrarium.Core.Model;
using Terrarium.Core.Rendering;
using Terrarium.Core.Storage;
using Terrarium.Core.Updates;

int passed = 0, failed = 0;
void Test(string name, Action body)
{
    try { body(); passed++; Console.WriteLine($"  ok   {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"  FAIL {name}: {ex.Message}"); }
}
void Check(bool cond, string msg) { if (!cond) throw new Exception(msg); }
void Near(double a, double b, double tol, string msg) { if (Math.Abs(a - b) > tol) throw new Exception($"{msg} (got {a}, expected {b})"); }

var noon = new DateTime(2026, 11, 7, 12, 0, 0);

// Simulates N seconds of an activity in 1-second ticks; returns all events.
List<GardenEvent> Run(GardenEngine e, double seconds, ActivityKind kind, DateTime? start = null)
{
    var all = new List<GardenEvent>();
    var t = start ?? noon;
    for (int i = 0; i < seconds; i++) { all.AddRange(e.Tick(t, 1, kind)); t = t.AddSeconds(1); }
    return all;
}

Console.WriteLine("Classifier");
Test("process rules match without .exe, case-insensitive", () =>
{
    var c = new ActivityClassifier(Settings.DefaultRules());
    Check(c.Classify("excel.exe", "Budget.xlsx") == ActivityKind.Focus, "excel");
    Check(c.Classify("Code", "main.cs - VS Code") == ActivityKind.Focus, "code");
    Check(c.Classify("notepad", "x") == ActivityKind.Neutral, "notepad neutral");
});
Test("title rules beat process rules (sites inside a browser)", () =>
{
    var c = new ActivityClassifier(Settings.DefaultRules());
    Check(c.Classify("chrome", "Cats - YouTube - Google Chrome") == ActivityKind.Distraction, "youtube");
    Check(c.Classify("chrome", "Gmail - Google Chrome") == ActivityKind.Neutral, "gmail neutral");
    Check(c.Classify("msedge", "Gridline - Project X") == ActivityKind.Focus, "gridline in browser");
});
Test("longest title match wins", () =>
{
    var c = new ActivityClassifier([
        new AppRule(MatchTarget.Title, "YouTube", ActivityKind.Distraction),
        new AppRule(MatchTarget.Title, "YouTube Studio", ActivityKind.Focus)]);
    Check(c.Classify("chrome", "Channel - YouTube Studio") == ActivityKind.Focus, "specific");
});
Test("Terrarium itself is always neutral", () =>
{
    var c = new ActivityClassifier([new AppRule(MatchTarget.Title, "Terrarium", ActivityKind.Distraction)]);
    Check(c.Classify("Terrarium", "Terrarium") == ActivityKind.Neutral, "self");
});

Console.WriteLine("Garden engine");
Test("new garden starts with one fern seed, nothing announced as new", () =>
{
    var e = new GardenEngine(GardenState.CreateNew(1), 180, 1);
    Check(e.State.Slots[0]?.SpeciesId == "fern", "fern");
    Check(e.State.Slots[1] is null && e.State.Slots[2] is null, "other slots closed");
    Check(Run(e, 5, ActivityKind.Focus).Count == 0, "no events in first seconds");
});
Test("focus grows plants and reaches Sprout at 20 minutes", () =>
{
    var e = new GardenEngine(GardenState.CreateNew(1), 180, 1);
    var ev = Run(e, 20 * 60 + 2, ActivityKind.Focus);
    Check(Catalog.StageFor(e.State.Slots[0]!.GrowthHours) == 1, "stage 1");
    Check(ev.Any(x => x.Kind == GardenEventKind.StageUp), "stage-up event");
    Near(e.State.Day(noon).FocusSeconds, 1202, 0.5, "day focus");
});
Test("distraction within grace period does no harm", () =>
{
    var e = new GardenEngine(GardenState.CreateNew(1), 180, 1);
    Run(e, 170, ActivityKind.Distraction);
    Near(e.State.Slots[0]!.Health, 1, 1e-9, "health");
});
Test("long distraction wilts gradually, floors, and notifies once", () =>
{
    var e = new GardenEngine(GardenState.CreateNew(1), 180, 1);
    var ev = Run(e, 180 + 45 * 60, ActivityKind.Distraction);
    Near(e.State.Slots[0]!.Health, 0.5, 0.01, "half health after 45 min past grace");
    Check(ev.Count(x => x.Kind == GardenEventKind.Wilting) == 1, "one wilting notice");
    Run(e, 3 * 3600, ActivityKind.Distraction);
    Near(e.State.Slots[0]!.Health, GardenEngine.HealthFloor, 1e-9, "floor");
});
Test("idle resets the distraction streak and changes nothing", () =>
{
    var e = new GardenEngine(GardenState.CreateNew(1), 180, 1);
    Run(e, 170, ActivityKind.Distraction);
    var before = e.State.LifetimeFocusSeconds;
    Run(e, 600, ActivityKind.Idle);
    Check(e.State.DistractionStreakSeconds == 0, "streak reset");
    Run(e, 170, ActivityKind.Distraction);
    Near(e.State.Slots[0]!.Health, 1, 1e-9, "still healthy");
    Check(e.State.LifetimeFocusSeconds == before, "no focus from idle");
});
Test("neutral apps don't reset a distraction streak", () =>
{
    var e = new GardenEngine(GardenState.CreateNew(1), 180, 1);
    Run(e, 150, ActivityKind.Distraction);
    Run(e, 60, ActivityKind.Neutral);
    Run(e, 60, ActivityKind.Distraction);
    Check(e.State.Slots[0]!.Health < 1, "streak continued across neutral");
});
Test("focus recovers health and announces recovery", () =>
{
    var e = new GardenEngine(GardenState.CreateNew(1), 0, 1);
    Run(e, 60 * 60, ActivityKind.Distraction);
    var ev = Run(e, 30 * 60, ActivityKind.Focus);
    Check(e.State.Slots[0]!.Health >= 0.99, "recovered");
    Check(ev.Any(x => x.Kind == GardenEventKind.Recovered), "recovered event");
});
Test("growth rate slows plants but not unlock progress (cozy mode)", () =>
{
    var e = new GardenEngine(GardenState.CreateNew(1), 180, 1) { GrowthRate = 0.5 };
    Run(e, 3600, ActivityKind.Focus);
    Near(e.State.Slots[0]!.GrowthSeconds, 1800, 1, "half growth");
    Near(e.State.LifetimeFocusSeconds, 3600, 1, "full unlock progress");
});
Test("settings default to cozy mode, including older files without a mode", () =>
{
    Check(new Settings().Mode == GardenMode.Cozy, "default");
    var old = System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"idleMinutes\":3}", JsonStore.Options)!;
    Check(old.Mode == GardenMode.Cozy, "missing field");
});
Test("unlock-everything fills pots, enables decor once, and stays quiet", () =>
{
    var e = new GardenEngine(GardenState.CreateNew(1), 180, 1) { UnlockEverything = true };
    Check(e.UnlockAllNow(), "changed");
    Check(e.State.Slots.All(p => p is not null), "all pots planted");
    Check(e.State.Slots.Select(p => p!.SpeciesId).Distinct().Count() == 3, "three different plants");
    Check(e.State.Decorations.Count == Catalog.Decorations.Length, "decor on");
    e.State.Decorations.Remove("snail");
    Check(!e.UnlockAllNow() && !e.State.Decorations.Contains("snail"), "toggle respected");
    Check(Run(e, 4 * 3600, ActivityKind.Focus).All(x => x.Kind == GardenEventKind.StageUp), "no unlock notices");
    Check(Catalog.NextUnlock(e.UnlockHours) is null, "nothing left to unlock");
});
Test("wilted plants grow slower", () =>
{
    Check(GardenEngine.GrowthMultiplier(0.1) < GardenEngine.GrowthMultiplier(1), "multiplier");
});
Test("big time gaps (sleep) count as idle", () =>
{
    var e = new GardenEngine(GardenState.CreateNew(1), 180, 1);
    e.Tick(noon, 3600, ActivityKind.Focus);
    Check(e.State.LifetimeFocusSeconds == 0, "no credit for gap");
});
Test("unlocks fire once and slots open with a new seed", () =>
{
    var e = new GardenEngine(GardenState.CreateNew(1), 180, 1);
    var ev = new List<GardenEvent>();
    for (int i = 0; i < 3 * 360 + 1; i++) ev.AddRange(e.Tick(noon, 10, ActivityKind.Focus)); // just over 3h
    Check(ev.Count(x => x.Title.Contains("Pebbles")) == 1, "pebbles at 1h");
    Check(ev.Count(x => x.Title.Contains("Succulent")) == 1, "succulent at 2h");
    Check(ev.Count(x => x.Kind == GardenEventKind.SlotOpened) == 1, "slot 2 opened at 3h");
    Check(e.State.Slots[1] is not null, "slot 2 planted");
    Check(e.State.Slots[1]!.SpeciesId == "succulent", "picks a species not already growing");
    Check(e.State.Decorations.Contains("pebbles"), "decoration auto-enabled");
});
Test("existing garden doesn't re-announce old unlocks", () =>
{
    var s = GardenState.CreateNew(1);
    s.LifetimeFocusSeconds = 30 * 3600;
    var e = new GardenEngine(s, 180, 1);
    Check(e.State.Slots.All(p => p is not null), "slots filled on load");
    var ev = Run(e, 5, ActivityKind.Focus);
    Check(!ev.Any(x => x.Kind == GardenEventKind.Unlocked), "no stale unlocks");
});
Test("stages progress over roughly days and weeks", () =>
{
    Check(Catalog.StageFor(0) == 0 && Catalog.StageFor(0.34) == 1 && Catalog.StageFor(16) == 5, "thresholds");
    Near(Catalog.GrowthValue(0.75 * (1.5 - 1.0 / 3) + 1.0 / 3), 1.75, 1e-6, "continuous growth");
});

Console.WriteLine("Stats & storage");
Test("weekly summary covers seven days", () =>
{
    var s = GardenState.CreateNew(1);
    s.Day(noon).FocusSeconds = 3600;
    s.Day(noon.AddDays(-6)).FocusSeconds = 1800;
    s.Day(noon.AddDays(-7)).FocusSeconds = 99999;
    var week = Stats.LastDays(s, noon);
    Check(week.Count == 7, "7 days");
    Near(week.Sum(d => d.FocusSeconds), 5400, 1e-9, "sum excludes day -7");
    Check(Stats.FormatDuration(5400) == "1h 30m", Stats.FormatDuration(5400));
    Check(Stats.WeeklyRecap(s, noon).Contains("1h 30m"), "recap text");
});
Test("JSON round-trip preserves garden and settings", () =>
{
    var dir = Path.Combine(Path.GetTempPath(), "terrarium-test-" + Guid.NewGuid().ToString("N"));
    try
    {
        var e = new GardenEngine(GardenState.CreateNew(1), 180, 1);
        Run(e, 500, ActivityKind.Focus);
        JsonStore.Save(Path.Combine(dir, "garden.json"), e.State);
        var back = JsonStore.Load(Path.Combine(dir, "garden.json"), () => new GardenState());
        Near(back.Slots[0]!.GrowthSeconds, e.State.Slots[0]!.GrowthSeconds, 1e-6, "growth");
        Check(back.Slots.Count == 3 && back.Slots[1] is null, "slots");

        var st = new Settings { IdleMinutes = 7 };
        JsonStore.Save(Path.Combine(dir, "settings.json"), st);
        var st2 = JsonStore.Load(Path.Combine(dir, "settings.json"), () => new Settings());
        Check(st2.IdleMinutes == 7 && st2.Rules.Count == st.Rules.Count, "settings");
        Check(File.ReadAllText(Path.Combine(dir, "settings.json")).Contains("\"Focus\""), "enums as strings");
    }
    finally { try { Directory.Delete(dir, true); } catch { } }
});
Test("corrupt file falls back to defaults and is kept as .bad", () =>
{
    var dir = Path.Combine(Path.GetTempPath(), "terrarium-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        var p = Path.Combine(dir, "garden.json");
        File.WriteAllText(p, "{ not json");
        var g = JsonStore.Load(p, () => GardenState.CreateNew(3));
        Check(g.Slots[0] is not null, "fallback");
        Check(File.Exists(p + ".bad"), "backup kept");
    }
    finally { try { Directory.Delete(dir, true); } catch { } }
});

Console.WriteLine("Updates");
Test("version parsing and comparison", () =>
{
    Check(VersionUtil.TryParse("v1.2.3") == new Version(1, 2, 3, 0), "v-prefix");
    Check(VersionUtil.TryParse("1.2") == new Version(1, 2, 0, 0), "short");
    Check(VersionUtil.TryParse("2.0.0-beta.1") == new Version(2, 0, 0, 0), "prerelease");
    Check(VersionUtil.TryParse("nightly") is null, "junk");
    Check(VersionUtil.IsNewer(new Version(0, 2, 0), new Version(0, 1, 9, 0)), "newer");
    Check(!VersionUtil.IsNewer(new Version(0, 1, 0, 0), new Version(0, 1)), "equal isn't newer");
});
Test("parses a GitHub release payload", () =>
{
    const string json = """
    {"tag_name":"v0.2.0","html_url":"https://github.com/larosa023/Terrarium/releases/tag/v0.2.0","body":"Snails!","prerelease":false,
     "assets":[{"name":"Terrarium.exe","browser_download_url":"https://example.invalid/Terrarium.exe","size":123},
               {"name":"Terrarium.exe.sha256","browser_download_url":"https://example.invalid/Terrarium.exe.sha256","size":80}]}
    """;
    var r = ReleaseInfo.Parse(json)!;
    Check(r.Version == new Version(0, 2, 0, 0), "version");
    Check(r.Exe?.Size == 123 && r.Hash is not null, "assets");
    Check(r.Notes == "Snails!", "notes");
});
Test("parses sha256sum files", () =>
{
    var hex = new string('a', 64);
    Check(ReleaseInfo.ParseSha256($"{hex.ToUpperInvariant()}  Terrarium.exe\n") == hex, "with name");
    Check(ReleaseInfo.ParseSha256("nope") is null, "bad");
});

Console.WriteLine("Rendering");
Test("renders a non-empty, deterministic frame with transparent corners", () =>
{
    var r = new TerrariumRenderer();
    var scene = SceneInput.FromState(GardenState.CreateNew(1), 12, 1.5);
    var a = new PixelCanvas(64, 80);
    var b = new PixelCanvas(64, 80);
    r.Render(a, scene);
    r.Render(b, scene);
    Check(a.Pixels.SequenceEqual(b.Pixels), "deterministic");
    Check(a.Pixels.Count(p => p >> 24 != 0) > 2000, "drawn");
    Check(a.Pixels[0] == 0 && a.Pixels[63] == 0, "corners transparent (click-through)");
});
Test("night is darker than noon", () =>
{
    var r = new TerrariumRenderer();
    double Brightness(double hour)
    {
        var c = new PixelCanvas(64, 80);
        r.Render(c, SceneInput.FromState(GardenState.CreateNew(1), hour, 0));
        return c.Pixels.Sum(p => (double)(((p >> 16) & 255) + ((p >> 8) & 255) + (p & 255)));
    }
    Check(Brightness(23) < Brightness(12) * 0.8, "night darker");
});
Test("render is fast enough for 8 fps with headroom", () =>
{
    var r = new TerrariumRenderer();
    var s = GardenState.CreateNew(1);
    s.LifetimeFocusSeconds = 60 * 3600;
    var e = new GardenEngine(s, 0, 1);
    foreach (var p in s.Slots) p!.GrowthSeconds = 20 * 3600;
    var c = new PixelCanvas(64, 80);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    for (int i = 0; i < 400; i++) r.Render(c, SceneInput.FromState(s, 22, i * 0.125));
    double ms = sw.Elapsed.TotalMilliseconds / 400;
    Console.WriteLine($"         ({ms:F3} ms per fully-decorated frame)");
    Check(ms < 5, $"too slow: {ms} ms");
});

Console.WriteLine();
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
