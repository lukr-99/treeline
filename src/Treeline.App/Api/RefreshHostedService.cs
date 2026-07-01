using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Treeline.Core.Services;
using Treeline.Core.Storage;

namespace Treeline.App.Api;

/// <summary>Periodically rebuilds the snapshot so the UI and agents see fresh git state.</summary>
public sealed class RefreshHostedService : BackgroundService
{
    private readonly SnapshotService _snapshot;
    private readonly IConfigStore _config;
    private readonly ILogger<RefreshHostedService> _log;

    public RefreshHostedService(SnapshotService snapshot, IConfigStore config, ILogger<RefreshHostedService> log)
    {
        _snapshot = snapshot;
        _config = config;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var seconds = ResolveIntervalSeconds();
            // Only spend git cycles while a client is actually watching. When idle, poll cheaply
            // (no git) so we react quickly once the dashboard is opened again.
            if (_snapshot.ClientActiveWithin(TimeSpan.FromSeconds(seconds * 3)))
            {
                await SafeRefresh(stoppingToken);
                if (await DelayAsync(seconds, stoppingToken)) break;
            }
            else
            {
                if (await DelayAsync(2, stoppingToken)) break;
            }
        }
    }

    private static async Task<bool> DelayAsync(int seconds, CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(seconds), ct); return false; }
        catch (OperationCanceledException) { return true; }
    }

    private async Task SafeRefresh(CancellationToken ct)
    {
        try { await _snapshot.RefreshAllAsync(ct); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.LogWarning(ex, "Background refresh failed."); }
    }

    private int ResolveIntervalSeconds()
    {
        var raw = _config.GetOrDefault("refreshIntervalSeconds", "10");
        return int.TryParse(raw, out var s) ? Math.Clamp(s, 3, 600) : 10;
    }
}
