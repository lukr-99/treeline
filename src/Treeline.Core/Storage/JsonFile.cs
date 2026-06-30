using System.Text.Json;

namespace Treeline.Core.Storage;

/// <summary>Load/save helper for the small JSON files that back the stores. Writes atomically.</summary>
internal static class JsonFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static T Load<T>(string path, Func<T> fallback)
    {
        try
        {
            if (!File.Exists(path)) return fallback();
            var json = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(json) ? fallback() : JsonSerializer.Deserialize<T>(json, Options) ?? fallback();
        }
        catch
        {
            return fallback();
        }
    }

    public static void Save<T>(string path, T value)
    {
        var json = JsonSerializer.Serialize(value, Options);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }
}
