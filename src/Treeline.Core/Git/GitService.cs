using Treeline.Core.Models;

namespace Treeline.Core.Git;

/// <summary>
/// Implements <see cref="IGitService"/> by parsing porcelain output from <see cref="GitRunner"/>.
/// All methods are read-or-write wrappers; none of them prompt or block on credentials.
/// </summary>
public sealed class GitService : IGitService
{
    private const char US = ''; // unit separator
    private const char RS = ''; // record separator
    private readonly GitRunner _git;

    public GitService(GitRunner git) => _git = git;

    public Task<string?> GetVersionAsync(CancellationToken ct = default) => _git.TryGetVersionAsync(ct);

    public async Task<string?> GetCommonDirAsync(string path, CancellationToken ct = default)
    {
        var r = await _git.RunAsync(path, ["rev-parse", "--path-format=absolute", "--git-common-dir"], ct);
        return r.Success ? r.StdOut.Trim() : null;
    }

    public async Task<string?> GetTopLevelAsync(string path, CancellationToken ct = default)
    {
        var r = await _git.RunAsync(path, ["rev-parse", "--show-toplevel"], ct);
        return r.Success ? r.StdOut.Trim() : null;
    }

    public async Task<string?> GetRemoteUrlAsync(string repoPath, CancellationToken ct = default)
    {
        var r = await _git.RunAsync(repoPath, ["remote", "get-url", "origin"], ct);
        return r.Success && r.StdOut.Trim().Length > 0 ? r.StdOut.Trim() : null;
    }

    public async Task<string?> GetCurrentBranchAsync(string repoPath, CancellationToken ct = default)
    {
        var r = await _git.RunAsync(repoPath, ["rev-parse", "--abbrev-ref", "HEAD"], ct);
        if (!r.Success) return null;
        var b = r.StdOut.Trim();
        return b == "HEAD" ? null : b; // detached
    }

