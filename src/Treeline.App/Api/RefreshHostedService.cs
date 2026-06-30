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
        // Initial build before the first tick so the UI has data immediately.
        await SafeRefresh(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var seconds = ResolveIntervalSeconds();
            try { await Task.Delay(TimeSpan.FromSeconds(seconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
            await SafeRefresh(stoppingToken);
        }
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
