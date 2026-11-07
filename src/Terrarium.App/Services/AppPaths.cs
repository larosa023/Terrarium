using System;
using System.IO;

namespace Terrarium.App.Services;

/// <summary>Everything Terrarium stores lives under %LOCALAPPDATA%\Terrarium. Nothing leaves the PC.</summary>
internal static class AppPaths
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Terrarium");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string GardenFile => Path.Combine(DataDir, "garden.json");
    public static string LogFile => Path.Combine(DataDir, "terrarium.log");

    /// <summary>Full path of the running Terrarium.exe (single-file aware).</summary>
    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Terrarium.exe");

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var fi = new FileInfo(LogFile);
            if (fi.Exists && fi.Length > 256 * 1024) fi.Delete();
            File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch
        {
            // logging must never take the app down
        }
    }
}
