using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Terrarium.App.Interop;
using Terrarium.App.Services;
using Terrarium.App.Views;
using Terrarium.Core.Activity;
using Terrarium.Core.Garden;
using Terrarium.Core.Model;
using Terrarium.Core.Rendering;
using Terrarium.Core.Storage;
using Terrarium.Core.Updates;
using WF = System.Windows.Forms;
using ActivityKind = Terrarium.Core.Activity.ActivityKind; // System.Diagnostics has one too

namespace Terrarium.App;

/// <summary>
/// Wires everything together: samples activity once a second, feeds the garden engine,
/// animates the widget at 8 fps only while it's visible and someone is there to see it,
/// and owns the tray menu, settings, summaries, saving and updates.
/// </summary>
internal sealed class AppController : IDisposable
{
    private const int FrameMs = 125; // 8 fps: plenty for gentle pixel-art sway

    private readonly Settings _settings;
    private GardenState _garden;
    private GardenEngine _engine;
    private ActivityClassifier _classifier;
    private readonly ActivityMonitor _monitor = new();
    private readonly TerrariumRenderer _renderer = new();
    private readonly PixelCanvas _canvas = new(TerrariumRenderer.Width, TerrariumRenderer.Height);
    private readonly Updater _updater = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _activityTimer;
    private readonly DispatcherTimer _renderTimer;
    private readonly bool _firstRun;

    private WidgetWindow _widget = null!;
    private TrayIcon _tray = null!;
    private SettingsWindow? _settingsWindow;
    private SummaryWindow? _summaryWindow;

    private double _lastTick;
    private long _tickCount;
    private bool _gardenDirty;
    private bool _paused;
    private bool _locked;
    private bool _idle;
    private bool _fullscreenActive;
    private bool _fullscreenHidden;
    private ActivityKind _kind = ActivityKind.Neutral;
    private ReleaseInfo? _availableUpdate;
    private bool _checkingUpdate;
    private bool _installing;
    private bool _shuttingDown;

