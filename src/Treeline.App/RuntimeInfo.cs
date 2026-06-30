using System.Diagnostics;
using System.Text.Json;
using Treeline.Core.Storage;

namespace Treeline.App;

/// <summary>
/// Publishes a small discovery file (endpoint.json) so agents and the tray can find the
/// running instance's URL without guessing the port.
/// </summary>
internal static class RuntimeInfo
{
    private static string File => Path.Combine(TreelinePaths.DataDirectory, "endpoint.json");

    public static void Write(int port)
    {
        var info = new
        {
            url = $"http://127.0.0.1:{port}",
            port,
            pid = Environment.ProcessId,
            version = typeof(RuntimeInfo).Assembly.GetName().Version?.ToString(),
            startedAt = DateTimeOffset.UtcNow,
        };
        try { System.IO.File.WriteAllText(File, JsonSerializer.Serialize(info)); } catch { /* best effort */ }
    }

    public static string? ReadUrl()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return null;
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(File));
            return doc.RootElement.GetProperty("url").GetString();
        }
        catch { return null; }
    }

    public static void Delete()
    {
        try { if (System.IO.File.Exists(File)) System.IO.File.Delete(File); } catch { /* ignore */ }
    }
}

internal static class BrowserLauncher
{
    public static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }
}
