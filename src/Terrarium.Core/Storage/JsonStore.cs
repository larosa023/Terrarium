using System.Text.Json;
using System.Text.Json.Serialization;

namespace Terrarium.Core.Storage;

/// <summary>
/// Small local JSON persistence. Writes are atomic (temp file + replace) so a crash or power
/// loss mid-save can't corrupt the garden. A corrupt file is set aside as *.bad and defaults are used.
/// </summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T Load<T>(string path, Func<T> fallback) where T : class
    {
        try
        {
            if (!File.Exists(path)) return fallback();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, Options) ?? fallback();
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            try { File.Copy(path, path + ".bad", overwrite: true); } catch { /* best effort */ }
            return fallback();
        }
    }

    public static void Save<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        var json = JsonSerializer.Serialize(value, Options);
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }
}