    public AppController()
    {
        _firstRun = !File.Exists(AppPaths.GardenFile);
        _settings = JsonStore.Load(AppPaths.SettingsFile, () => new Settings());
        _settings.Normalize();
        _garden = JsonStore.Load(AppPaths.GardenFile, () => GardenState.CreateNew(Environment.TickCount));
        _engine = new GardenEngine(_garden, _settings.GraceMinutes * 60.0);
        _classifier = new ActivityClassifier(_settings.Rules);
        ApplyMode();

        _activityTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _activityTimer.Tick += (_, _) => SafeRun(OnActivityTick);
        _renderTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(FrameMs) };
        _renderTimer.Tick += (_, _) => SafeRun(RenderFrame);
    }

    public void Start(bool justUpdated)
    {
        _widget = new WidgetWindow(_settings.Scale)
        {
            HoverTextProvider = HoverText,
            Topmost = _settings.AlwaysOnTop,
        };
        _widget.RightClicked += () => _tray.ShowMenuAtCursor();
        _widget.DoubleClicked += ShowSummary;
        _widget.ScaleRequested += SetScale;
        _widget.Moved += SavePosition;

        _tray = new TrayIcon();
        _tray.MenuOpening += BuildMenu;
        _tray.LeftClicked += ShowWidget;
        _tray.DoubleClicked += ShowSummary;

        RenderFrame();
        if (_settings.WidgetVisible) ShowAndPlaceWidget();

        StartupManager.Apply(_settings.StartWithWindows);
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        _lastTick = _clock.Elapsed.TotalSeconds;
        _activityTimer.Start();
        UpdateAnimation();
        UpdateTooltip();
        SaveAll();

        if (justUpdated)
            _tray.Notify("Terrarium updated", $"You're now on version {Updater.CurrentVersionText}. Your garden came along.");
        else if (_firstRun)
            _tray.Notify("Your terrarium is planted",
                "It grows a little whenever you're at your computer. Drag it anywhere; right-click it for the garden menu.",
                ShowSettings);
    }

    // ───────────────────────────── main loop ─────────────────────────────

    private void OnActivityTick()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = now - _lastTick;
        _lastTick = now;
        _tickCount++;

        var sample = _monitor.Sample();
        bool idle = _locked || sample.IdleSeconds >= _settings.IdleMinutes * 60.0;
        ActivityKind kind = _paused ? ActivityKind.Paused
            : idle ? ActivityKind.Idle
            : _settings.Mode == GardenMode.Cozy ? ActivityKind.Focus // cozy: any time at the PC grows the garden
            : _classifier.Classify(sample.ProcessName, sample.Title);
        if (kind is not (ActivityKind.Paused or ActivityKind.Idle)) _monitor.Record(sample, dt);
        _kind = kind;

        foreach (var ev in _engine.Tick(DateTime.Now, dt, kind)) OnGardenEvent(ev);
        if (kind is ActivityKind.Focus or ActivityKind.Distraction) _gardenDirty = true;

        // Fullscreen: always pause the animation; hide the widget if it shares that monitor.
        bool fs = sample.Fullscreen && sample.Window != _widget.Handle;
        bool hide = fs && _settings.HideWhenFullscreen &&
                    (_widget.Handle == IntPtr.Zero || ActivityMonitor.MonitorOf(sample.Window) == ActivityMonitor.MonitorOf(_widget.Handle));
        _idle = idle;
        _fullscreenActive = fs;
        if (hide != _fullscreenHidden)
        {
            _fullscreenHidden = hide;
            AppPaths.Log(hide
                ? $"Hiding for fullscreen app: {sample.ProcessName} \"{sample.Title}\""
                : "Fullscreen app gone; showing terrarium");
            UpdateWidgetVisibility();
            if (hide) TrimMemory();
        }
        UpdateAnimation();

        if (_tickCount % 5 == 0 && _settings.AlwaysOnTop && _widget.IsVisible) _widget.ReassertTopmost();
        if (_tickCount % 10 == 0)
        {
            UpdateTooltip();
            _widget.RefreshHover();
            _summaryWindow?.Refresh();
        }
        // While not animating, still repaint occasionally so growth and daylight stay current.
        if (!_renderTimer.IsEnabled && _widget.IsVisible && _tickCount % 30 == 0) RenderFrame();
        if (_tickCount % 30 == 0) CheckRecaps();
        if (_tickCount % 60 == 0 && _gardenDirty) SaveGarden();
        if (_tickCount % 60 == 45) MaybeAutoCheckUpdates();
        if (_tickCount == 20) TrimMemory();
    }

    private void RenderFrame()
    {
        var local = DateTime.Now;
        _renderer.Render(_canvas, SceneInput.FromState(_garden, local.TimeOfDay.TotalHours, _clock.Elapsed.TotalSeconds));
        _widget.Present(_canvas.Pixels);
    }

    private void UpdateAnimation()
    {
        bool animate = _widget.IsVisible && !_fullscreenActive && !_idle;
        if (animate == _renderTimer.IsEnabled) return;
        if (animate)
        {
            RenderFrame();
            _renderTimer.Start();
        }
        else
        {
            _renderTimer.Stop();
        }
    }

    private bool _placed;

    /// <summary>Shows the widget; the first time, places it once it has its real size.</summary>
    private void ShowAndPlaceWidget()
    {
        RenderFrame();
        _widget.Show();
        _widget.UpdateLayout();
        if (!_placed)
        {
            _widget.RestorePosition(_settings.WindowLeft, _settings.WindowTop);
            _placed = true;
            var (x, y) = _widget.GetPhysicalPosition();
            AppPaths.Log($"Terrarium {Updater.CurrentVersionText} shown at {x},{y} (scale {_settings.Scale}, DPI {System.Windows.Media.VisualTreeHelper.GetDpi(_widget).DpiScaleX:0.##})");
        }
        _widget.SetAlwaysOnTop(_settings.AlwaysOnTop);
    }

    private void ResetPosition()
    {
        _settings.WindowLeft = null;
        _settings.WindowTop = null;
        _settings.WidgetVisible = true;
        _fullscreenHidden = false;
        UpdateWidgetVisibility();
        _widget.RestorePosition(null, null);
        SavePosition();
    }

    private void UpdateWidgetVisibility()
    {
        bool show = _settings.WidgetVisible && !_fullscreenHidden;
        if (show && !_widget.IsVisible)
        {
            ShowAndPlaceWidget();
        }
        else if (!show && _widget.IsVisible)
        {
            _widget.Hide();
        }
        UpdateAnimation();
    }

    private void OnGardenEvent(GardenEvent ev)
    {
        _gardenDirty = true;
        _tray.Notify(ev.Title, ev.Message, ShowSummary);
        if (ev.Kind is GardenEventKind.SlotOpened or GardenEventKind.Unlocked or GardenEventKind.StageUp) SaveGarden();
    }

    private string StatusText() => _kind switch
    {
        ActivityKind.Paused => "Tracking paused",
        ActivityKind.Idle => "Resting while you're away",
        ActivityKind.Focus => "Growing",
        _ when _settings.Mode == GardenMode.Cozy => "Growing",
        ActivityKind.Distraction => _engine.IsWilting ? "Wilting. Back to work?" : "Distracted",
        _ => "Waiting for focus",
    };

    private string HoverText()
    {
        var today = Stats.ForDay(_garden, DateTime.Now);
        return $"{Words.Today(Stats.FormatDuration(today.FocusSeconds))}\n{StatusText()}";
    }

    private void UpdateTooltip()
    {
        if (_installing) return;
        var today = Stats.ForDay(_garden, DateTime.Now);
        _tray.SetTooltip($"Terrarium · {Words.Today(Stats.FormatDuration(today.FocusSeconds))} · {StatusText()}");
    }

    // ───────────────────────────── tray menu ─────────────────────────────

    private void BuildMenu()
    {
        var items = _tray.Items;
        items.Clear();
        var today = Stats.ForDay(_garden, DateTime.Now);
        items.Add(new WF.ToolStripMenuItem($"{Words.Today(Stats.FormatDuration(today.FocusSeconds))} · {StatusText()}") { Enabled = false });

        if (_availableUpdate is ReleaseInfo r)
        {
            var upd = Item($"Install update {r.Tag}…", PromptInstall);
            upd.Font = new System.Drawing.Font(upd.Font, System.Drawing.FontStyle.Bold);
            upd.Enabled = !_installing;
            items.Add(upd);
        }
        items.Add(new WF.ToolStripSeparator());

        items.Add(Item(_settings.WidgetVisible ? "Hide terrarium" : "Show terrarium", ToggleWidget));
        items.Add(Item("Move to bottom-right corner", ResetPosition));

        var size = new WF.ToolStripMenuItem("Size");
        foreach (var (label, scale) in new[] { ("Small", 2), ("Medium", 3), ("Large", 4), ("Extra large", 6) })
            size.DropDownItems.Add(Check(label, _settings.Scale == scale, () => SetScale(scale)));
        size.DropDownItems.Add(new WF.ToolStripMenuItem("Tip: Ctrl + scroll on the terrarium") { Enabled = false });
        items.Add(size);

        items.Add(BuildGardenMenu());
        items.Add(Item("Summary…", ShowSummary));
        items.Add(Check("Pause tracking", _paused, TogglePause));
        items.Add(Item("Settings…", ShowSettings));
        var check = Item(_checkingUpdate ? "Checking for updates…" : "Check for updates", () => _ = CheckForUpdatesAsync(interactive: true));
        check.Enabled = !_checkingUpdate && !_installing;
        items.Add(check);
        items.Add(new WF.ToolStripSeparator());
        items.Add(Item("Quit Terrarium", Quit));
    }

    private WF.ToolStripMenuItem BuildGardenMenu()
    {
        double hours = _engine.UnlockHours;
        var garden = new WF.ToolStripMenuItem("Garden");

        for (int i = 0; i < Catalog.SlotCount; i++)
        {
            int slot = i;
            var p = _garden.Slots[i];
            if (p is null)
            {
                garden.DropDownItems.Add(new WF.ToolStripMenuItem($"Pot {i + 1}: unlocks at {Catalog.SlotHours[i]:0.#}h of {Words.TimeUnit}") { Enabled = false });
                continue;
            }
            var sp = Catalog.SpeciesOf(p.SpeciesId);
            string stage = Catalog.StageNames[Catalog.StageFor(p.GrowthHours)];
            string health = p.Health < GardenEngine.WiltNoticeThreshold ? ", drooping" : "";
            var sub = new WF.ToolStripMenuItem($"Pot {i + 1}: {sp.Name} ({stage}{health})");
            sub.DropDownItems.Add(new WF.ToolStripMenuItem("Replant with…") { Enabled = false });
            foreach (var s in Catalog.Species)
            {
                if (Catalog.IsUnlocked(s, hours))
                {
                    string id = s.Id;
                    sub.DropDownItems.Add(Item(s.Name, () => Replant(slot, id)));
                }
                else
                {
                    sub.DropDownItems.Add(new WF.ToolStripMenuItem($"{s.Name} (unlocks at {s.Hours:0.#}h)") { Enabled = false });
                }
            }
            garden.DropDownItems.Add(sub);
        }
        garden.DropDownItems.Add(new WF.ToolStripSeparator());

        var pots = new WF.ToolStripMenuItem("Pot style");
        foreach (var pot in Catalog.Pots)
        {
            string id = pot.Id;
            pots.DropDownItems.Add(Catalog.IsUnlocked(pot, hours)
                ? Check(pot.Name, _garden.PotId == id, () => { _garden.PotId = id; GardenChanged(); })
                : new WF.ToolStripMenuItem($"{pot.Name} (unlocks at {pot.Hours:0.#}h)") { Enabled = false });
        }
        garden.DropDownItems.Add(pots);

        var decos = new WF.ToolStripMenuItem("Decorations");
        foreach (var d in Catalog.Decorations)
        {
            string id = d.Id;
            decos.DropDownItems.Add(Catalog.IsUnlocked(d, hours)
                ? Check(d.Name, _garden.Decorations.Contains(id), () => ToggleDecoration(id))
                : new WF.ToolStripMenuItem($"{d.Name} (unlocks at {d.Hours:0.#}h)") { Enabled = false });
        }
        garden.DropDownItems.Add(decos);

        var next = Catalog.NextUnlock(hours);
        if (next is not null)
            garden.DropDownItems.Add(new WF.ToolStripMenuItem(
                $"Next: {next.Name} in {Stats.FormatDuration((next.Hours - hours) * 3600)} of {Words.TimeUnit}") { Enabled = false });
        return garden;
    }

    private static WF.ToolStripMenuItem Item(string text, Action onClick)
    {
        var item = new WF.ToolStripMenuItem(text);
        item.Click += (_, _) => SafeRun(onClick);
        return item;
    }

    private static WF.ToolStripMenuItem Check(string text, bool isChecked, Action onClick)
    {
        var item = Item(text, onClick);
        item.Checked = isChecked;
        return item;
    }

    // ───────────────────────────── actions ─────────────────────────────

    public void ShowWidget()
    {
        if (!_settings.WidgetVisible)
        {
            _settings.WidgetVisible = true;
            SaveSettings();
        }
        UpdateWidgetVisibility();
    }

    private void ToggleWidget()
    {
        _settings.WidgetVisible = !_settings.WidgetVisible;
        SaveSettings();
        UpdateWidgetVisibility();
    }

    private void SetScale(int scale)
    {
        scale = Math.Clamp(scale, 2, 8);
        if (scale == _settings.Scale) return;
        _settings.Scale = scale;
        _widget.SetScaleAnchored(scale);
        SavePosition();
    }

    private void SavePosition()
    {
        var (x, y) = _widget.GetPhysicalPosition();
        _settings.WindowLeft = x;
        _settings.WindowTop = y;
        SaveSettings();
    }

    private void TogglePause()
    {
        _paused = !_paused;
        _kind = _paused ? ActivityKind.Paused : ActivityKind.Neutral;
        UpdateTooltip();
    }

    private void Replant(int slot, string speciesId)
    {
        var current = _garden.Slots[slot];
        if (current is not null)
        {
            var from = Catalog.SpeciesOf(current.SpeciesId).Name.ToLowerInvariant();
            var to = Catalog.SpeciesOf(speciesId).Name.ToLowerInvariant();
            var answer = MessageBox.Show(
                $"Replant pot {slot + 1} with a {to}?\n\nThe {from} goes to the compost and the new seed starts from scratch. Your history and unlocks are kept.",
                "Replant", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;
        }
        _engine.PlantSlot(slot, speciesId);
        GardenChanged();
    }

    private void ToggleDecoration(string id)
    {
        if (!_garden.Decorations.Remove(id)) _garden.Decorations.Add(id);
        GardenChanged();
    }

    private void GardenChanged()
    {
        SaveGarden();
        RenderFrame();
    }

    private void ShowSummary()
    {
        if (_summaryWindow is not null)
        {
            _summaryWindow.Refresh();
            _summaryWindow.Activate();
            return;
        }
        _summaryWindow = new SummaryWindow(() => _garden) { Icon = IconFactory.WindowIcon };
        _summaryWindow.Closed += (_, _) => _summaryWindow = null;
        _summaryWindow.Show();
        _summaryWindow.Activate();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(Clone(_settings), _monitor.RecentApps())
        {
            Icon = IconFactory.WindowIcon,
        };
        _settingsWindow.Saved += ApplySettings;
        _settingsWindow.CheckUpdatesRequested += () => _ = CheckForUpdatesAsync(interactive: true);
        _settingsWindow.OpenDataFolderRequested += () =>
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataDir}\"") { UseShellExecute = true });
        _settingsWindow.ResetGardenRequested += ResetGarden;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ApplySettings(Settings edited)
    {
        _settings.Rules = edited.Rules.Select(r => r.Clone()).ToList();
        _settings.IdleMinutes = edited.IdleMinutes;
        _settings.GraceMinutes = edited.GraceMinutes;
        _settings.StartWithWindows = edited.StartWithWindows;
        _settings.AlwaysOnTop = edited.AlwaysOnTop;
        _settings.HideWhenFullscreen = edited.HideWhenFullscreen;
        _settings.CheckForUpdates = edited.CheckForUpdates;
        _settings.DailyRecap = edited.DailyRecap;
        _settings.WeeklyRecap = edited.WeeklyRecap;
        _settings.RecapHour = edited.RecapHour;
        _settings.Mode = edited.Mode;
        _settings.Normalize();
        ApplyMode();
        RenderFrame();

        _classifier = new ActivityClassifier(_settings.Rules);
        _engine.GraceSeconds = _settings.GraceMinutes * 60.0;
        StartupManager.Apply(_settings.StartWithWindows);
        _widget.SetAlwaysOnTop(_settings.AlwaysOnTop);
        if (!_settings.HideWhenFullscreen && _fullscreenHidden)
        {
            _fullscreenHidden = false;
            UpdateWidgetVisibility();
        }
        SaveSettings();
    }

    /// <summary>Cozy: grows with any time at the PC at half speed and never wilts. Focus: the original rules.</summary>
    private void ApplyMode()
    {
        bool cozy = _settings.Mode == GardenMode.Cozy;
        Words.Cozy = cozy;
        _engine.GrowthRate = cozy ? 0.5 : 1.0;
        _engine.UnlockEverything = cozy;
        if (cozy && _engine.UnlockAllNow()) SaveGarden(); // every pot gets a seed, decorations switch on once
        if (cozy)
        {
            // Leaving focus mode: let any droopy plants recover instead of staying sad forever.
            _garden.DistractionStreakSeconds = 0;
            _garden.WiltNoticeShown = false;
            foreach (var p in _garden.Slots) if (p is not null) p.Health = 1.0;
        }
    }

    private void ResetGarden()
    {
        var answer = MessageBox.Show(
            "Start a brand-new garden?\n\nAll plants, unlocks and history will be erased. This can't be undone.",
            "Reset garden", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;
        _garden = GardenState.CreateNew(Environment.TickCount);
        _engine = new GardenEngine(_garden, _settings.GraceMinutes * 60.0);
        ApplyMode();
        GardenChanged();
        _summaryWindow?.Refresh();
    }

    private void CheckRecaps()
    {
        var now = DateTime.Now;
        if (now.Hour < _settings.RecapHour) return;
        string today = GardenState.DayKey(now);
        if (_settings.WeeklyRecap && now.DayOfWeek == DayOfWeek.Friday && _settings.LastWeeklyRecapDate != today)
        {
            _settings.LastWeeklyRecapDate = today;
            _settings.LastDailyRecapDate = today; // the weekly recap covers today too
            SaveSettings();
            string weekly = Words.Cozy
                ? $"Your garden grew for {Stats.FormatDuration(Stats.LastDays(_garden, now).Sum(d => d.FocusSeconds))} this week."
                : Stats.WeeklyRecap(_garden, now);
            _tray.Notify("Your week in the garden", weekly, ShowSummary);
        }
        else if (_settings.DailyRecap && _settings.LastDailyRecapDate != today)
        {
            _settings.LastDailyRecapDate = today;
            SaveSettings();
            string daily = Words.Cozy
                ? $"Your garden grew for {Stats.FormatDuration(Stats.ForDay(_garden, now).FocusSeconds)} today."
                : Stats.DailyRecap(_garden, now);
            _tray.Notify("Daily recap", daily, ShowSummary);
        }
    }

    // ───────────────────────────── updates ─────────────────────────────

    private void MaybeAutoCheckUpdates()
    {
        if (!_settings.CheckForUpdates || _checkingUpdate || _installing) return;
        if (_settings.LastUpdateCheckUtc is DateTime last && DateTime.UtcNow - last < TimeSpan.FromHours(24)) return;
        _ = CheckForUpdatesAsync(interactive: false);
    }

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        if (_checkingUpdate || _installing) return;
        _checkingUpdate = true;
        try
        {
            var release = await _updater.CheckAsync();
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            SaveSettings();
            if (release is null)
            {
                _availableUpdate = null;
                if (interactive)
                    MessageBox.Show($"You're on the latest version ({Updater.CurrentVersionText}).", "Terrarium",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _availableUpdate = release;
            if (interactive) PromptInstall();
            else if (_settings.SkippedVersion != release.Tag)
                _tray.Notify("Update available", $"Terrarium {release.Tag.TrimStart('v')} is ready. Click here to install.", PromptInstall);
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Update check failed: {ex.Message}");
            if (interactive)
                MessageBox.Show($"Couldn't check for updates.\n\n{ex.Message}", "Terrarium", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _checkingUpdate = false;
        }
    }

    private void PromptInstall()
    {
        if (_availableUpdate is not ReleaseInfo r || _installing) return;
        string notes = string.IsNullOrWhiteSpace(r.Notes) ? "" : "\n\nWhat's new:\n" + Truncate(r.Notes.Trim(), 600);
        var answer = MessageBox.Show(
            $"Terrarium {r.Tag.TrimStart('v')} is available. You have {Updater.CurrentVersionText}.{notes}\n\n" +
            "Yes: install now (Terrarium restarts in a few seconds; your garden is saved)\n" +
            "No: skip this version\n" +
            "Cancel: remind me later",
            "Update Terrarium", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
        {
            _ = InstallUpdateAsync(r);
        }
        else if (answer == MessageBoxResult.No)
        {
            _settings.SkippedVersion = r.Tag;
            _availableUpdate = null;
            SaveSettings();
        }
    }

    private async Task InstallUpdateAsync(ReleaseInfo release)
    {
        _installing = true;
        _tray.SetTooltip("Terrarium · downloading update…");
        try
        {
            var progress = new Progress<double>(p => _tray.SetTooltip($"Terrarium · downloading update {p:P0}"));
            var exe = await _updater.DownloadAndStageAsync(release, progress);
            SaveAll();
            Updater.LaunchUpdated(exe);
            Quit();
        }
        catch (Exception ex)
        {
            _installing = false;
            AppPaths.Log($"Update install failed: {ex}");
            UpdateTooltip();
            MessageBox.Show($"The update couldn't be installed.\n\n{ex.Message}\n\nYou can always download it from {Updater.ReleasesPage}",
                "Terrarium", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";

    // ───────────────────────────── system events & persistence ─────────────────────────────

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e) =>
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (e.Mode == PowerModes.Suspend) SaveAll();
            if (e.Mode == PowerModes.Resume) _lastTick = _clock.Elapsed.TotalSeconds;
        });

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e) =>
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            {
                _locked = true;
                SaveAll();
            }
            else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            {
                _locked = false;
            }
        });

    private void SaveGarden()
    {
        try
        {
            JsonStore.Save(AppPaths.GardenFile, _garden);
            _gardenDirty = false;
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Saving garden failed: {ex.Message}");
        }
    }

    private void SaveSettings()
    {
        try
        {
            JsonStore.Save(AppPaths.SettingsFile, _settings);
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Saving settings failed: {ex.Message}");
        }
    }

    public void SaveAll()
    {
        SaveGarden();
        SaveSettings();
    }

    private static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonStore.Options), JsonStore.Options)!;

    /// <summary>Releases startup memory back to Windows. Purely cosmetic for Task Manager, but cheap.</summary>
    private static void TrimMemory()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            using var p = Process.GetCurrentProcess();
            Native.EmptyWorkingSet(p.Handle);
        }
        catch
        {
            // best effort
        }
    }

    private static void SafeRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Error: {ex}");
        }
    }

    public void Quit()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        SaveAll();
        Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _activityTimer.Stop();
        _renderTimer.Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _tray?.Dispose();
        try { _widget?.Close(); } catch { /* already closing */ }
    }
}
