using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Auraline.Contracts;
using Auraline.Host.Platform.Windows;

var options = ProbeOptions.Parse(args);
using var client = new HttpClient { BaseAddress = options.BaseUri, Timeout = TimeSpan.FromSeconds(10) };
var attachResponse = await client.PostAsJsonAsync("/api/v1/render-sessions/attach", new
{
    contract_major = ContractVersion.Current.Major,
    contract_minor = ContractVersion.Current.Minor,
    profile_id = options.ProfileId,
    width = options.Width,
    height = options.Height,
    target_fps = options.TargetFps
});
var attachJson = await attachResponse.Content.ReadAsStringAsync();
if (!attachResponse.IsSuccessStatusCode)
    throw new InvalidOperationException($"Attach failed ({(int)attachResponse.StatusCode}): {attachJson}");

var attachment = JsonSerializer.Deserialize<RenderSessionAttachment>(attachJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
    ?? throw new InvalidDataException("Attach response did not contain a render-session attachment.");
if (!ContractVersion.Current.IsCompatibleWith(attachment.Session.ContractVersion))
    throw new NotSupportedException($"Unsupported session contract {attachment.Session.ContractVersion}.");

using var reader = new WindowsSharedMemoryFrameTransportFactory().Open(attachment.Session.Transport);
var stopwatch = Stopwatch.StartNew();
var deadline = DateTimeOffset.UtcNow + options.Duration;
var nextHeartbeat = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(8);
ulong firstSequence = 0;
ulong lastSequence = 0;
byte[]? firstPixels = null;
var contentChanged = false;
var completeReads = 0;

try
{
    while (DateTimeOffset.UtcNow < deadline)
    {
        if (reader.TryReadLatest(out var frame) && frame is not null)
        {
            if (frame.Width != options.Width || frame.Height != options.Height ||
                frame.Stride != checked(options.Width * 4) || frame.Pixels.Length != checked(frame.Stride * frame.Height) ||
                frame.PixelFormat != "rgba8888-premul" || !frame.Premultiplied)
                throw new InvalidDataException("Consumed frame geometry or pixel layout did not match the negotiated session.");
            if (lastSequence != 0 && frame.Sequence < lastSequence)
                throw new InvalidDataException("Consumed frame sequence moved backwards.");
            firstSequence = firstSequence == 0 ? frame.Sequence : firstSequence;
            lastSequence = frame.Sequence;
            firstPixels ??= frame.Pixels;
            contentChanged |= !firstPixels.AsSpan().SequenceEqual(frame.Pixels);
            completeReads++;
        }

        if (DateTimeOffset.UtcNow >= nextHeartbeat)
        {
            var heartbeat = await client.PostAsync(
                $"/api/v1/render-sessions/{attachment.Session.SessionId}/leases/{attachment.Lease.LeaseId}/heartbeat",
                null);
            heartbeat.EnsureSuccessStatusCode();
            nextHeartbeat = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(8);
        }
        await Task.Delay(5);
    }
}
finally
{
    if (!options.Abrupt)
    {
        var detach = await client.DeleteAsync(
            $"/api/v1/render-sessions/{attachment.Session.SessionId}/leases/{attachment.Lease.LeaseId}");
        detach.EnsureSuccessStatusCode();
    }
}

stopwatch.Stop();
if (completeReads == 0 || firstSequence == 0 || lastSequence <= firstSequence)
    throw new InvalidOperationException("Probe did not observe advancing complete frames.");
if (!contentChanged)
    throw new InvalidOperationException("Probe did not observe pixel-content changes.");

var observedFps = (lastSequence - firstSequence) / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
Console.WriteLine(JsonSerializer.Serialize(new
{
    session_id = attachment.Session.SessionId,
    lease_id = attachment.Lease.LeaseId,
    width = options.Width,
    height = options.Height,
    target_fps = options.TargetFps,
    first_sequence = firstSequence,
    last_sequence = lastSequence,
    complete_reads = completeReads,
    content_changed = contentChanged,
    observed_fps = Math.Round(observedFps, 2),
    detached = !options.Abrupt
}));

internal sealed record ProbeOptions(Uri BaseUri, string ProfileId, int Width, int Height, int TargetFps, TimeSpan Duration, bool Abrupt)
{
    public static ProbeOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal)) continue;
            var key = args[index][2..];
            var value = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++index]
                : "true";
            values[key] = value;
        }

        var baseUri = new Uri(values.GetValueOrDefault("base-url", "http://127.0.0.1:48481"));
        if (!baseUri.IsLoopback) throw new ArgumentException("Probe base URL must be loopback.");
        var profileId = values.GetValueOrDefault("profile-id", AuralineProfiles.DefaultProfileId);
        var width = int.Parse(values.GetValueOrDefault("width", "320"));
        var height = int.Parse(values.GetValueOrDefault("height", "120"));
        var fps = int.Parse(values.GetValueOrDefault("fps", "30"));
        var seconds = double.Parse(values.GetValueOrDefault("seconds", "4"), System.Globalization.CultureInfo.InvariantCulture);
        return new ProbeOptions(baseUri, profileId, width, height, fps, TimeSpan.FromSeconds(seconds), values.ContainsKey("abrupt"));
    }
}
