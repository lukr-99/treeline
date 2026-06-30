using Treeline.Core.Models;

namespace Treeline.Core.Storage;

/// <summary>JSON-file backed implementation of <see cref="ISourceStore"/> (sources.json).</summary>
public sealed class SourceStore : ISourceStore
{
    private readonly string _file;
    private readonly object _gate = new();
    private List<TrackedSource> _items;

    public SourceStore(string? file = null)
    {
        _file = file ?? TreelinePaths.SourcesFile;
        _items = JsonFile.Load(_file, () => new List<TrackedSource>());
    }

    public IReadOnlyList<TrackedSource> GetAll()
    {
        lock (_gate) return _items.ToList();
    }

    public TrackedSource? Get(string id)
    {
        lock (_gate) return _items.FirstOrDefault(s => s.Id == id);
    }

    public void Add(TrackedSource source)
    {
        lock (_gate)
        {
            _items.Add(source);
            Persist();
        }
    }

    public void Update(TrackedSource source)
    {
        lock (_gate)
        {
            var idx = _items.FindIndex(s => s.Id == source.Id);
            if (idx >= 0) _items[idx] = source;
            Persist();
        }
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            _items.RemoveAll(s => s.Id == id);
            Persist();
        }
    }

    private void Persist() => JsonFile.Save(_file, _items);
}
