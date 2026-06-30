using System.Security.Cryptography;
using System.Text;

namespace Treeline.Core.Services;

/// <summary>Path normalization and stable id generation.</summary>
public static class PathId
{
    /// <summary>Full path, trimmed of trailing separators, lower-cased (Windows is case-insensitive).</summary>
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full;
    }

    public static string ForSource(string path) => "s-" + Hash(Normalize(path));
    public static string ForRepo(string commonDir) => "r-" + Hash(Normalize(commonDir));

    private static string Hash(string value)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }
}
