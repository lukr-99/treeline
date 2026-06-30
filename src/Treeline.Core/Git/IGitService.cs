using Treeline.Core.Models;

namespace Treeline.Core.Git;

/// <summary>High-level git operations used by the rest of the application.</summary>
public interface IGitService
{
    Task<string?> GetVersionAsync(CancellationToken ct = default);

    /// <summary>Returns the canonical git common directory, or null if <paramref name="path"/> is not a repo.</summary>
    Task<string?> GetCommonDirAsync(string path, CancellationToken ct = default);
    Task<string?> GetTopLevelAsync(string path, CancellationToken ct = default);
    Task<string?> GetRemoteUrlAsync(string repoPath, CancellationToken ct = default);
    Task<string?> GetCurrentBranchAsync(string repoPath, CancellationToken ct = default);

    Task<IReadOnlyList<Worktree>> GetWorktreesAsync(string repoPath, CancellationToken ct = default);
    Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(string repoPath, bool includeRemote = true, CancellationToken ct = default);
    Task<IReadOnlyList<GitCommit>> GetLogAsync(string worktreePath, int skip, int take, CancellationToken ct = default);

    // Non-destructive operations
    Task<GitResult> FetchAsync(string repoPath, bool prune = true, CancellationToken ct = default);
    Task<GitResult> PullAsync(string worktreePath, CancellationToken ct = default);
    Task<GitResult> CheckoutAsync(string worktreePath, string branch, CancellationToken ct = default);
    Task<GitResult> CreateBranchAsync(string repoPath, string name, string? startPoint, CancellationToken ct = default);
    Task<GitResult> AddWorktreeAsync(string repoPath, string path, string? branch, bool createBranch, CancellationToken ct = default);
    Task<GitResult> PruneWorktreesAsync(string repoPath, CancellationToken ct = default);

    // Destructive operations (callers must enforce confirmation before invoking)
    Task<GitResult> RemoveWorktreeAsync(string repoPath, string worktreePath, bool force, CancellationToken ct = default);
    Task<GitResult> DeleteBranchAsync(string repoPath, string name, bool force, CancellationToken ct = default);
    Task<GitResult> DiscardChangesAsync(string worktreePath, CancellationToken ct = default);
}
