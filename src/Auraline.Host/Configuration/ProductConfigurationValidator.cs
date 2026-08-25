using System.Globalization;
using System.Text.RegularExpressions;

namespace Auraline.Host.Configuration;

public static partial class ProductConfigurationValidator
{
    public static IReadOnlyList<string> ValidateGroup(SourceGroupDefinition group)
    {
        var errors = new List<string>();
        ValidateId(group.Id, "Source-group", errors);
        if (group.SchemaVersion != SourceGroupDefinition.CurrentSchemaVersion)
            errors.Add($"Unsupported source-group schema version {group.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(group.FriendlyName)) errors.Add("Source-group friendly name is required.");
        if (group.Members.Count == 0) errors.Add("A source group must contain at least one member.");
        foreach (var member in group.Members)
        {
            if (string.IsNullOrWhiteSpace(member.ProviderId)) errors.Add("Every source-group member needs a provider ID.");
            var hasSource = !string.IsNullOrWhiteSpace(member.SourceId);
            var hasIntent = !string.IsNullOrWhiteSpace(member.LogicalIntent);
            if (hasSource == hasIntent) errors.Add("Every source-group member must define exactly one source ID or logical intent.");
            if (hasIntent && !string.Equals(member.LogicalIntent, ProductDefaults.DefaultLogicalSourceIntent, StringComparison.Ordinal))
                errors.Add($"Unsupported logical source intent '{member.LogicalIntent}'.");
            if (!double.IsFinite(member.Gain) || member.Gain <= 0) errors.Add("Source-group member gain must be a positive finite number.");
        }
        return errors;
    }

    public static IReadOnlyList<string> ValidateProfile(ProfileDefinition profile, IReadOnlyCollection<string> groupIds)
    {
        var errors = new List<string>();
        ValidateId(profile.Id, "Profile", errors);
        if (profile.SchemaVersion != ProfileDefinition.CurrentSchemaVersion)
            errors.Add($"Unsupported profile schema version {profile.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(profile.FriendlyName)) errors.Add("Profile friendly name is required.");
        if (!string.Equals(profile.VisualizationType, "waveform", StringComparison.Ordinal))
            errors.Add($"Unsupported visualization type '{profile.VisualizationType}'.");
        if (!groupIds.Contains(profile.SourceGroupId, StringComparer.OrdinalIgnoreCase))
            errors.Add($"Source group '{profile.SourceGroupId}' does not exist.");
        if (!HexColor().IsMatch(profile.Waveform.Color)) errors.Add("Waveform color must use #RRGGBB.");
        if (!string.Equals(profile.Waveform.Style, "centered-line", StringComparison.Ordinal))
            errors.Add($"Unsupported waveform style '{profile.Waveform.Style}'.");
        if (!string.Equals(profile.Waveform.Background, "transparent", StringComparison.Ordinal))
            errors.Add("M5 supports only a transparent background.");
        if (!double.IsFinite(profile.Waveform.FixedScale) || profile.Waveform.FixedScale is < 0.05 or > 10)
            errors.Add("Fixed scale must be between 0.05 and 10.");
        if (!double.IsFinite(profile.Waveform.SmoothingAmount) || profile.Waveform.SmoothingAmount is < 0 or > 1)
            errors.Add("Smoothing amount must be between 0 and 1.");
        if (profile.Waveform.TargetFps is not (30 or 60)) errors.Add("Target FPS must be 30 or 60.");
        if (profile.Revision < 1) errors.Add("Profile revision must be positive.");
        return errors;
    }

    private static void ValidateId(string id, string label, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(id) || !StableId().IsMatch(id))
            errors.Add($"{label} ID must contain only lowercase letters, numbers, and hyphens.");
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$")]
    private static partial Regex StableId();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();
}
