namespace Treeline.Core.Models;

/// <summary>A single commit as shown in the log views.</summary>
public sealed class GitCommit
{
    public required string Sha { get; init; }
    public string ShortSha => Sha.Length >= 7 ? Sha[..7] : Sha;
    public required string Author { get; init; }
    public required string Email { get; init; }
    public required DateTimeOffset Date { get; init; }
    public required string Subject { get; init; }
    public string? Body { get; init; }
}

/// <summary>A local or remote branch.</summary>
public sealed class BranchInfo
{
    public required string Name { get; init; }
    public bool IsCurrent { get; init; }
    public bool IsRemote { get; init; }
    public string? Upstream { get; init; }
    public int Ahead { get; init; }
    public int Behind { get; init; }
    public DateTimeOffset? LastCommitDate { get; init; }
    public string? LastCommitSubject { get; init; }
}

/// <summary>Working-tree status counts for a worktree.</summary>
public sealed class WorkingTreeStatus
{
    public int Staged { get; init; }
    public int Modified { get; init; }
    public int Untracked { get; init; }
    public int Conflicted { get; init; }
    public bool IsDirty => Staged + Modified + Untracked + Conflicted > 0;
}
