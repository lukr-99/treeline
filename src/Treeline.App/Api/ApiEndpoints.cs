using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Treeline.Core.Git;
using Treeline.Core.Models;
using Treeline.Core.Services;

namespace Treeline.App.Api;

/// <summary>Maps every HTTP endpoint Treeline exposes on loopback (consumed by the UI and by agents).</summary>
public static class ApiEndpoints
{
    public static void MapTreelineApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/health", (SnapshotService snap) =>
        {
            var s = snap.Current;
            return Results.Ok(new
            {
                status = "ok",
                version = typeof(ApiEndpoints).Assembly.GetName().Version?.ToString(),
                gitVersion = s.GitVersion,
                dataDirectory = Treeline.Core.Storage.TreelinePaths.DataDirectory,
                generatedAt = s.GeneratedAt,
                sources = s.Sources.Count,
                repositories = s.TotalRepositories,
                worktrees = s.TotalWorktrees,
            });
        });

        // ---- read model ----
        api.MapGet("/snapshot", (SnapshotService snap) =>
        {
            snap.MarkActive();
            return Results.Ok(snap.Current);
        });

        // Cheap poll target: clients check this and only fetch the full snapshot when it changes.
        api.MapGet("/snapshot/revision", (SnapshotService snap) =>
        {
            snap.MarkActive();
            return Results.Ok(new { revision = snap.Revision, generatedAt = snap.Current.GeneratedAt });
        });

        api.MapGet("/repos/{id}", (string id, SnapshotService snap) =>
            snap.Current.FindRepository(id) is { } repo ? Results.Ok(repo) : Results.NotFound());

        api.MapGet("/repos/{id}/branches", async (string id, bool? remote, SnapshotService snap, IGitService git) =>
        {
            var repo = snap.Current.FindRepository(id);
            if (repo is null) return Results.NotFound();
            return Results.Ok(await git.GetBranchesAsync(repo.Path, remote ?? true));
        });

        api.MapGet("/repos/{id}/log", async (string id, string worktree, int? skip, int? take,
            SnapshotService snap, IGitService git) =>
        {
            var repo = snap.Current.FindRepository(id);
            if (repo is null) return Results.NotFound();
            if (!TryResolveWorktree(repo, worktree, out var wt)) return Results.BadRequest(Err("Unknown worktree."));
            return Results.Ok(await git.GetLogAsync(wt.Path, skip ?? 0, take ?? 5));
        });

        // ---- filesystem (folder picker + reveal) ----
        api.MapGet("/fs", (string? path, FileSystemBrowser fs) =>
        {
            try { return Results.Ok(fs.Browse(path)); }
            catch (DirectoryNotFoundException) { return Results.BadRequest(Err("Directory not found.")); }
            catch (Exception ex) { return Results.BadRequest(Err(ex.Message)); }
        });

        api.MapPost("/fs/reveal", (RevealRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.Path) || !Directory.Exists(req.Path))
                return Results.BadRequest(Err("Path does not exist."));
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(req.Path) { UseShellExecute = true });
                return Results.Ok(new { ok = true });
            }
            catch (Exception ex) { return Results.BadRequest(Err(ex.Message)); }
        });

        // ---- sources ----
        api.MapGet("/sources", (SourceManager sources) => Results.Ok(sources.All()));

        api.MapPost("/sources", async (AddSourceRequest req, SourceManager sources, SnapshotService snap) =>
        {
            var r = sources.Add(req.Path, req.Type, req.DisplayName, req.ScanDepth ?? 3);
            if (!r.Ok) return Results.BadRequest(Err(r.Error!));
            await snap.RefreshSourceAsync(r.Source!.Id);
            return Results.Ok(r.Source);
        });

        api.MapPatch("/sources/{id}", async (string id, UpdateSourceRequest req, SourceManager sources, SnapshotService snap) =>
        {
            var s = sources.Update(id, req.DisplayName, req.ScanDepth);
            if (s is null) return Results.NotFound();
            await snap.RefreshSourceAsync(id);
            return Results.Ok(s);
        });

        // Untracking a source removes nothing on disk, so it needs no destructive confirmation.
        api.MapDelete("/sources/{id}", async (string id, SourceManager sources, SnapshotService snap) =>
        {
            if (!sources.Remove(id)) return Results.NotFound();
            await snap.RefreshAllAsync();
            return Results.Ok(new { ok = true });
        });

        // ---- refresh ----
        api.MapPost("/refresh", async (SnapshotService snap) => Results.Ok(await snap.RefreshAllAsync()));
        api.MapPost("/refresh/source/{id}", async (string id, SnapshotService snap) => Results.Ok(await snap.RefreshSourceAsync(id)));
        api.MapPost("/refresh/repo/{id}", async (string id, SnapshotService snap) => Results.Ok(await snap.RefreshRepoAsync(id)));

        // ---- non-destructive git operations ----
        api.MapPost("/repos/{id}/fetch", (string id, SnapshotService snap, IGitService git) =>
            WithRepo(snap, id, repo => Run(git.FetchAsync(repo.Path), snap, id)));

        api.MapPost("/repos/{id}/pull", (string id, PullRequest req, SnapshotService snap, IGitService git) =>
            WithWorktree(snap, id, req.Worktree, (repo, wt) => Run(git.PullAsync(wt.Path), snap, id)));

        api.MapPost("/repos/{id}/checkout", (string id, CheckoutRequest req, SnapshotService snap, IGitService git) =>
            WithWorktree(snap, id, req.Worktree, (repo, wt) => Run(git.CheckoutAsync(wt.Path, req.Branch), snap, id)));

        api.MapPost("/repos/{id}/branch", (string id, CreateBranchRequest req, SnapshotService snap, IGitService git) =>
            WithRepo(snap, id, repo => Run(git.CreateBranchAsync(repo.Path, req.Name, req.From), snap, id)));

        api.MapPost("/repos/{id}/worktree", (string id, AddWorktreeRequest req, SnapshotService snap, IGitService git) =>
            WithRepo(snap, id, repo => Run(git.AddWorktreeAsync(repo.Path, req.Path, req.Branch, req.CreateBranch), snap, id)));

        api.MapPost("/repos/{id}/prune", (string id, SnapshotService snap, IGitService git) =>
            WithRepo(snap, id, repo => Run(git.PruneWorktreesAsync(repo.Path), snap, id)));

        // ---- destructive operations (two-phase confirm) ----
        api.MapPost("/repos/{id}/worktree/remove", (string id, RemoveWorktreeRequest req,
            SnapshotService snap, IGitService git, ConfirmationService confirm) =>
        {
            var repo = snap.Current.FindRepository(id);
            if (repo is null) return Task.FromResult(Results.NotFound());
            if (!TryResolveWorktree(repo, req.Worktree, out var wt)) return Task.FromResult(Results.BadRequest(Err("Unknown worktree.")));
            var sig = ConfirmationService.Signature("worktree.remove", id, PathId.Normalize(wt.Path));
            return Guarded(confirm, req.ConfirmToken, sig,
                $"Remove worktree '{wt.Path}'. This deletes the working directory from disk.",
                () => Run(git.RemoveWorktreeAsync(repo.Path, wt.Path, req.Force), snap, id));
        });

        api.MapPost("/repos/{id}/branch/delete", (string id, DeleteBranchRequest req,
            SnapshotService snap, IGitService git, ConfirmationService confirm) =>
        {
            var repo = snap.Current.FindRepository(id);
            if (repo is null) return Task.FromResult(Results.NotFound());
            var sig = ConfirmationService.Signature("branch.delete", id, req.Name);
            return Guarded(confirm, req.ConfirmToken, sig,
                $"Delete branch '{req.Name}'{(req.Force ? " (force, may discard unmerged commits)" : "")}.",
                () => Run(git.DeleteBranchAsync(repo.Path, req.Name, req.Force), snap, id));
        });

        api.MapPost("/repos/{id}/discard", (string id, DiscardRequest req,
            SnapshotService snap, IGitService git, ConfirmationService confirm) =>
        {
            var repo = snap.Current.FindRepository(id);
            if (repo is null) return Task.FromResult(Results.NotFound());
            if (!TryResolveWorktree(repo, req.Worktree, out var wt)) return Task.FromResult(Results.BadRequest(Err("Unknown worktree.")));
            var sig = ConfirmationService.Signature("discard", id, PathId.Normalize(wt.Path));
            return Guarded(confirm, req.ConfirmToken, sig,
                $"Discard ALL local changes in '{wt.Path}' (git reset --hard + clean -fd). Unsaved work is lost.",
                () => Run(git.DiscardChangesAsync(wt.Path), snap, id));
        });
    }

    // ---- helpers ----

    private static async Task<IResult> WithRepo(SnapshotService snap, string id, Func<Repository, Task<IResult>> action)
    {
        var repo = snap.Current.FindRepository(id);
        return repo is null ? Results.NotFound() : await action(repo);
    }

    private static async Task<IResult> WithWorktree(SnapshotService snap, string id, string worktree,
        Func<Repository, Worktree, Task<IResult>> action)
    {
        var repo = snap.Current.FindRepository(id);
        if (repo is null) return Results.NotFound();
        if (!TryResolveWorktree(repo, worktree, out var wt)) return Results.BadRequest(Err("Unknown worktree."));
        return await action(repo, wt);
    }

    /// <summary>Phase 1 (no/invalid token) returns a confirmation token; phase 2 (valid token) executes.</summary>
    private static async Task<IResult> Guarded(ConfirmationService confirm, string? token, string signature,
        string summary, Func<Task<IResult>> execute)
    {
        if (confirm.TryConsume(token, signature)) return await execute();
        return Results.Ok(new ConfirmationRequired(true, confirm.Issue(signature), summary));
    }

    private static async Task<IResult> Run(Task<GitResult> op, SnapshotService snap, string repoId)
    {
        var r = await op;
        await snap.RefreshRepoAsync(repoId);
        return Results.Ok(new OperationResult(r.Success, r.StdOut, r.Success ? null : r.FailureMessage));
    }

    private static bool TryResolveWorktree(Repository repo, string path, out Worktree worktree)
    {
        var norm = PathId.Normalize(path);
        worktree = repo.Worktrees.FirstOrDefault(w => PathId.Normalize(w.Path) == norm)!;
        return worktree is not null;
    }

    private static object Err(string message) => new { error = message };
}
