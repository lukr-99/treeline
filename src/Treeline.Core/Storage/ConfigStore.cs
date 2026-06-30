namespace Treeline.Core.Storage;

/// <summary>JSON-file backed implementation of <see cref="IConfigStore"/> (config.json).</summary>
public sealed class ConfigStore : IConfigStore
{
    private readonly string _file;
    private readonly object _gate = new();
    private Dictionary<string, string> _items;

    public ConfigStore(string? file = null)
    {
        _file = file ?? TreelinePaths.ConfigFile;
        _items = JsonFile.Load(_file, () => new Dictionary<string, string>());
    }

    public string? Get(string key)
    {
        lock (_gate) return _items.TryGetValue(key, out var v) ? v : null;
    }

    public string GetOrDefault(string key, string fallback) => Get(key) ?? fallback;

    public void Set(string key, string? value)
    {
        lock (_gate)
        {
            if (value is null) _items.Remove(key);
            else _items[key] = value;
            JsonFile.Save(_file, _items);
        }
    }

    public IReadOnlyDictionary<string, string> GetAll()
    {
        lock (_gate) return new Dictionary<string, string>(_items);
    }
}
