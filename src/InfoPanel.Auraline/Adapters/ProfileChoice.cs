using Auraline.Contracts;

namespace InfoPanel.Auraline.Adapters;

internal static class ProfileChoice
{
    public static string Format(AuralineProfileSummary profile) =>
        $"{profile.FriendlyName} [{profile.ProfileId}]";

    public static string ParseProfileId(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        var open = text.LastIndexOf('[');
        if (open >= 0 && text.EndsWith(']'))
        {
            var id = text[(open + 1)..^1].Trim();
            if (!string.IsNullOrWhiteSpace(id)) return id;
        }
        if (!string.IsNullOrWhiteSpace(text)) return text;
        return AuralineProfiles.DefaultProfileId;
    }
}
