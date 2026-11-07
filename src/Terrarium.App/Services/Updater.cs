using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Terrarium.Core.Updates;

namespace Terrarium.App.Services;

/// <summary>
/// Self-updater backed by GitHub Releases of larosa023/Terrarium.
/// 1. GET /releases/latest and compare its tag with this build's version.
/// 2. Download Terrarium.exe and Terrarium.exe.sha256 next to the running exe; verify the hash.
/// 3. Rename the running exe to Terrarium.exe.old (Windows allows renaming a running exe),
///    move the new one into place, launch it with --updated, and exit. The new process
///    deletes the .old file on startup. Any failure rolls back.
/// This is the only network traffic Terrarium ever makes, and it can be turned off in Settings.
/// </summary>
internal sealed class Updater
{
    public const string Owner = "larosa023";
    public const string Repo = "Terrarium";
    private static readonly string LatestUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
    public static string ReleasesPage => $"https://github.com/{Owner}/{Repo}/releases";

    private static readonly HttpClient Http = CreateClient();

    public static Version CurrentVersion { get; } = VersionUtil.Normalize(
        VersionUtil.TryParse(Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
        ?? Assembly.GetExecutingAssembly().GetName().Version
        ?? new Version(0, 0, 0, 0));

    public static string CurrentVersionText => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Terrarium-Updater", "1.0"));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    /// <summary>Returns the latest release if it is newer than this build, otherwise null.</summary>
    public async Task<ReleaseInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync(LatestUrl, ct).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // no releases yet
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var release = ReleaseInfo.Parse(json);
        if (release is null || release.Prerelease || release.Exe is null) return null;
        return VersionUtil.IsNewer(release.Version, CurrentVersion) ? release : null;
    }

    /// <summary>Downloads, verifies and swaps in the new exe. Returns the path to launch.</summary>
    public async Task<string> DownloadAndStageAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct = default)
    {
        var exeAsset = release.Exe ?? throw new InvalidOperationException("The release has no Terrarium.exe.");
        var hashAsset = release.Hash ?? throw new InvalidOperationException("The release has no checksum file, so it can't be verified.");

        string exe = AppPaths.ExePath;
        string dir = Path.GetDirectoryName(exe)!;
        string download = Path.Combine(dir, "Terrarium.exe.download");
        string old = exe + ".old";

        string expected = ReleaseInfo.ParseSha256(await Http.GetStringAsync(hashAsset.DownloadUrl, ct).ConfigureAwait(false))
                          ?? throw new InvalidOperationException("The checksum file is malformed.");

        try
        {
            using (var resp = await Http.GetAsync(exeAsset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? exeAsset.Size;
                await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = new FileStream(download, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                var buf = new byte[81920];
                long done = 0;
                int n;
                while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    done += n;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Terrarium can't write to its own folder ({dir}). Move Terrarium.exe somewhere you own, such as %LOCALAPPDATA%\\Programs\\Terrarium, and try again.");
        }

        string actual;
        await using (var fs = File.OpenRead(download))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false)).ToLowerInvariant();
        if (actual != expected)
        {
            TryDelete(download);
            throw new InvalidOperationException("The download didn't match its checksum and was discarded.");
        }

        // Swap.
        TryDelete(old);
        File.Move(exe, old);
        try
        {
            File.Move(download, exe);
        }
        catch
        {
            File.Move(old, exe); // roll back
            TryDelete(download);
            throw;
        }
        return exe;
    }

    public static void LaunchUpdated(string exe)
    {
        Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = false });
    }

    /// <summary>Removes the previous version left behind by an update. Called at startup.</summary>
    public static void CleanupAfterUpdate()
    {
        string old = AppPaths.ExePath + ".old";
        for (int i = 0; i < 10 && File.Exists(old); i++)
        {
            if (TryDelete(old)) break;
            Thread.Sleep(300); // the old process may still be exiting
        }
        TryDelete(Path.Combine(Path.GetDirectoryName(AppPaths.ExePath)!, "Terrarium.exe.download"));
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
