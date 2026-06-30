namespace Treeline.Core.Models;

/// <summary>One worktree belonging to a <see cref="Repository"/> (the main checkout is also a worktree).</summary>
public sealed class Worktree
{
    public required string Path { get; init; }
    public required string Head { get; init; }
    public string? Branch { get; init; }
    public bool IsMain { get; init; }
    public bool IsBare { get; init; }
    public bool IsDetached { get; init; }
    public bool IsLocked { get; init; }
    public bool IsPrunable { get; init; }
    public string? LockReason { get; init; }
    public int Ahead { get; init; }
    public int Behind { get; init; }
    public string? Upstream { get; init; }
    public WorkingTreeStatus Status { get; init; } = new();
    public bool Exists { get; init; } = true;
}

/// <summary>
/// A git repository discovered from a <see cref="TrackedSource"/>. Holds the
/// last refreshed snapshot of its worktrees and high-level metadata.
/// </summary>
public sealed class Repository
{
    /// <summary>Stable id derived from the canonical common-dir path.</summary>
    public required string Id { get; init; }
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required string SourceId { get; init; }
    public string? RemoteUrl { get; init; }
    public string? CurrentBranch { get; init; }
    public bool IsValid { get; init; } = true;
    public string? Error { get; init; }
    public IReadOnlyList<Worktree> Worktrees { get; init; } = [];
    public DateTimeOffset RefreshedAt { get; init; } = DateTimeOffset.UtcNow;

    public int WorktreeCount => Worktrees.Count;
    public bool HasDirtyWorktree => Worktrees.Any(w => w.Status.IsDirty);
}
