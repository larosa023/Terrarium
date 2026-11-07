namespace Terrarium.Core.Activity;

/// <summary>How the current moment counts toward the garden.</summary>
public enum ActivityKind
{
    /// <summary>An app that isn't on either list. Counts for nothing.</summary>
    Neutral,
    /// <summary>Focused work. Plants grow and recover.</summary>
    Focus,
    /// <summary>A distraction. After a grace period, plants slowly wilt.</summary>
    Distraction,
    /// <summary>Away from the keyboard, locked, or asleep. Counts for nothing and resets the distraction streak.</summary>
    Idle,
    /// <summary>Tracking paused by the user.</summary>
    Paused,
}
