using Terrarium.Core.Model;

namespace Terrarium.Core.Activity;

/// <summary>
/// Turns (process name, window title) into an <see cref="ActivityKind"/> using the user's rules.
/// Window-title rules win over process rules, so "Chrome" can stay neutral while
/// "YouTube - Google Chrome" is a distraction and a browser-based work tool is focus.
/// Among title rules the longest matching pattern wins (most specific).
/// </summary>
public sealed class ActivityClassifier
{
    private readonly Dictionary<string, ActivityKind> _process = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Pattern, ActivityKind Kind)> _titles = [];

    public static readonly string SelfProcessName = "Terrarium";

    public ActivityClassifier(IEnumerable<AppRule> rules)
    {
        foreach (var r in rules)
        {
            if (string.IsNullOrWhiteSpace(r.Pattern) || r.Kind is not (ActivityKind.Focus or ActivityKind.Distraction))
                continue;
            if (r.Target == MatchTarget.Process)
                _process[NormalizeProcess(r.Pattern)] = r.Kind;
            else
                _titles.Add((r.Pattern.Trim(), r.Kind));
        }
        _titles.Sort((a, b) => b.Pattern.Length.CompareTo(a.Pattern.Length));
    }

    public static string NormalizeProcess(string name)
    {
        name = name.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name;
    }

    public ActivityKind Classify(string? processName, string? windowTitle)
    {
        var proc = NormalizeProcess(processName ?? "");
        if (proc.Equals(SelfProcessName, StringComparison.OrdinalIgnoreCase))
            return ActivityKind.Neutral;

        if (!string.IsNullOrEmpty(windowTitle))
        {
            foreach (var (pattern, kind) in _titles)
                if (windowTitle.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return kind;
        }

        if (proc.Length > 0 && _process.TryGetValue(proc, out var k))
            return k;

        return ActivityKind.Neutral;
    }
}
