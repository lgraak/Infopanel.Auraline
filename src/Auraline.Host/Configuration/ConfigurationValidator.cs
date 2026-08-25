namespace Auraline.Host.Configuration;

public static class ConfigurationValidator
{
    private static readonly string[] Themes = ["system", "light", "dark"];

    public static IReadOnlyList<string> Validate(HostConfiguration configuration)
    {
        var errors = new List<string>();
        if (configuration.SchemaVersion != HostConfiguration.CurrentSchemaVersion)
            errors.Add($"Unsupported schema version {configuration.SchemaVersion}.");
        if (configuration.Host.Port is < 1024 or > 65535)
            errors.Add("Host port must be between 1024 and 65535.");
        if (!Themes.Contains(configuration.Host.Theme, StringComparer.OrdinalIgnoreCase))
            errors.Add("Theme must be system, light, or dark.");
        if (configuration.Providers.Count == 0)
            errors.Add("At least one provider must be configured.");

        var providerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in configuration.Providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Id)) errors.Add("Provider ID cannot be empty.");
            else if (!providerIds.Add(provider.Id)) errors.Add($"Duplicate provider ID '{provider.Id}'.");
            if (string.IsNullOrWhiteSpace(provider.FriendlyName)) errors.Add($"Provider '{provider.Id}' needs a friendly name.");
            if (!Uri.TryCreate(provider.Endpoint, UriKind.Absolute, out var endpoint) ||
                endpoint.Scheme != Uri.UriSchemeHttp || !System.Net.IPAddress.TryParse(endpoint.Host, out var address) ||
                !System.Net.IPAddress.IsLoopback(address) || endpoint.AbsolutePath != "/" ||
                !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
            {
                errors.Add($"Provider '{provider.Id}' endpoint must be an HTTP numeric loopback address.");
            }
            else if (endpoint.Port == configuration.Host.Port)
            {
                errors.Add($"Provider '{provider.Id}' endpoint conflicts with the Host port.");
            }
        }
        return errors;
    }
}
