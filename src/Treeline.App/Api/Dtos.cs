using Treeline.Core.Models;

namespace Treeline.App.Api;

// Request payloads
public sealed record AddSourceRequest(string Path, SourceType Type, string? DisplayName, int? ScanDepth);
public sealed record UpdateSourceRequest(string? DisplayName, int? ScanDepth);
public sealed record PullRequest(string Worktree);
public sealed record CheckoutRequest(string Worktree, string Branch);
public sealed record CreateBranchRequest(string Name, string? From);
public sealed record AddWorktreeRequest(string Path, string? Branch, bool CreateBranch);

// Destructive request payloads carry an optional confirmation token (two-phase).
public sealed record RemoveWorktreeRequest(string Worktree, bool Force, string? ConfirmToken);
public sealed record DeleteBranchRequest(string Name, bool Force, string? ConfirmToken);
public sealed record DiscardRequest(string Worktree, string? ConfirmToken);

// Responses
public sealed record OperationResult(bool Ok, string? Output, string? Error);
public sealed record ConfirmationRequired(bool RequiresConfirmation, string ConfirmToken, string Summary);
