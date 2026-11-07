using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Terrarium.Core.Garden;
using Terrarium.Core.Model;

namespace Terrarium.App.Views;

/// <summary>Today's and this week's focus, plus how each plant is doing and what unlocks next.</summary>
public partial class SummaryWindow : Window
{
    private readonly Func<GardenState> _state;

    internal SummaryWindow(Func<GardenState> state)
    {
        InitializeComponent();
        _state = state;
        Refresh();
    }

    public void Refresh()
    {
        var s = _state();
        var now = DateTime.Now;
        var today = Stats.ForDay(s, now);

        TodayFocus.Text = Words.Headline(Stats.FormatDuration(today.FocusSeconds));
        TodayDetail.Text = Words.Cozy
            ? "Your garden grows whenever you're at your computer."
            : today.DistractionSeconds >= 60
                ? $"{Stats.FormatDuration(today.DistractionSeconds)} on distractions"
                : "No distractions logged. Nice.";

        var week = Stats.LastDays(s, now, 7);
        WeekTotal.Text = $"{Stats.FormatDuration(week.Sum(d => d.FocusSeconds))} total";
        BuildBars(week);

        PlantList.Children.Clear();
        for (int i = 0; i < s.Slots.Count; i++)
        {
            var p = s.Slots[i];
            string line;
            if (p is null)
            {
                line = $"Pot {i + 1}: opens at {Catalog.SlotHours[i]:0.#}h of {Words.TimeUnit}";
            }
            else
            {
                var sp = Catalog.SpeciesOf(p.SpeciesId);
                int stage = Catalog.StageFor(p.GrowthHours);
                string next = stage < Catalog.MaxStage
                    ? $", {Stats.FormatDuration((Catalog.StageHours[stage + 1] - p.GrowthHours) * 3600)} to {Catalog.StageNames[stage + 1].ToLowerInvariant()}"
                    : "";
                string mood = p.Health >= 0.9 ? "thriving" : p.Health >= 0.6 ? "doing okay" : "drooping";
                line = $"{sp.Name}: {Catalog.StageNames[stage]}, {mood}{next}";
            }
            PlantList.Children.Add(new TextBlock { Text = "•  " + line, Margin = new Thickness(0, 1, 0, 1), TextWrapping = TextWrapping.Wrap });
        }

        double hours = s.LifetimeFocusHours;
        Lifetime.Text = $"{Stats.FormatDuration(s.LifetimeFocusSeconds)} {(Words.Cozy ? "of growing" : "focused")} since {s.CreatedUtc.ToLocalTime():MMMM d, yyyy}.";

        // Cozy mode has nothing to unlock, so the progress row is hidden.
        NextUnlockText.Visibility = NextUnlockBar.Visibility = Words.Cozy ? Visibility.Collapsed : Visibility.Visible;
        var nextUnlock = Catalog.NextUnlock(hours);
        if (nextUnlock is null)
        {
            NextUnlockText.Text = "Everything is unlocked. Your garden is complete.";
            NextUnlockBar.Value = 1;
        }
        else
        {
            var prev = Catalog.All.Where(x => x.Hours <= hours).Select(x => x.Hours).DefaultIfEmpty(0).Max();
            NextUnlockText.Text = $"Next unlock: {nextUnlock.Name}, in {Stats.FormatDuration((nextUnlock.Hours - hours) * 3600)} of {Words.TimeUnit}";
            NextUnlockBar.Value = nextUnlock.Hours > prev ? (hours - prev) / (nextUnlock.Hours - prev) : 0;
        }
    }

    private void BuildBars(System.Collections.Generic.IReadOnlyList<DaySummary> week)
    {
        WeekBars.Children.Clear();
        WeekBars.ColumnDefinitions.Clear();
        double max = Math.Max(3600, week.Max(d => d.FocusSeconds));
        var leaf = (Brush)FindResource("Leaf");
        var clay = (Brush)FindResource("Clay");
        var muted = (Brush)FindResource("Muted");
        const double barArea = 104;

        for (int i = 0; i < week.Count; i++)
        {
            var d = week[i];
            WeekBars.ColumnDefinitions.Add(new ColumnDefinition());
            bool isToday = i == week.Count - 1;

            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(4, 0, 4, 0) };
            col.Children.Add(new TextBlock
            {
                Text = d.FocusSeconds >= 60 ? Stats.FormatDuration(d.FocusSeconds) : "",
                FontSize = 10.5,
                Foreground = muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2),
            });
            col.Children.Add(new Rectangle
            {
                Height = Math.Max(2, barArea * d.FocusSeconds / max),
                Fill = leaf,
                Opacity = isToday ? 1 : 0.75,
                RadiusX = 3,
                RadiusY = 3,
            });
            // thin clay line underneath shows distraction time on the same scale
            col.Children.Add(new Rectangle
            {
                Height = d.DistractionSeconds >= 60 ? Math.Max(2, Math.Min(18, barArea * d.DistractionSeconds / max)) : 0,
                Fill = clay,
                Opacity = 0.6,
                Margin = new Thickness(6, 2, 6, 0),
                RadiusX = 2,
                RadiusY = 2,
            });
            col.Children.Add(new TextBlock
            {
                Text = isToday ? "Today" : d.Date.ToString("ddd", CultureInfo.InvariantCulture),
                FontSize = 11,
                FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
            });
            col.ToolTip = Words.Cozy
                ? $"{d.Date:dddd, MMM d}\n{Stats.FormatDuration(d.FocusSeconds)} of growing"
                : $"{d.Date:dddd, MMM d}\n{Stats.FormatDuration(d.FocusSeconds)} focused\n{Stats.FormatDuration(d.DistractionSeconds)} distracted";
            Grid.SetColumn(col, i);
            WeekBars.Children.Add(col);
        }
    }
}
