using Terrarium.Core.Activity;

namespace Terrarium.Core.Model;

public enum MatchTarget
{
    /// <summary>Matches the executable name without ".exe" (e.g. "EXCEL", "Code"). Case-insensitive, exact.</summary>
    Process,
    /// <summary>Matches when the window title contains the pattern (e.g. "YouTube"). Case-insensitive.</summary>
    Title,
}

public enum GardenMode
{
    /// <summary>Grows whenever you're at your computer, whatever you're doing. Never wilts.</summary>
    Cozy,
    /// <summary>Grows with focus apps, droops during long distractions.</summary>
    Focus,
}

public sealed class AppRule
{
    public MatchTarget Target { get; set; }
    public string Pattern { get; set; } = "";
    public ActivityKind Kind { get; set; }

    public AppRule() { }
    public AppRule(MatchTarget target, string pattern, ActivityKind kind)
    {
        Target = target;
        Pattern = pattern;
        Kind = kind;
    }

    public AppRule Clone() => new(Target, Pattern, Kind);
}

public sealed class Settings
{
    public int SchemaVersion { get; set; } = 1;

    public GardenMode Mode { get; set; } = GardenMode.Cozy;

    public List<AppRule> Rules { get; set; } = DefaultRules();

    /// <summary>No keyboard/mouse input for this long counts as idle.</summary>
    public int IdleMinutes { get; set; } = 3;

    /// <summary>Continuous distraction allowed before plants start to wilt.</summary>
    public int GraceMinutes { get; set; } = 3;

    public bool StartWithWindows { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public bool HideWhenFullscreen { get; set; } = true;
    public bool CheckForUpdates { get; set; } = true;
    public bool DailyRecap { get; set; }
    public bool WeeklyRecap { get; set; }
    /// <summary>Local hour (0–23) at which recaps appear.</summary>
    public int RecapHour { get; set; } = 17;

    /// <summary>Integer pixel scale of the widget (physical pixels per art pixel).</summary>
    public int Scale { get; set; } = 3;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool WidgetVisible { get; set; } = true;

    public string? SkippedVersion { get; set; }
    public DateTime? LastUpdateCheckUtc { get; set; }
    public string? LastDailyRecapDate { get; set; }
    public string? LastWeeklyRecapDate { get; set; }

    public static List<AppRule> DefaultRules() =>
    [
        // Focus apps
        new(MatchTarget.Process, "Code", ActivityKind.Focus),          // VS Code
        new(MatchTarget.Process, "devenv", ActivityKind.Focus),        // Visual Studio
        new(MatchTarget.Process, "EXCEL", ActivityKind.Focus),
        new(MatchTarget.Process, "WINWORD", ActivityKind.Focus),
        new(MatchTarget.Process, "POWERPNT", ActivityKind.Focus),
        new(MatchTarget.Process, "Revu", ActivityKind.Focus),          // Bluebeam Revu
        new(MatchTarget.Process, "PlanSwift", ActivityKind.Focus),
        new(MatchTarget.Process, "acad", ActivityKind.Focus),          // AutoCAD
        new(MatchTarget.Process, "Gridline", ActivityKind.Focus),
        new(MatchTarget.Title, "Gridline", ActivityKind.Focus),        // if Gridline runs in a browser

        // Distractions (titles catch websites inside any browser)
        new(MatchTarget.Title, "YouTube", ActivityKind.Distraction),
        new(MatchTarget.Title, "Reddit", ActivityKind.Distraction),
        new(MatchTarget.Title, "Netflix", ActivityKind.Distraction),
        new(MatchTarget.Title, "Twitch", ActivityKind.Distraction),
        new(MatchTarget.Title, "TikTok", ActivityKind.Distraction),
        new(MatchTarget.Title, "Instagram", ActivityKind.Distraction),
        new(MatchTarget.Title, "Facebook", ActivityKind.Distraction),
        new(MatchTarget.Process, "steam", ActivityKind.Distraction),
        new(MatchTarget.Process, "EpicGamesLauncher", ActivityKind.Distraction),
    ];

    public void Normalize()
    {
        IdleMinutes = Math.Clamp(IdleMinutes, 1, 60);
        GraceMinutes = Math.Clamp(GraceMinutes, 0, 60);
        RecapHour = Math.Clamp(RecapHour, 0, 23);
        Scale = Math.Clamp(Scale, 1, 8);
        Rules ??= DefaultRules();
        Rules.RemoveAll(r => r is null || string.IsNullOrWhiteSpace(r.Pattern));
        foreach (var r in Rules) r.Pattern = r.Pattern.Trim();
    }
}
