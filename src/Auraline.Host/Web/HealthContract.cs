using System.Text.Json.Serialization;
using Auraline.Host.Configuration;
using Auraline.Host.Providers;
using Auraline.Host.Waveform;

namespace Auraline.Host.Web;

public sealed record HealthContract(
    [property: JsonPropertyName("host_status")] string HostStatus,
    [property: JsonPropertyName("host_version")] string HostVersion,
    [property: JsonPropertyName("provider_summary")] ProviderSummaryContract ProviderSummary,
    [property: JsonPropertyName("providers")] IReadOnlyList<ProviderHealthContract> Providers,
    [property: JsonPropertyName("waveform")] WaveformEngineHealth? Waveform,
    [property: JsonPropertyName("configuration_error")] string? ConfigurationError);

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
    IWaveformEngineStatusProvider? waveformEngine = null)
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
        return new(configuration.LoadError is null ? "healthy" : "degraded", Version,
            new(statuses.Count, enabled, connected, unavailable),
            statuses.Select(p => new ProviderHealthContract(p.Id, p.FriendlyName, p.Enabled,
                p.State.ToString(), p.Sources.Count, p.LastError)).ToArray(), waveformEngine?.GetHealth(), configuration.LoadError);
    }
}
