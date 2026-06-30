using System.Diagnostics;
using System.Text;

namespace Treeline.Core.Git;

/// <summary>Result of a single git invocation.</summary>
public sealed record GitResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
    public string FailureMessage => string.IsNullOrWhiteSpace(StdErr) ? StdOut : StdErr;
}

/// <summary>
/// Thin, safe wrapper around the <c>git</c> executable. Arguments are passed as a
/// list (never string-concatenated) so paths and branch names cannot break quoting.
/// </summary>
public sealed class GitRunner
{
    private readonly string _gitPath;

    public GitRunner(string gitPath = "git") => _gitPath = gitPath;

    public async Task<GitResult> RunAsync(
        string workingDirectory,
        IEnumerable<string> args,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _gitPath,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // Keep git non-interactive: never pop a credential prompt and never page.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_PAGER"] = "cat";
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.pager=cat");
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        if (!process.Start())
            return new GitResult(-1, "", "Failed to start git process.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw;
        }

        return new GitResult(process.ExitCode, stdout.ToString().TrimEnd(), stderr.ToString().TrimEnd());
    }

    /// <summary>Returns true if the git executable is reachable.</summary>
    public async Task<string?> TryGetVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await RunAsync(Environment.CurrentDirectory, ["--version"], ct).ConfigureAwait(false);
            return r.Success ? r.StdOut.Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
