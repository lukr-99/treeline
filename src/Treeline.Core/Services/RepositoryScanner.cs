using Treeline.Core.Models;

namespace Treeline.Core.Services;

/// <summary>Discovers git repository directories under tracked sources.</summary>
public sealed class RepositoryScanner
{
    // Directories never worth descending into when scanning a folder source.
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", ".vs", ".idea", "dist", "build", "target",
        "packages", ".cache", "vendor", "__pycache__", ".venv", "venv"
    };

    /// <summary>Returns candidate repository directories (those containing a .git entry) for a source.</summary>
    public IReadOnlyList<string> Discover(TrackedSource source)
    {
        if (!Directory.Exists(source.Path)) return [];

        if (source.Type == SourceType.Repo)
            return IsRepoRoot(source.Path) ? [source.Path] : [];

        var found = new List<string>();
        Walk(source.Path, source.ScanDepth, found);
        return found;
    }

    private static void Walk(string dir, int depthRemaining, List<string> found)
    {
        if (IsRepoRoot(dir))
        {
            found.Add(dir);
            return; // do not descend into a repo (its worktrees are resolved via git)
        }
        if (depthRemaining <= 0) return;

        IEnumerable<string> subdirs;
        try
        {
            subdirs = Directory.EnumerateDirectories(dir);
        }
        catch (UnauthorizedAccessException) { return; }
        catch (IOException) { return; }

        foreach (var sub in subdirs)
        {
            var name = Path.GetFileName(sub);
            if (name.StartsWith('.') && name != ".git") continue;
            if (SkipDirs.Contains(name)) continue;
            Walk(sub, depthRemaining - 1, found);
        }
    }

    /// <summary>A directory is a repo root if it has a .git directory (normal) or .git file (worktree/submodule).</summary>
    private static bool IsRepoRoot(string dir)
    {
        var gitPath = Path.Combine(dir, ".git");
        return Directory.Exists(gitPath) || File.Exists(gitPath);
    }
}