    public async Task<IReadOnlyList<Worktree>> GetWorktreesAsync(string repoPath, CancellationToken ct = default)
    {
        var r = await _git.RunAsync(repoPath, ["worktree", "list", "--porcelain"], ct);
        if (!r.Success) return [];

        // Pass 1: parse worktree entries (cheap, no extra git calls).
        var raws = new List<RawWorktree>();
        RawWorktree? cur = null;
        void Flush() { if (cur is not null) { raws.Add(cur); cur = null; } }
        foreach (var line in r.StdOut.Split('\n'))
        {
            var l = line.TrimEnd('\r');
            if (l.Length == 0) { Flush(); continue; }
            if (l.StartsWith("worktree ")) { Flush(); cur = new RawWorktree { Path = l[9..] }; }
            else if (cur is null) continue;
            else if (l.StartsWith("HEAD ")) cur.Head = l[5..];
            else if (l.StartsWith("branch ")) cur.Branch = ShortenRef(l[7..]);
            else if (l == "bare") cur.Bare = true;
            else if (l == "detached") cur.Detached = true;
            else if (l.StartsWith("locked")) { cur.Locked = true; cur.LockReason = l.Length > 7 ? l[7..] : null; }
            else if (l.StartsWith("prunable")) cur.Prunable = true;
        }
        Flush();

        // Pass 2: read working-tree status for each worktree in parallel (bounded).
        var result = new Worktree[raws.Count];
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(raws.Select(async (e, i) =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var exists = Directory.Exists(e.Path);
                var (status, ahead, behind, upstream) = exists && !e.Bare
                    ? await ReadStatusAsync(e.Path, ct).ConfigureAwait(false)
                    : (new WorkingTreeStatus(), 0, 0, null);
                result[i] = new Worktree
                {
                    Path = e.Path,
                    Head = e.Head,
                    Branch = e.Branch,
                    IsBare = e.Bare,
                    IsDetached = e.Detached,
                    IsLocked = e.Locked,
                    IsPrunable = e.Prunable,
                    LockReason = e.LockReason,
                    IsMain = i == 0,
                    Status = status,
                    Ahead = ahead,
                    Behind = behind,
                    Upstream = upstream,
                    Exists = exists,
                };
            }
            finally { gate.Release(); }
        })).ConfigureAwait(false);

        return result;
    }

    private sealed class RawWorktree
    {
        public required string Path { get; init; }
        public string Head { get; set; } = "";
        public string? Branch { get; set; }
        public bool Bare, Detached, Locked, Prunable;
        public string? LockReason { get; set; }
    }

    private async Task<(WorkingTreeStatus status, int ahead, int behind, string? upstream)> ReadStatusAsync(
        string worktreePath, CancellationToken ct)
    {
        var r = await _git.RunAsync(worktreePath, ["status", "--porcelain=v1", "--branch"], ct);
        if (!r.Success) return (new WorkingTreeStatus(), 0, 0, null);

        int staged = 0, modified = 0, untracked = 0, conflicted = 0, ahead = 0, behind = 0;
        string? upstream = null;

        foreach (var raw in r.StdOut.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            if (line.StartsWith("## "))
            {
                (upstream, ahead, behind) = ParseBranchHeader(line[3..]);
                continue;
            }
            if (line.Length < 2) continue;
            var x = line[0];
            var y = line[1];
            if (x == '?' && y == '?') { untracked++; continue; }
            if (IsConflict(x, y)) { conflicted++; continue; }
            if (x != ' ' && x != '?') staged++;
            if (y != ' ' && y != '?') modified++;
        }

        return (new WorkingTreeStatus
        {
            Staged = staged,
            Modified = modified,
            Untracked = untracked,
            Conflicted = conflicted,
        }, ahead, behind, upstream);
    }

    private static bool IsConflict(char x, char y) =>
        (x, y) is ('U', _) or (_, 'U') or ('A', 'A') or ('D', 'D');

    private static (string? upstream, int ahead, int behind) ParseBranchHeader(string header)
    {
        // Examples: "main", "main...origin/main", "main...origin/main [ahead 1, behind 2]"
        string? upstream = null;
        int ahead = 0, behind = 0;
        var trackStart = header.IndexOf(" [", StringComparison.Ordinal);
        var nameAndUpstream = trackStart >= 0 ? header[..trackStart] : header;
        var sep = nameAndUpstream.IndexOf("...", StringComparison.Ordinal);
        if (sep >= 0) upstream = nameAndUpstream[(sep + 3)..];
        if (trackStart >= 0)
        {
            var track = header[(trackStart + 2)..].TrimEnd(']');
            foreach (var part in track.Split(','))
            {
                var p = part.Trim();
                if (p.StartsWith("ahead ")) int.TryParse(p[6..], out ahead);
                else if (p.StartsWith("behind ")) int.TryParse(p[7..], out behind);
            }
        }
        return (upstream, ahead, behind);
    }

    public async Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(
        string repoPath, bool includeRemote = true, CancellationToken ct = default)
    {
        var refs = new List<string> { "refs/heads" };
        if (includeRemote) refs.Add("refs/remotes");

        var format = string.Join(US.ToString(), new[]
        {
            "%(HEAD)", "%(refname:short)", "%(upstream:short)",
            "%(upstream:track,nobracket)", "%(committerdate:iso-strict)", "%(contents:subject)"
        });

        var args = new List<string> { "for-each-ref", "--format=" + format };
        args.AddRange(refs);
        var r = await _git.RunAsync(repoPath, args, ct);
        if (!r.Success) return [];

        var list = new List<BranchInfo>();
        foreach (var raw in r.StdOut.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            var f = line.Split(US);
            if (f.Length < 6) continue;
            var name = f[1];
            if (name.EndsWith("/HEAD")) continue; // skip remote HEAD pointer
            var (ahead, behind) = ParseTrack(f[3]);
            DateTimeOffset? date = DateTimeOffset.TryParse(f[4], out var d) ? d : null;
            list.Add(new BranchInfo
            {
                Name = name,
                IsCurrent = f[0] == "*",
                IsRemote = name.Contains('/') && includeRemote && !File.Exists(Path.Combine(repoPath, name)),
                Upstream = string.IsNullOrWhiteSpace(f[2]) ? null : f[2],
                Ahead = ahead,
                Behind = behind,
                LastCommitDate = date,
                LastCommitSubject = string.IsNullOrWhiteSpace(f[5]) ? null : f[5],
            });
        }
        // Local branches first, then by most recent commit.
        return list
            .OrderByDescending(b => b.IsCurrent)
            .ThenBy(b => b.IsRemote)
            .ThenByDescending(b => b.LastCommitDate ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private static (int ahead, int behind) ParseTrack(string track)
    {
        int ahead = 0, behind = 0;
        foreach (var part in track.Split(','))
        {
            var p = part.Trim();
            if (p.StartsWith("ahead ")) int.TryParse(p[6..], out ahead);
            else if (p.StartsWith("behind ")) int.TryParse(p[7..], out behind);
        }
        return (ahead, behind);
    }

    public async Task<IReadOnlyList<GitCommit>> GetLogAsync(
        string worktreePath, int skip, int take, CancellationToken ct = default)
    {
        var pretty = string.Join(US.ToString(), new[] { "%H", "%an", "%ae", "%aI", "%s", "%b" }) + RS;
        var r = await _git.RunAsync(worktreePath,
            ["log", $"--skip={Math.Max(0, skip)}", $"-n{Math.Max(1, take)}", "--pretty=format:" + pretty], ct);
        if (!r.Success) return [];

        var commits = new List<GitCommit>();
        foreach (var record in r.StdOut.Split(RS))
        {
            var rec = record.Trim('\n', '\r');
            if (rec.Length == 0) continue;
            var f = rec.Split(US);
            if (f.Length < 5) continue;
            commits.Add(new GitCommit
            {
                Sha = f[0],
                Author = f[1],
                Email = f[2],
                Date = DateTimeOffset.TryParse(f[3], out var d) ? d : DateTimeOffset.MinValue,
                Subject = f[4],
                Body = f.Length > 5 && f[5].Trim().Length > 0 ? f[5].Trim() : null,
            });
        }
        return commits;
    }

    // ---- operations ----

    public Task<GitResult> FetchAsync(string repoPath, bool prune = true, CancellationToken ct = default)
    {
        var args = new List<string> { "fetch", "--all" };
        if (prune) args.Add("--prune");
        return _git.RunAsync(repoPath, args, ct);
    }

    public Task<GitResult> PullAsync(string worktreePath, CancellationToken ct = default) =>
        _git.RunAsync(worktreePath, ["pull", "--ff-only"], ct);

    public Task<GitResult> CheckoutAsync(string worktreePath, string branch, CancellationToken ct = default) =>
        _git.RunAsync(worktreePath, ["checkout", branch], ct);

    public Task<GitResult> CreateBranchAsync(string repoPath, string name, string? startPoint, CancellationToken ct = default)
    {
        var args = new List<string> { "branch", name };
        if (!string.IsNullOrWhiteSpace(startPoint)) args.Add(startPoint!);
        return _git.RunAsync(repoPath, args, ct);
    }

    public Task<GitResult> AddWorktreeAsync(string repoPath, string path, string? branch, bool createBranch, CancellationToken ct = default)
    {
        var args = new List<string> { "worktree", "add" };
        if (createBranch && !string.IsNullOrWhiteSpace(branch)) { args.Add("-b"); args.Add(branch!); args.Add(path); }
        else { args.Add(path); if (!string.IsNullOrWhiteSpace(branch)) args.Add(branch!); }
        return _git.RunAsync(repoPath, args, ct);
    }

    public Task<GitResult> PruneWorktreesAsync(string repoPath, CancellationToken ct = default) =>
        _git.RunAsync(repoPath, ["worktree", "prune", "-v"], ct);

    public Task<GitResult> RemoveWorktreeAsync(string repoPath, string worktreePath, bool force, CancellationToken ct = default)
    {
        var args = new List<string> { "worktree", "remove" };
        if (force) args.Add("--force");
        args.Add(worktreePath);
        return _git.RunAsync(repoPath, args, ct);
    }

    public Task<GitResult> DeleteBranchAsync(string repoPath, string name, bool force, CancellationToken ct = default) =>
        _git.RunAsync(repoPath, ["branch", force ? "-D" : "-d", name], ct);

    public async Task<GitResult> DiscardChangesAsync(string worktreePath, CancellationToken ct = default)
    {
        var reset = await _git.RunAsync(worktreePath, ["reset", "--hard", "HEAD"], ct);
        if (!reset.Success) return reset;
        return await _git.RunAsync(worktreePath, ["clean", "-fd"], ct);
    }

    private static string ShortenRef(string fullRef) =>
        fullRef.StartsWith("refs/heads/") ? fullRef["refs/heads/".Length..] : fullRef;
}
