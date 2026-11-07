using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Terrarium.App.Services;
using Terrarium.Core.Activity;
using Terrarium.Core.Model;

namespace Terrarium.App.Views;

public sealed class RuleRow
{
    public AppRule Rule { get; }
    public RuleRow(AppRule rule) => Rule = rule;
    public string KindText => Rule.Kind == ActivityKind.Focus ? "Focus" : "Distraction";
    public string TargetText => Rule.Target == MatchTarget.Process ? "App name is" : "Title has";
    public string Pattern => Rule.Pattern;
}

public partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private readonly ObservableCollection<RuleRow> _rules;

    internal event Action<Settings>? Saved;
    internal event Action? CheckUpdatesRequested;
    internal event Action? OpenDataFolderRequested;
    internal event Action? ResetGardenRequested;

    internal SettingsWindow(Settings editableCopy, IReadOnlyList<SeenApp> recentApps)
    {
        InitializeComponent();
        _settings = editableCopy;

        CozyModeRadio.IsChecked = _settings.Mode == GardenMode.Cozy;
        FocusModeRadio.IsChecked = _settings.Mode == GardenMode.Focus;
        AlwaysOnTopBox.IsChecked = _settings.AlwaysOnTop;
        HideFullscreenBox.IsChecked = _settings.HideWhenFullscreen;
        StartWithWindowsBox.IsChecked = _settings.StartWithWindows;
        IdleSlider.Value = _settings.IdleMinutes;
        GraceSlider.Value = _settings.GraceMinutes;
        DailyRecapBox.IsChecked = _settings.DailyRecap;
        WeeklyRecapBox.IsChecked = _settings.WeeklyRecap;
        for (int h = 0; h < 24; h++)
            RecapHourBox.Items.Add(new DateTime(2000, 1, 1, h, 0, 0).ToString("h:00 tt", System.Globalization.CultureInfo.InvariantCulture));
        RecapHourBox.SelectedIndex = _settings.RecapHour;
        CheckUpdatesBox.IsChecked = _settings.CheckForUpdates;
        VersionText.Text = $"You're running Terrarium {Updater.CurrentVersionText}.";

        _rules = new ObservableCollection<RuleRow>(_settings.Rules.Select(r => new RuleRow(r.Clone())));
        RulesList.ItemsSource = _rules;

        RecentList.ItemsSource = recentApps;
        RecentEmpty.Visibility = recentApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _settings.Mode = FocusModeRadio.IsChecked == true ? GardenMode.Focus : GardenMode.Cozy;
        _settings.AlwaysOnTop = AlwaysOnTopBox.IsChecked == true;
        _settings.HideWhenFullscreen = HideFullscreenBox.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsBox.IsChecked == true;
        _settings.IdleMinutes = (int)Math.Round(IdleSlider.Value);
        _settings.GraceMinutes = (int)Math.Round(GraceSlider.Value);
        _settings.DailyRecap = DailyRecapBox.IsChecked == true;
        _settings.WeeklyRecap = WeeklyRecapBox.IsChecked == true;
        _settings.RecapHour = Math.Max(0, RecapHourBox.SelectedIndex);
        _settings.CheckForUpdates = CheckUpdatesBox.IsChecked == true;
        _settings.Rules = _rules.Select(r => r.Rule).ToList();
        Saved?.Invoke(_settings);
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnRemoveRule(object sender, RoutedEventArgs e)
    {
        foreach (var row in RulesList.SelectedItems.Cast<RuleRow>().ToList()) _rules.Remove(row);
    }

    private void OnRestoreDefaults(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, "Replace your rules with the default list?", "Restore defaults",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        _rules.Clear();
        foreach (var r in Settings.DefaultRules()) _rules.Add(new RuleRow(r));
    }

    private void OnMarkFocus(object sender, RoutedEventArgs e) => MarkSelected(ActivityKind.Focus);
    private void OnMarkDistraction(object sender, RoutedEventArgs e) => MarkSelected(ActivityKind.Distraction);

    private void MarkSelected(ActivityKind kind)
    {
        var picked = RecentList.SelectedItems.Cast<SeenApp>().ToList();
        if (picked.Count == 0)
        {
            MessageBox.Show(this, "Select one or more apps on the right first.", "Terrarium", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        foreach (var app in picked) Upsert(MatchTarget.Process, app.ProcessName, kind);
    }

    private void OnAddRule(object sender, RoutedEventArgs e)
    {
        var pattern = NewPatternBox.Text.Trim();
        if (pattern.Length == 0)
        {
            NewPatternBox.Focus();
            return;
        }
        var kind = NewKindBox.SelectedIndex == 1 ? ActivityKind.Distraction : ActivityKind.Focus;
        var target = NewTargetBox.SelectedIndex == 0 ? MatchTarget.Process : MatchTarget.Title;
        if (target == MatchTarget.Process) pattern = ActivityClassifier.NormalizeProcess(pattern);
        Upsert(target, pattern, kind);
        NewPatternBox.Clear();
    }

    /// <summary>Adds a rule, replacing any existing rule for the same app/title so there are no contradictions.</summary>
    private void Upsert(MatchTarget target, string pattern, ActivityKind kind)
    {
        var existing = _rules.FirstOrDefault(r => r.Rule.Target == target &&
                                                  string.Equals(r.Rule.Pattern, pattern, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) _rules.Remove(existing);
        var row = new RuleRow(new AppRule(target, pattern, kind));
        _rules.Insert(0, row);
        RulesList.SelectedItem = row;
        RulesList.ScrollIntoView(row);
    }

    private void OnCheckUpdates(object sender, RoutedEventArgs e) => CheckUpdatesRequested?.Invoke();
    private void OnOpenData(object sender, RoutedEventArgs e) => OpenDataFolderRequested?.Invoke();
    private void OnResetGarden(object sender, RoutedEventArgs e) => ResetGardenRequested?.Invoke();
}
