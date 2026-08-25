using Auraline.Host.Configuration;
using Auraline.Host.Lifecycle;
using Auraline.Host.Waveform;
using Auraline.Host.Platform;
using Auraline.Host.Platform.Windows;
using Auraline.Host.Providers;
using Auraline.Host.Web;
using Serilog;
using Serilog.Events;

namespace Auraline.Host;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        ISingleInstanceCoordinator instance = new SingleInstanceCoordinator("Auraline.Host");
        if (!instance.IsPrimary)
        {
            instance.SignalOpenAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            instance.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return 0;
        }

        IPlatformPaths platformPaths = new WindowsPlatformPaths();
        var paths = platformPaths.GetPaths();
        paths.EnsureDirectories();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(Path.Combine(paths.LogsDirectory, "auraline-.log"), rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10 * 1024 * 1024, rollOnFileSizeLimit: true, retainedFileCountLimit: 7,
                shared: false, flushToDiskInterval: TimeSpan.FromSeconds(2))
            .CreateLogger();

        WebApplication? app = null;
        try
        {
            Log.Information("Auraline Host starting");
            var configuration = new ConfigurationStore(paths);
            var load = configuration.LoadAsync().GetAwaiter().GetResult();
            if (load.Error is not null) Log.Error("{ConfigurationError}", load.Error);

            var builder = WebApplication.CreateBuilder(args);
            builder.Host.UseSerilog();
            builder.WebHost.UseUrls($"http://127.0.0.1:{configuration.Current.Host.Port}");
            builder.Services.AddSingleton(paths);
            builder.Services.AddSingleton(configuration);
            builder.Services.AddSingleton<IAsyncDelay, SystemAsyncDelay>();
            builder.Services.AddSingleton<IProviderConnector, ResonanceSignalClient>();
            builder.Services.AddHttpClient("resonance-signal", client => client.Timeout = TimeSpan.FromSeconds(5));
            builder.Services.AddSingleton<ProviderManager>();
            builder.Services.AddHostedService(services => services.GetRequiredService<ProviderManager>());
            builder.Services.AddSingleton<WaveformProcessor>();
            builder.Services.AddSingleton<WaveformRenderer>();
            builder.Services.AddSingleton<WaveformReconnectPolicy>();
            builder.Services.AddSingleton<WaveformEngineService>();
            builder.Services.AddSingleton<IWaveformEngineStatusProvider>(services => services.GetRequiredService<WaveformEngineService>());
            builder.Services.AddHostedService(services => services.GetRequiredService<WaveformEngineService>());
            builder.Services.AddSingleton<IStartupRegistration, WindowsStartupRegistration>();
            builder.Services.AddSingleton<StartupRegistrationState>();
            builder.Services.AddSingleton<HostStatusService>();
            builder.Services.AddSingleton<IBrowserLauncher, BrowserLauncher>();

            app = builder.Build();
            app.Use(async (context, next) =>
            {
                if (HttpMethods.IsPost(context.Request.Method) && !LoopbackRequestGuard.IsAllowed(context.Request, configuration.Current.Host.Port))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                await next();
            });
            MapEndpoints(app);
            app.StartAsync().GetAwaiter().GetResult();

            var webUi = new Uri($"http://127.0.0.1:{configuration.Current.Host.Port}/");
            var browser = app.Services.GetRequiredService<IBrowserLauncher>();
            var providers = app.Services.GetRequiredService<ProviderManager>();
            var startup = app.Services.GetRequiredService<IStartupRegistration>();
            var startupState = app.Services.GetRequiredService<StartupRegistrationState>();
            startupState.LastResult = configuration.CanPersist
                ? startup.Apply(configuration.Current.Host.StartWithWindows, Environment.ProcessPath ?? Application.ExecutablePath)
                : new StartupRegistrationResult(false, "Startup registration was not changed because the configuration could not be loaded.");
            if (!startupState.LastResult.Succeeded) Log.Warning("Windows startup registration failed: {Reason}", startupState.LastResult.Error);

            instance.StartListening(() => browser.Open(webUi));
            if (!configuration.Current.Host.FirstRunCompleted)
            {
                if (browser.Open(webUi) && configuration.CanPersist)
                {
                    configuration.UpdateAsync(current => current with { Host = current.Host with { FirstRunCompleted = true } })
                        .GetAwaiter().GetResult();
                }
            }

            ApplicationConfiguration.Initialize();
            using var tray = new TrayApplicationContext(webUi, browser, providers);
            Application.Run(tray);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Auraline Host terminated unexpectedly");
            MessageBox.Show($"Auraline Host could not start.\n\n{ex.Message}", "Auraline Host", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            if (app is not null)
            {
                try { app.StopAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); }
                catch (Exception ex) { Log.Error(ex, "Auraline Host shutdown encountered an error"); }
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            instance.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Log.Information("Auraline Host stopped");
            Log.CloseAndFlush();
        }
    }

    public static void MapEndpoints(WebApplication app)
    {
        app.MapGet("/health", (HostStatusService status) => Results.Json(status.GetHealth()));
        app.MapGet("/waveform/preview.png", (HttpResponse response, IWaveformEngineStatusProvider waveform, WaveformRenderer renderer) =>
        {
            response.Headers.CacheControl = "no-store";
            var frame = waveform.GetLatestFrame();
            return frame is null
                ? Results.NotFound()
                : Results.Bytes(renderer.EncodePng(frame), "image/png");
        });
        app.MapGet("/", (HostStatusService status, ConfigurationStore config, StartupRegistrationState startup) =>
            Results.Content(UiRenderer.Dashboard(status.GetHealth(), config.Current, startup.LastResult), "text/html"));
        app.MapGet("/providers", (ProviderManager providers, ConfigurationStore config) =>
            Results.Content(UiRenderer.Providers(providers.GetStatuses(), config.Current.Host.Theme), "text/html"));
        app.MapGet("/sources", (ProviderManager providers, ConfigurationStore config) =>
            Results.Content(UiRenderer.Sources(providers.GetStatuses(), config.Current.Host.Theme), "text/html"));
        app.MapGet("/source-groups", (ConfigurationStore config) => Results.Content(UiRenderer.Placeholder("Source Groups", "M5", config.Current.Host.Theme), "text/html"));
        app.MapGet("/profiles", (ConfigurationStore config) => Results.Content(UiRenderer.Placeholder("Profiles", "M5", config.Current.Host.Theme), "text/html"));
        app.MapGet("/diagnostics", (HostStatusService status, ConfigurationStore config) => Results.Content(UiRenderer.Diagnostics(status.GetHealth(), config.Current.Host.Theme), "text/html"));

        app.MapPost("/providers/{providerId}/toggle", async (string providerId, HttpRequest request, ProviderManager providers) =>
        {
            var form = await request.ReadFormAsync();
            await providers.SetEnabledAsync(providerId, bool.TryParse(form["enabled"], out var enabled) && enabled);
            return Results.Redirect("/providers");
        });
        app.MapPost("/providers/{providerId}/reconnect", async (string providerId, ProviderManager providers) =>
        {
            await providers.ReconnectAsync(providerId);
            return Results.Redirect("/providers");
        });
        app.MapPost("/providers/{providerId}/refresh", async (string providerId, ProviderManager providers) =>
        {
            try { await providers.RefreshSourcesAsync(providerId); } catch { }
            return Results.Redirect("/providers");
        });
        app.MapPost("/settings", async (HttpRequest request, ConfigurationStore config, IStartupRegistration registration, StartupRegistrationState state) =>
        {
            var form = await request.ReadFormAsync();
            var startWithWindows = form.ContainsKey("startWithWindows");
            var theme = form["theme"].ToString().ToLowerInvariant();
            if (theme is not ("system" or "light" or "dark")) theme = config.Current.Host.Theme;
            state.LastResult = registration.Apply(startWithWindows, Environment.ProcessPath ?? Application.ExecutablePath);
            if (state.LastResult.Succeeded)
                await config.UpdateAsync(current => current with { Host = current.Host with { StartWithWindows = startWithWindows, Theme = theme } });
            return Results.Redirect("/");
        });
    }
}
