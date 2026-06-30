namespace Treeline.Core.Models;

/// <summary>How a tracked source is interpreted on disk.</summary>
public enum SourceType
{
    /// <summary>A directory that is scanned (up to <see cref="TrackedSource.ScanDepth"/>) for git repositories.</summary>
    Folder,
    /// <summary>A single git repository (its own directory contains a .git entry).</summary>
    Repo
}

/// <summary>
/// A user-added root that Treeline watches. Persisted in the local database.
/// A <see cref="SourceType.Folder"/> may expand into many repositories; a
/// <see cref="SourceType.Repo"/> maps to exactly one.
/// </summary>
public sealed class TrackedSource
{
    public required string Id { get; init; }
    public required string Path { get; set; }
    public required SourceType Type { get; set; }
    public string? DisplayName { get; set; }
    public int ScanDepth { get; set; } = 3;
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;

    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveName =>
        string.IsNullOrWhiteSpace(DisplayName)
            ? System.IO.Path.GetFileName(Path.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : Path
            : DisplayName!;
}
