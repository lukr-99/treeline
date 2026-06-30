using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Treeline.App.Api;
using Treeline.App.Tray;
using Treeline.Core.Git;
using Treeline.Core.Services;
using Treeline.Core.Storage;

namespace Treeline.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var options = LaunchOptions.Parse(args);

        // Single instance: if Treeline is already running, just open the UI and exit.
        using var mutex = new Mutex(initiallyOwned: true, "Treeline.Local.SingleInstance", out var isNew);
        if (!isNew)
        {
            BrowserLauncher.Open(RuntimeInfo.ReadUrl() ?? $"http://127.0.0.1:{options.Port}");
            return;
        }

        var app = BuildApp(options);
        app.StartAsync().GetAwaiter().GetResult();
        RuntimeInfo.Write(options.Port);

        try
        {
            if (options.Headless)
            {
                // Server-only mode for agents / unattended hosts.
                app.WaitForShutdownAsync().GetAwaiter().GetResult();
            }
            else
            {
                System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.SystemAware);
                System.Windows.Forms.Application.EnableVisualStyles();
                System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                using var tray = new TrayApplicationContext(options.Port);
                System.Windows.Forms.Application.Run(tray);
            }
        }
        finally
        {
            app.StopAsync().GetAwaiter().GetResult();
            RuntimeInfo.Delete();
        }
    }

    private static WebApplication BuildApp(LaunchOptions options)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = options.RawArgs,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        // Core services (single shared instances).
        builder.Services.AddSingleton(new GitRunner());
        builder.Services.AddSingleton<IGitService, GitService>();
        builder.Services.AddSingleton<ISourceStore>(_ => new SourceStore());
        builder.Services.AddSingleton<IConfigStore>(_ => new ConfigStore());
        builder.Services.AddSingleton<RepositoryScanner>();
        builder.Services.AddSingleton<FileSystemBrowser>();
        builder.Services.AddSingleton<SourceManager>();
        builder.Services.AddSingleton<ConfirmationService>();
        builder.Services.AddSingleton<SnapshotService>();
        builder.Services.AddHostedService<RefreshHostedService>();

        var app = builder.Build();
        app.Urls.Add($"http://127.0.0.1:{options.Port}");
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapTreelineApi();
        return app;
    }
}

/// <summary>Parsed command-line options.</summary>
internal sealed record LaunchOptions(int Port, bool Headless, string[] RawArgs)
{
    public static LaunchOptions Parse(string[] args)
    {
        int port = ResolveDefaultPort();
        bool headless = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--port" when i + 1 < args.Length && int.TryParse(args[i + 1], out var p):
                    port = p; i++; break;
                case "--headless" or "--no-tray" or "--server":
                    headless = true; break;
            }
        }
        return new LaunchOptions(port, headless, args);
    }

    private static int ResolveDefaultPort()
    {
        try { return int.TryParse(new ConfigStore().GetOrDefault("port", "8787"), out var p) ? p : 8787; }
        catch { return 8787; }
    }
}
