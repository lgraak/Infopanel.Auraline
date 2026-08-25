using Auraline.Contracts;
using Auraline.Host.Configuration;
using Auraline.Host.Lifecycle;
using Auraline.Host.Waveform;
using Auraline.Host.Platform;
using Auraline.Host.Platform.Windows;
using Auraline.Host.Providers;
using Auraline.Host.RenderSessions;
using Auraline.Host.Web;
using Serilog;
using Serilog.Events;
using System.Globalization;
using System.Text.Json.Serialization;

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
            var products = new ProductConfigurationStore(paths);
            var productLoad = products.LoadAsync().GetAwaiter().GetResult();
            if (productLoad.Error is not null) Log.Error("{ConfigurationError}", productLoad.Error);

            var builder = WebApplication.CreateBuilder(args);
            builder.Host.UseSerilog();
            builder.WebHost.UseUrls($"http://127.0.0.1:{configuration.Current.Host.Port}");
            builder.Services.AddSingleton(paths);
            builder.Services.AddSingleton(configuration);
            builder.Services.AddSingleton(products);
            builder.Services.AddSingleton<IProfileCatalog>(products);
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
            builder.Services.AddSingleton<IWaveformRenderStateSource>(services => services.GetRequiredService<WaveformEngineService>());
            builder.Services.AddHostedService(services => services.GetRequiredService<WaveformEngineService>());
            builder.Services.AddSingleton<IAuralineFrameTransportFactory, WindowsSharedMemoryFrameTransportFactory>();
            builder.Services.AddSingleton<IRenderSessionClock, SystemRenderSessionClock>();
            builder.Services.AddSingleton(RenderSessionOptions.Default);
            builder.Services.AddSingleton<RenderSessionManager>();
            builder.Services.AddHostedService(services => services.GetRequiredService<RenderSessionManager>());
            builder.Services.AddSingleton<IStartupRegistration, WindowsStartupRegistration>();
            builder.Services.AddSingleton<StartupRegistrationState>();
            builder.Services.AddSingleton<HostStatusService>();
            builder.Services.AddSingleton<IBrowserLauncher, BrowserLauncher>();
            builder.Services.ConfigureHttpJsonOptions(options =>
                options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.SnakeCaseLower)));

            app = builder.Build();
            app.Use(async (context, next) =>
            {
                if (IsStateChanging(context.Request.Method) && !LoopbackRequestGuard.IsAllowed(context.Request, configuration.Current.Host.Port))
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
        app.MapConfigurationEndpoints();
        app.MapRenderSessionEndpoints();
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
        app.MapGet("/providers", (ProviderManager providers, ProductConfigurationStore products, ConfigurationStore config) =>
            Results.Content(UiRenderer.Providers(providers.GetStatuses(), products, config.Current.Host.Theme), "text/html"));
        app.MapGet("/sources", (ProviderManager providers, ProductConfigurationStore products, ConfigurationStore config) =>
            Results.Content(UiRenderer.Sources(providers.GetStatuses(), products, config.Current.Host.Theme), "text/html"));
        app.MapGet("/source-groups", (ProductConfigurationStore products, ProviderManager providers, ConfigurationStore config) =>
            Results.Content(UiRenderer.SourceGroups(products, providers.GetStatuses(), config.Current.Host.Theme), "text/html"));
        app.MapGet("/source-groups/{groupId}/edit", (string groupId, ProductConfigurationStore products, ProviderManager providers, ConfigurationStore config) =>
            Html(() => UiRenderer.SourceGroupEditor(products.GetGroup(groupId), products, providers.GetStatuses(), config.Current.Host.Theme), config.Current.Host.Theme, "/source-groups"));
        app.MapGet("/profiles", (ProductConfigurationStore products, RenderSessionManager sessions, ProviderManager providers, ConfigurationStore config) =>
            Results.Content(UiRenderer.Profiles(products, sessions, providers.GetStatuses(), config.Current.Host.Theme), "text/html"));
        app.MapGet("/profiles/{profileId}/edit", (string profileId, ProductConfigurationStore products, ConfigurationStore config) =>
            Html(() => UiRenderer.ProfileEditor(products.GetProfile(profileId), products.GetGroups(), config.Current.Host.Theme), config.Current.Host.Theme, "/profiles"));
        app.MapGet("/diagnostics", (HostStatusService status, ProductConfigurationStore products, ConfigurationStore config) => Results.Content(UiRenderer.Diagnostics(status.GetHealth(), products, config.Current.Host.Theme), "text/html"));

        app.MapPost("/providers", async (HttpRequest request, ProviderManager providers, ConfigurationStore config) =>
        {
            try
            {
                var form = await request.ReadFormAsync();
                await providers.AddAsync(new ProviderConfiguration
                {
                    Id = form["id"].ToString().Trim(),
                    FriendlyName = form["friendlyName"].ToString().Trim(),
                    Endpoint = form["endpoint"].ToString().Trim(),
                    Enabled = form.ContainsKey("enabled")
                });
                return Results.Redirect("/providers");
            }
            catch (Exception ex) { return UiError("Provider was not added", ex, "/providers", config.Current.Host.Theme); }
        });
        app.MapPost("/providers/{providerId}/save", async (string providerId, HttpRequest request, ProviderManager providers, ConfigurationStore config) =>
        {
            try
            {
                var form = await request.ReadFormAsync();
                await providers.UpdateAsync(providerId, new ProviderConfiguration
                {
                    Id = providerId,
                    FriendlyName = form["friendlyName"].ToString().Trim(),
                    Endpoint = form["endpoint"].ToString().Trim(),
                    Enabled = form.ContainsKey("enabled")
                });
                return Results.Redirect("/providers");
            }
            catch (Exception ex) { return UiError("Provider was not saved", ex, "/providers", config.Current.Host.Theme); }
        });
        app.MapPost("/providers/{providerId}/delete", async (string providerId, ProviderManager providers, ConfigurationStore config) =>
        {
            try { await providers.DeleteAsync(providerId); return Results.Redirect("/providers"); }
            catch (Exception ex) { return UiError("Provider was not deleted", ex, "/providers", config.Current.Host.Theme); }
        });

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

        app.MapPost("/source-groups", async (HttpRequest request, ProductConfigurationStore products, ProviderManager providers, ConfigurationStore config) =>
        {
            try
            {
                var form = await request.ReadFormAsync();
                var members = ParseMembers(form["members"], products, providers.GetStatuses());
                await products.CreateGroupAsync(form["friendlyName"].ToString(), members);
                return Results.Redirect("/source-groups");
            }
            catch (Exception ex) { return UiError("Source group was not created", ex, "/source-groups", config.Current.Host.Theme); }
        });
        app.MapPost("/source-groups/{groupId}/save", async (string groupId, HttpRequest request, ProductConfigurationStore products, ProviderManager providers, ConfigurationStore config) =>
        {
            try
            {
                var form = await request.ReadFormAsync();
                var current = products.GetGroup(groupId);
                var selected = ParseMembers(form["members"], products, providers.GetStatuses());
                await products.SaveGroupAsync(current with
                {
                    FriendlyName = form["friendlyName"].ToString().Trim(),
                    Members = selected.Count == 0 ? current.Members : [.. selected]
                });
                return Results.Redirect("/source-groups");
            }
            catch (Exception ex) { return UiError("Source group was not saved", ex, $"/source-groups/{Uri.EscapeDataString(groupId)}/edit", config.Current.Host.Theme); }
        });
        app.MapPost("/source-groups/{groupId}/duplicate", async (string groupId, ProductConfigurationStore products, ConfigurationStore config) =>
        {
            try { await products.DuplicateGroupAsync(groupId); return Results.Redirect("/source-groups"); }
            catch (Exception ex) { return UiError("Source group was not duplicated", ex, "/source-groups", config.Current.Host.Theme); }
        });
        app.MapPost("/source-groups/{groupId}/set-default", async (string groupId, ProductConfigurationStore products, ConfigurationStore config) =>
        {
            try { await products.SetDefaultGroupAsync(groupId); return Results.Redirect("/source-groups"); }
            catch (Exception ex) { return UiError("Default source group was not changed", ex, "/source-groups", config.Current.Host.Theme); }
        });
        app.MapPost("/source-groups/{groupId}/delete", async (string groupId, ProductConfigurationStore products, ConfigurationStore config) =>
        {
            try { await products.DeleteGroupAsync(groupId); return Results.Redirect("/source-groups"); }
            catch (Exception ex) { return UiError("Source group was not deleted", ex, "/source-groups", config.Current.Host.Theme); }
        });

        app.MapPost("/profiles", async (HttpRequest request, ProductConfigurationStore products, ConfigurationStore config) =>
        {
            try
            {
                var form = await request.ReadFormAsync();
                var profile = await products.CreateProfileAsync(form["friendlyName"].ToString(), form["sourceGroupId"].ToString());
                return Results.Redirect($"/profiles/{Uri.EscapeDataString(profile.Id)}/edit");
            }
            catch (Exception ex) { return UiError("Profile was not created", ex, "/profiles", config.Current.Host.Theme); }
        });
        app.MapPost("/profiles/{profileId}/save", async (string profileId, HttpRequest request, ProductConfigurationStore products, ConfigurationStore config) =>
        {
            try
            {
                var form = await request.ReadFormAsync();
                var current = products.GetProfile(profileId);
                var scaleMode = Enum.TryParse<WaveformScaleMode>(form["scaleMode"], true, out var parsedScale) ? parsedScale : current.Waveform.ScaleMode;
                var fixedScale = double.TryParse(form["fixedScale"], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedFixed) ? parsedFixed : current.Waveform.FixedScale;
                var smoothingAmount = double.TryParse(form["smoothingAmount"], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedSmoothing) ? parsedSmoothing : current.Waveform.SmoothingAmount;
                var fps = int.TryParse(form["targetFps"], out var parsedFps) ? parsedFps : current.Waveform.TargetFps;
                await products.SaveProfileAsync(current with
                {
                    FriendlyName = form["friendlyName"].ToString().Trim(),
                    SourceGroupId = form["sourceGroupId"].ToString(),
                    Waveform = current.Waveform with
                    {
                        Color = form["color"].ToString(),
                        ScaleMode = scaleMode,
                        FixedScale = fixedScale,
                        SmoothingEnabled = form.ContainsKey("smoothingEnabled"),
                        SmoothingAmount = smoothingAmount,
                        TargetFps = fps
                    }
                });
                return Results.Redirect("/profiles");
            }
            catch (Exception ex) { return UiError("Profile was not saved", ex, $"/profiles/{Uri.EscapeDataString(profileId)}/edit", config.Current.Host.Theme); }
        });
        app.MapPost("/profiles/{profileId}/duplicate", async (string profileId, ProductConfigurationStore products, ConfigurationStore config) =>
        {
            try { var copy = await products.DuplicateProfileAsync(profileId); return Results.Redirect($"/profiles/{Uri.EscapeDataString(copy.Id)}/edit"); }
            catch (Exception ex) { return UiError("Profile was not duplicated", ex, "/profiles", config.Current.Host.Theme); }
        });
        app.MapPost("/profiles/{profileId}/set-default", async (string profileId, ProductConfigurationStore products, ConfigurationStore config) =>
        {
            try { await products.SetDefaultProfileAsync(profileId); return Results.Redirect("/profiles"); }
            catch (Exception ex) { return UiError("Default profile was not changed", ex, "/profiles", config.Current.Host.Theme); }
        });
        app.MapPost("/profiles/{profileId}/delete", async (string profileId, ProductConfigurationStore products, RenderSessionManager sessions, ConfigurationStore config) =>
        {
            try { await products.DeleteProfileAsync(profileId, sessions.IsProfileInUse); return Results.Redirect("/profiles"); }
            catch (Exception ex) { return UiError("Profile was not deleted", ex, "/profiles", config.Current.Host.Theme); }
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

    private static bool IsStateChanging(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    private static IResult Html(Func<string> render, string theme, string returnPath)
    {
        try { return Results.Content(render(), "text/html"); }
        catch (Exception ex) { return UiError("Configuration could not be displayed", ex, returnPath, theme); }
    }

    private static IResult UiError(string title, Exception exception, string returnPath, string theme) =>
        Results.Content(UiRenderer.ErrorPage(title, exception.Message, returnPath, theme), "text/html", statusCode: exception is KeyNotFoundException ? 404 : 400);

    private static IReadOnlyList<SourceReference> ParseMembers(
        Microsoft.Extensions.Primitives.StringValues values,
        ProductConfigurationStore products,
        IReadOnlyList<ProviderStatus> providers)
    {
        var members = new List<SourceReference>();
        foreach (var value in values)
        {
            var parts = (value ?? string.Empty).Split('|', 3, StringSplitOptions.TrimEntries);
            if (parts.Length != 3) throw new InvalidDataException("Source selection was invalid.");
            var providerId = parts[1];
            if (parts[0] == "intent")
            {
                members.Add(new SourceReference { ProviderId = providerId, LogicalIntent = parts[2] });
                continue;
            }
            if (parts[0] != "source") throw new InvalidDataException("Source selection was invalid.");
            var current = providers.SelectMany(item => item.Sources).FirstOrDefault(item =>
                item.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase) && item.SourceId.Equals(parts[2], StringComparison.Ordinal));
            var lastKnown = products.SourceCatalog.Sources.FirstOrDefault(item =>
                item.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase) && item.SourceId.Equals(parts[2], StringComparison.Ordinal));
            members.Add(new SourceReference
            {
                ProviderId = providerId,
                SourceId = parts[2],
                LastKnownDisplayName = current?.DisplayName ?? lastKnown?.DisplayName,
                LastKnownKind = current?.Kind ?? lastKnown?.Kind
            });
        }
        return members;
    }
}
