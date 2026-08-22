using System.Diagnostics;
using System.Windows.Forms;
using DotNetLib.Core.Updating;
using Treeline.Core.Storage;

namespace Treeline.App.Tray;

/// <summary>
/// System-tray presence for Treeline. Keeps the local server alive and offers quick actions:
/// open the dashboard, refresh, open the data folder, and exit.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly int _port;
    private readonly HttpClient _http = new();
    private readonly UpdateService _updater;

    private string Url => $"http://127.0.0.1:{_port}";

    public TrayApplicationContext(int port)
    {
        _port = port;

        var version = typeof(TrayApplicationContext).Assembly.GetName().Version;
        _updater = new UpdateService(
            new GitHubReleaseSource(_http, "lukr-99", "Treeline",
                name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)),
            version == null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}", _http);

        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Open Treeline", null, (_, _) => OpenDashboard()) { Font = new System.Drawing.Font(SystemFonts.MenuFont!, System.Drawing.FontStyle.Bold) };
        menu.Items.Add(open);
        menu.Items.Add(new ToolStripMenuItem("Refresh all", null, (_, _) => RefreshAll()));
        menu.Items.Add(new ToolStripMenuItem("Open data folder", null, (_, _) => OpenDataFolder()));
        menu.Items.Add(new ToolStripMenuItem("Check for updates", null, (_, _) => CheckForUpdates()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Exit()));

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Treeline",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => OpenDashboard();
        _icon.BalloonTipTitle = "Treeline is running";
        _icon.BalloonTipText = $"Dashboard at {Url}";
        _icon.ShowBalloonTip(2500);
    }

    private void OpenDashboard() => BrowserLauncher.Open(Url);

    private void RefreshAll()
    {
        _ = Task.Run(async () =>
        {
            try { await _http.PostAsync($"{Url}/api/refresh", null); } catch { /* ignore */ }
        });
    }

    private void CheckForUpdates()
    {
        _ = Task.Run(async () =>
        {
            ReleaseInfo? release;
            try { release = await _updater.CheckForUpdateAsync(); }
            catch { release = null; }

            if (release is null)
            {
                _icon.ShowBalloonTip(2500, "Treeline", "You're up to date.", ToolTipIcon.Info);
                return;
            }

            var answer = MessageBox.Show(
                $"Treeline v{release.Version} is available. Download and install now?",
                "Treeline update", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            try
            {
                await _updater.DownloadAndLaunchAsync(release, "/SILENT /SUPPRESSMSGBOXES /NORESTART");
                _icon.Visible = false;
                Environment.Exit(0);   // quit (installer is detached) so it can replace files
            }
            catch
            {
                _icon.ShowBalloonTip(3000, "Treeline", "Update download failed — try again later.", ToolTipIcon.Warning);
            }
        });
    }

    private static void OpenDataFolder()
    {
        try { Process.Start(new ProcessStartInfo(TreelinePaths.DataDirectory) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }

    private void Exit()
    {
        _icon.Visible = false;
        ExitThread();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "treeline.ico");
        try { return File.Exists(path) ? new System.Drawing.Icon(path) : SystemIcons.Application; }
        catch { return SystemIcons.Application; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon.Dispose();
            _http.Dispose();
        }
        base.Dispose(disposing);
    }
}
