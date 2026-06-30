using Treeline.Core.Git;
using Treeline.Core.Models;
using Treeline.Core.Storage;

namespace Treeline.Core.Services;

/// <summary>
/// Central read model. Builds and caches a <see cref="TreeSnapshot"/> of every tracked
/// source, repository and worktree, and supports targeted refreshes. Thread-safe: readers
/// always see a complete, immutable snapshot; writers are serialized.
/// </summary>
public sealed class SnapshotService
{
    private readonly ISourceStore _sources;
    private readonly IGitService _git;
    private readonly RepositoryScanner _scanner;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private const int MaxParallelism = 6;

    private volatile TreeSnapshot _current = TreeSnapshot.Empty;

    public SnapshotService(ISourceStore sources, IGitService git, RepositoryScanner scanner)
    {
        _sources = sources;
        _git = git;
        _scanner = scanner;
    }

    public TreeSnapshot Current => _current;

    /// <summary>Raised after the cached snapshot is replaced.</summary>
    public event Action<TreeSnapshot>? Updated;

    public async Task<TreeSnapshot> RefreshAllAsync(CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var gitVersion = await _git.GetVersionAsync(ct);
            var sources = _sources.GetAll();
            var nodes = new SourceNode[sources.Count];
            for (var i = 0; i < sources.Count; i++)
                nodes[i] = await BuildSourceNodeAsync(sources[i], ct);

            _current = new TreeSnapshot
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                GitVersion = gitVersion,
                Sources = nodes,
            };
        }
        finally { _writeLock.Release(); }

        Updated?.Invoke(_current);
        return _current;
    }

    public async Task<TreeSnapshot> RefreshSourceAsync(string sourceId, CancellationToken ct = default)
    {
        var source = _sources.Get(sourceId);
        if (source is null) return _current;

        var node = await BuildSourceNodeAsync(source, ct);
        await ReplaceAsync(snap =>
        {
            var list = snap.Sources.ToList();
            var idx = list.FindIndex(s => s.Source.Id == sourceId);
            if (idx >= 0) list[idx] = node; else list.Add(node);
            return list;
        }, ct);
        return _current;
    }

    public async Task<TreeSnapshot> RefreshRepoAsync(string repoId, CancellationToken ct = default)
    {
        var existing = _current.FindRepository(repoId);
        if (existing is null) return _current;

        var source = _sources.Get(existing.SourceId);
        if (source is null) return _current;

        var rebuilt = await BuildRepositoryAsync(existing.SourceId, existing.Path, ct);
        await ReplaceAsync(snap =>
        {
            var list = snap.Sources.ToList();
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Source.Id != existing.SourceId) continue;
                var repos = list[i].Repositories.ToList();
                var ri = repos.FindIndex(r => r.Id == repoId);
                if (ri >= 0) repos[ri] = rebuilt;
                list[i] = new SourceNode { Source = list[i].Source, Repositories = repos, Error = list[i].Error };
            }
            return list;
        }, ct);
        return _current;
    }

    private async Task ReplaceAsync(Func<TreeSnapshot, List<SourceNode>> mutate, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            _current = new TreeSnapshot
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                GitVersion = _current.GitVersion,
                Sources = mutate(_current),
            };
        }
        finally { _writeLock.Release(); }
        Updated?.Invoke(_current);
    }

    private async Task<SourceNode> BuildSourceNodeAsync(TrackedSource source, CancellationToken ct)
    {
        if (!Directory.Exists(source.Path))
            return new SourceNode { Source = source, Error = "Path no longer exists." };

        var candidates = _scanner.Discover(source);

        // Collapse linked worktrees that live inside the source onto their main repo by common-dir id.
        var byId = new Dictionary<string, string>();
        foreach (var path in candidates)
        {
            var commonDir = await _git.GetCommonDirAsync(path, ct);
            if (commonDir is null) continue;
            var id = PathId.ForRepo(commonDir);
            byId.TryAdd(id, path);
        }

        var repos = new List<Repository>(byId.Count);
        var sem = new SemaphoreSlim(MaxParallelism);
        var tasks = byId.Select(async kv =>
        {
            await sem.WaitAsync(ct);
            try { return await BuildRepositoryAsync(source.Id, kv.Value, ct); }
            finally { sem.Release(); }
        });
        repos.AddRange(await Task.WhenAll(tasks));

        repos.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return new SourceNode { Source = source, Repositories = repos };
    }

    private async Task<Repository> BuildRepositoryAsync(string sourceId, string repoPath, CancellationToken ct)
    {
        var commonDir = await _git.GetCommonDirAsync(repoPath, ct);
        if (commonDir is null)
            return new Repository
            {
                Id = PathId.ForRepo(repoPath), Path = repoPath, Name = Path.GetFileName(repoPath.TrimEnd('\\', '/')),
                SourceId = sourceId, IsValid = false, Error = "Not a git repository.",
            };

        var mainPath = ResolveMainPath(commonDir, repoPath);
        var name = Path.GetFileName(mainPath.TrimEnd('\\', '/'));
        try
        {
            var worktreesTask = _git.GetWorktreesAsync(mainPath, ct);
            var remoteTask = _git.GetRemoteUrlAsync(mainPath, ct);
            var branchTask = _git.GetCurrentBranchAsync(mainPath, ct);
            await Task.WhenAll(worktreesTask, remoteTask, branchTask);

            return new Repository
            {
                Id = PathId.ForRepo(commonDir),
                Path = mainPath,
                Name = name,
                SourceId = sourceId,
                RemoteUrl = remoteTask.Result,
                CurrentBranch = branchTask.Result,
                Worktrees = worktreesTask.Result,
                IsValid = true,
                RefreshedAt = DateTimeOffset.UtcNow,
            };
        }
        catch (Exception ex)
        {
            return new Repository
            {
                Id = PathId.ForRepo(commonDir), Path = mainPath, Name = name, SourceId = sourceId,
                IsValid = false, Error = ex.Message,
            };
        }
    }

    /// <summary>The main worktree path is the parent of the ".git" common dir, when that dir is a normal repo.</summary>
    private static string ResolveMainPath(string commonDir, string fallback)
    {
        var trimmed = commonDir.TrimEnd('\\', '/');
        if (Path.GetFileName(trimmed).Equals(".git", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(trimmed);
            if (!string.IsNullOrEmpty(parent)) return parent;
        }
        return fallback;
    }
}
