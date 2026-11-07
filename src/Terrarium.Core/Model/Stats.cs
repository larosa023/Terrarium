using System.Globalization;

namespace Terrarium.Core.Model;

public sealed record DaySummary(DateTime Date, double FocusSeconds, double DistractionSeconds);

public static class Stats
{
    public static DaySummary ForDay(GardenState state, DateTime localDate)
    {
        state.Days.TryGetValue(GardenState.DayKey(localDate), out var d);
        return new DaySummary(localDate.Date, d?.FocusSeconds ?? 0, d?.DistractionSeconds ?? 0);
    }

    /// <summary>The last <paramref name="days"/> days ending today, oldest first.</summary>
    public static IReadOnlyList<DaySummary> LastDays(GardenState state, DateTime localToday, int days = 7)
    {
        var list = new List<DaySummary>(days);
        for (int i = days - 1; i >= 0; i--)
            list.Add(ForDay(state, localToday.Date.AddDays(-i)));
        return list;
    }

    public static string FormatDuration(double seconds)
    {
        if (seconds < 60) return seconds < 1 ? "0m" : "<1m";
        var ts = TimeSpan.FromSeconds(seconds);
        int h = (int)ts.TotalHours;
        return h > 0 ? $"{h}h {ts.Minutes}m" : $"{ts.Minutes}m";
    }

    public static string DailyRecap(GardenState state, DateTime localNow)
    {
        var d = ForDay(state, localNow);
        return $"Today: {FormatDuration(d.FocusSeconds)} focused, {FormatDuration(d.DistractionSeconds)} on distractions.";
    }

    public static string WeeklyRecap(GardenState state, DateTime localNow)
    {
        var week = LastDays(state, localNow, 7);
        double focus = week.Sum(x => x.FocusSeconds);
        double distract = week.Sum(x => x.DistractionSeconds);
        var best = week.MaxBy(x => x.FocusSeconds)!;
        string bestDay = best.FocusSeconds > 0 ? $" Best day: {best.Date.ToString("dddd", CultureInfo.InvariantCulture)}." : "";
        return $"This week: {FormatDuration(focus)} focused, {FormatDuration(distract)} distracted.{bestDay}";
    }
}
