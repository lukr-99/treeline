using Treeline.Core.Models;
using Treeline.Core.Storage;

namespace Treeline.Core.Services;

public sealed record AddSourceResult(bool Ok, TrackedSource? Source, string? Error);

/// <summary>Validates and persists tracked sources, keeping ids stable and deduped.</summary>
public sealed class SourceManager
{
    private readonly ISourceStore _store;

    public SourceManager(ISourceStore store) => _store = store;

    public IReadOnlyList<TrackedSource> All() => _store.GetAll();

    public AddSourceResult Add(string path, SourceType type, string? displayName, int scanDepth = 3)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new AddSourceResult(false, null, "Path is required.");

        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception ex) { return new AddSourceResult(false, null, $"Invalid path: {ex.Message}"); }

        if (!Directory.Exists(fullPath))
            return new AddSourceResult(false, null, "Directory does not exist.");

        if (type == SourceType.Repo &&
            !(Directory.Exists(Path.Combine(fullPath, ".git")) || File.Exists(Path.Combine(fullPath, ".git"))))
            return new AddSourceResult(false, null, "Directory is not a git repository (no .git found).");

        var id = PathId.ForSource(fullPath);
        if (_store.Get(id) is not null)
            return new AddSourceResult(false, null, "This path is already tracked.");

        var source = new TrackedSource
        {
            Id = id,
            Path = fullPath,
            Type = type,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            ScanDepth = Math.Clamp(scanDepth, 1, 8),
        };
        _store.Add(source);
        return new AddSourceResult(true, source, null);
    }

    public bool Remove(string id)
    {
        if (_store.Get(id) is null) return false;
        _store.Remove(id);
        return true;
    }

    public TrackedSource? Update(string id, string? displayName, int? scanDepth)
    {
        var s = _store.Get(id);
        if (s is null) return null;
        if (displayName is not null) s.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        if (scanDepth is not null) s.ScanDepth = Math.Clamp(scanDepth.Value, 1, 8);
        _store.Update(s);
        return s;
    }
}
