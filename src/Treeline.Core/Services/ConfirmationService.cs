using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Treeline.Core.Services;

/// <summary>
/// Server-side enforcement of the "double confirm" rule for destructive operations.
/// A caller must first request a token bound to the exact action signature, then resubmit
/// the operation with that token. Tokens are single-use and expire quickly.
/// </summary>
public sealed class ConfirmationService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(120);
    private readonly ConcurrentDictionary<string, (string Signature, DateTimeOffset Expires)> _tokens = new();

    /// <summary>Builds the canonical signature for an action so a token cannot be reused for a different one.</summary>
    public static string Signature(string operation, string repoId, string target) =>
        $"{operation}|{repoId}|{target}";

    public string Issue(string signature)
    {
        Sweep();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        _tokens[token] = (signature, DateTimeOffset.UtcNow.Add(Ttl));
        return token;
    }

    /// <summary>Validates and consumes a token; returns false if missing, expired, or bound to another action.</summary>
    public bool TryConsume(string? token, string signature)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        if (!_tokens.TryRemove(token, out var entry)) return false;
        return entry.Expires > DateTimeOffset.UtcNow && entry.Signature == signature;
    }

    private void Sweep()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in _tokens)
            if (kv.Value.Expires <= now) _tokens.TryRemove(kv.Key, out _);
    }
}
