using System.Text.Json.Serialization;
using Auraline.Host.Configuration;
using Auraline.Host.Providers;
using Auraline.Host.RenderSessions;
using Auraline.Host.Waveform;

namespace Auraline.Host.Web;

public sealed record HealthContract(
    [property: JsonPropertyName("host_status")] string HostStatus,
    [property: JsonPropertyName("host_version")] string HostVersion,
    [property: JsonPropertyName("provider_summary")] ProviderSummaryContract ProviderSummary,
    [property: JsonPropertyName("providers")] IReadOnlyList<ProviderHealthContract> Providers,
    [property: JsonPropertyName("waveform")] WaveformEngineHealth? Waveform,
    [property: JsonPropertyName("render_sessions")] RenderSessionDiagnostics? RenderSessions,
    [property: JsonPropertyName("configuration_error")] string? ConfigurationError,
    [property: JsonPropertyName("product_configuration")] ProductConfigurationHealthContract? ProductConfiguration = null);

public sealed record ProductConfigurationHealthContract(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("profile_count")] int ProfileCount,
    [property: JsonPropertyName("source_group_count")] int SourceGroupCount,
    [property: JsonPropertyName("last_known_source_count")] int LastKnownSourceCount,
    [property: JsonPropertyName("source_catalog_refreshed_at_utc")] DateTimeOffset? SourceCatalogRefreshedAtUtc,
    [property: JsonPropertyName("default_profile_id")] string DefaultProfileId,
    [property: JsonPropertyName("default_source_group_id")] string DefaultSourceGroupId,
    [property: JsonPropertyName("validation_failure_count")] long ValidationFailureCount,
    [property: JsonPropertyName("save_failure_count")] long SaveFailureCount,
    [property: JsonPropertyName("load_error")] string? LoadError);

public sealed record ProviderSummaryContract(
    [property: JsonPropertyName("configured")] int Configured,
    [property: JsonPropertyName("enabled")] int Enabled,
    [property: JsonPropertyName("connected")] int Connected,
    [property: JsonPropertyName("unavailable")] int Unavailable);

public sealed record ProviderHealthContract(
    [property: JsonPropertyName("provider_id")] string ProviderId,
    [property: JsonPropertyName("friendly_name")] string FriendlyName,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("source_count")] int SourceCount,
    [property: JsonPropertyName("last_error")] string? LastError);

public sealed class HostStatusService(
    ConfigurationStore configuration,
    ProviderManager providers,
    IWaveformEngineStatusProvider? waveformEngine = null,
    RenderSessionManager? renderSessions = null,
    ProductConfigurationStore? productConfiguration = null)
{
    public static string Version
    {
        get
        {
            var informational = typeof(HostStatusService).Assembly.GetCustomAttributes(false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
            return informational?.Split('+', 2)[0]
                   ?? typeof(HostStatusService).Assembly.GetName().Version?.ToString() ?? "unknown";
        }
    }

    public HealthContract GetHealth()
    {
        var statuses = providers.GetStatuses();
        var enabled = statuses.Count(p => p.Enabled);
        var connected = statuses.Count(p => p.State == ProviderLifecycleState.Connected);
        var unavailable = statuses.Count(p => p.Enabled && p.State != ProviderLifecycleState.Connected);
        var productHealth = productConfiguration is null ? null : new ProductConfigurationHealthContract(
            ProductCatalogDocument.CurrentSchemaVersion,
            productConfiguration.GetProfiles().Count,
            productConfiguration.GetGroups().Count,
            productConfiguration.SourceCatalog.Sources.Count,
            productConfiguration.SourceCatalog.RefreshedAtUtc,
            productConfiguration.Catalog.DefaultProfileId,
            productConfiguration.Catalog.DefaultSourceGroupId,
            productConfiguration.ValidationFailureCount,
            productConfiguration.SaveFailureCount,
            productConfiguration.LoadError);
        var configurationError = configuration.LoadError ?? productConfiguration?.LoadError;
        return new(configurationError is null ? "healthy" : "degraded", Version,
            new(statuses.Count, enabled, connected, unavailable),
            statuses.Select(p => new ProviderHealthContract(p.Id, p.FriendlyName, p.Enabled,
                p.State.ToString(), p.Sources.Count, p.LastError)).ToArray(), waveformEngine?.GetHealth(), renderSessions?.GetDiagnostics(), configurationError, productHealth);
    }
}
