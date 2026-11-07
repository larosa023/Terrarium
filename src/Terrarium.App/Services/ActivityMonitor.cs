using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terrarium.App.Interop;

namespace Terrarium.App.Services;

internal readonly record struct ActivitySample(string ProcessName, string Title, double IdleSeconds, IntPtr Window, bool Fullscreen);

/// <summary>A recently seen foreground app, offered in Settings so rules are one click to add.</summary>
public sealed class SeenApp
{
    public string ProcessName { get; init; } = "";
    public string LastTitle { get; set; } = "";
    public DateTime LastSeen { get; set; }
    public double Seconds { get; set; }
}

/// <summary>
/// Reads the foreground window, its process, idle time, and whether something fullscreen is running.
/// Pure Win32 calls; nothing is recorded beyond an in-memory "recently seen apps" list for Settings.
/// </summary>
internal sealed class ActivityMonitor
{
    private readonly Dictionary<uint, string> _pidNames = new();
    private readonly Dictionary<string, SeenApp> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly char[] _pathBuf = new char[1024];

    public ActivitySample Sample()
    {
        double idle = Native.IdleSeconds();
        IntPtr hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return new ActivitySample("", "", idle, hwnd, false);

        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        string name = ProcessName(pid);
        string title = Native.GetWindowTitle(hwnd);
        bool fullscreen = IsFullscreen(hwnd);
        return new ActivitySample(name, title, idle, hwnd, fullscreen);
    }

    public void Record(ActivitySample s, double seconds)
    {
        if (string.IsNullOrEmpty(s.ProcessName) || s.ProcessName.Equals("Terrarium", StringComparison.OrdinalIgnoreCase)) return;
        if (!_seen.TryGetValue(s.ProcessName, out var app))
        {
            if (_seen.Count > 60)
            {
                var oldest = _seen.Values.OrderBy(a => a.LastSeen).First();
                _seen.Remove(oldest.ProcessName);
            }
            app = new SeenApp { ProcessName = s.ProcessName };
            _seen[s.ProcessName] = app;
        }
        app.LastSeen = DateTime.Now;
        app.Seconds += seconds;
        if (!string.IsNullOrWhiteSpace(s.Title)) app.LastTitle = s.Title;
    }

    public IReadOnlyList<SeenApp> RecentApps() => _seen.Values.OrderByDescending(a => a.LastSeen).ToList();

    private string ProcessName(uint pid)
    {
        if (pid == 0) return "";
        if (_pidNames.TryGetValue(pid, out var cached)) return cached;
        string name = "";
        IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h != IntPtr.Zero)
        {
            try
            {
                int size = _pathBuf.Length;
                if (Native.QueryFullProcessImageName(h, 0, _pathBuf, ref size))
                    name = Path.GetFileNameWithoutExtension(new string(_pathBuf, 0, size));
            }
            finally { Native.CloseHandle(h); }
        }
        if (_pidNames.Count > 256) _pidNames.Clear(); // PIDs get reused; keep the cache small and fresh
        _pidNames[pid] = name;
        return name;
    }

    /// <summary>
    /// True for games, videos and presentations taking a whole monitor. Maximized windows never count.
    /// </summary>
    public static bool IsFullscreen(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || hwnd == Native.GetDesktopWindow() || hwnd == Native.GetShellWindow()) return false;
        string cls = Native.GetClassName(hwnd);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (!Native.IsWindowVisible(hwnd)) return false;

        // Maximized windows (including frameless apps like Discord or VS Code) are normal work, not fullscreen,
        // even when they cover a monitor that has no taskbar.
        if (Native.IsZoomed(hwnd)) return false;

        if (!Native.GetWindowRect(hwnd, out var r)) return false;
        IntPtr mon = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
        var mi = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(mon, ref mi)) return false;
        return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top &&
               r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
    }

    public static IntPtr MonitorOf(IntPtr hwnd) => Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
}
