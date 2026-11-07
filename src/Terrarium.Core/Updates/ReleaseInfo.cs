using System.Text.Json;

namespace Terrarium.Core.Updates;

public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);

public sealed record ReleaseInfo(Version Version, string Tag, string Notes, string PageUrl, bool Prerelease, IReadOnlyList<ReleaseAsset> Assets)
{
    public const string ExeAssetName = "Terrarium.exe";
    public const string HashAssetName = "Terrarium.exe.sha256";

    public ReleaseAsset? Exe => Assets.FirstOrDefault(a => a.Name.Equals(ExeAssetName, StringComparison.OrdinalIgnoreCase));
    public ReleaseAsset? Hash => Assets.FirstOrDefault(a => a.Name.Equals(HashAssetName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Parses the GitHub "get latest release" JSON. Returns null if the tag isn't a version.</summary>
    public static ReleaseInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        var version = VersionUtil.TryParse(tag);
        if (version is null) return null;

        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in arr.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                long size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : 0;
                if (name is not null && url is not null) assets.Add(new ReleaseAsset(name, url, size));
            }
        }

        return new ReleaseInfo(
            version,
            tag,
            root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
            root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "",
            root.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True,
            assets);
    }

    /// <summary>Extracts the hex digest from a "sha256sum"-style file ("&lt;hex&gt;  Terrarium.exe").</summary>
    public static string? ParseSha256(string content)
    {
        var token = content.Trim().Split([' ', '\t', '\r', '\n', '*'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (token is null || token.Length != 64 || !token.All(Uri.IsHexDigit)) return null;
        return token.ToLowerInvariant();
    }
}

public static class VersionUtil
{
    /// <summary>"v1.2.3", "1.2", "1.2.3-beta" → Version (prerelease suffix ignored).</summary>
    public static Version? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        int cut = s.IndexOfAny(['-', '+']);
        if (cut >= 0) s = s[..cut];
        if (!Version.TryParse(s, out var v)) return null;
        return Normalize(v);
    }

    /// <summary>Treats missing build/revision as 0 so 1.2 == 1.2.0 == 1.2.0.0.</summary>
    public static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    public static bool IsNewer(Version candidate, Version current) => Normalize(candidate) > Normalize(current);
}
