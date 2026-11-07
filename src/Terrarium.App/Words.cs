namespace Terrarium.App;

/// <summary>User-facing wording, which depends on the garden mode.</summary>
internal static class Words
{
    /// <summary>Set by the controller whenever settings load or change.</summary>
    public static bool Cozy { get; set; } = true;

    /// <summary>"2h growing today" vs "2h focused today".</summary>
    public static string Today(string duration) => Cozy ? $"{duration} of growing today" : $"{duration} focused today";

    /// <summary>Summary headline: "2h of growing" vs "2h focused".</summary>
    public static string Headline(string duration) => Cozy ? $"{duration} of growing" : $"{duration} focused";

    /// <summary>Unit for unlock thresholds: "3h of growing time" vs "3h of focus".</summary>
    public static string TimeUnit => Cozy ? "growing time" : "focus";
}
