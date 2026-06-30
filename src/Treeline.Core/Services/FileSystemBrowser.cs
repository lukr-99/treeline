namespace Treeline.Core.Services;

public sealed record FsEntry(string Name, string Path, bool IsRepo);

/// <summary>A directory listing for the folder picker. At the root, <see cref="Entries"/> are drives.</summary>
public sealed record FsListing(string? Path, string? Parent, bool IsRoot, IReadOnlyList<FsEntry> Entries);

/// <summary>Read-only filesystem navigation for the folder picker. Lists directories only.</summary>
public sealed class FileSystemBrowser
{
    /// <summary>Lists drives when <paramref name="path"/> is empty, otherwise the subdirectories of it.</summary>
    public FsListing Browse(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new FsEntry(d.Name.TrimEnd('\\', '/'), d.RootDirectory.FullName, false))
                .ToList();
            return new FsListing(null, null, true, drives);
        }

        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);

        var entries = new List<FsEntry>();
        foreach (var dir in SafeEnumerate(full))
        {
            if (!IsListable(dir)) continue;
            var name = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(name)) name = dir;
            entries.Add(new FsEntry(name, dir, IsRepo(dir)));
        }
        entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        var parent = Directory.GetParent(full)?.FullName;
        return new FsListing(full, parent, false, entries);
    }

    private static IEnumerable<string> SafeEnumerate(string dir)
    {
        try { return Directory.EnumerateDirectories(dir); }
        catch (UnauthorizedAccessException) { return []; }
        catch (IOException) { return []; }
    }

    private static bool IsListable(string dir)
    {
        try
        {
            var attr = File.GetAttributes(dir);
            if (attr.HasFlag(FileAttributes.Hidden) || attr.HasFlag(FileAttributes.System)) return false;
            return true;
        }
        catch { return false; }
    }

    private static bool IsRepo(string dir)
    {
        try
        {
            var git = Path.Combine(dir, ".git");
            return Directory.Exists(git) || File.Exists(git);
        }
        catch { return false; }
    }
}
