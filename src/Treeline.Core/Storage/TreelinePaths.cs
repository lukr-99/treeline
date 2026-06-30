namespace Treeline.Core.Storage;

/// <summary>Resolves on-disk locations for Treeline's local data.</summary>
public static class TreelinePaths
{
    /// <summary>%APPDATA%\Treeline (created on first access).</summary>
    public static string DataDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Treeline");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SourcesFile => Path.Combine(DataDirectory, "sources.json");
    public static string ConfigFile => Path.Combine(DataDirectory, "config.json");
}
