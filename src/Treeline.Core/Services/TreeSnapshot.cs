using Treeline.Core.Models;

namespace Treeline.Core.Services;

/// <summary>A tracked source together with the repositories discovered under it.</summary>
public sealed class SourceNode
{
    public required TrackedSource Source { get; init; }
    public IReadOnlyList<Repository> Repositories { get; init; } = [];
    public string? Error { get; init; }
    public int RepositoryCount => Repositories.Count;
}

/// <summary>Immutable read model served to the UI and agents. Replaced wholesale on refresh.</summary>
public sealed class TreeSnapshot
{
    public required DateTimeOffset GeneratedAt { get; init; }
    public long Revision { get; init; }
    public string? GitVersion { get; init; }
    public IReadOnlyList<SourceNode> Sources { get; init; } = [];

    public int TotalRepositories => Sources.Sum(s => s.RepositoryCount);
    public int TotalWorktrees => Sources.Sum(s => s.Repositories.Sum(r => r.WorktreeCount));

    public static TreeSnapshot Empty => new() { GeneratedAt = DateTimeOffset.UtcNow, Sources = [] };

    public Repository? FindRepository(string repoId) =>
        Sources.SelectMany(s => s.Repositories).FirstOrDefault(r => r.Id == repoId);
}
