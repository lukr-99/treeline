using Treeline.Core.Models;

namespace Treeline.Core.Storage;

/// <summary>Persistence for user-added <see cref="TrackedSource"/> roots.</summary>
public interface ISourceStore
{
    IReadOnlyList<TrackedSource> GetAll();
    TrackedSource? Get(string id);
    void Add(TrackedSource source);
    void Update(TrackedSource source);
    void Remove(string id);
}

/// <summary>Persistence for arbitrary key/value configuration.</summary>
public interface IConfigStore
{
    string? Get(string key);
    string GetOrDefault(string key, string fallback);
    void Set(string key, string? value);
    IReadOnlyDictionary<string, string> GetAll();
}
