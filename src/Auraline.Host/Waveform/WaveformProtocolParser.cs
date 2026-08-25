using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Auraline.Host.Waveform;

public sealed class WaveformProtocolException(string message) : Exception(message);

public static class WaveformProtocolParser
{
    public const int BinaryHeaderLength = 40;
    public const int MaxBinaryPayloadBytes = 1_048_576;

    public static WaveformStreamStartedEvent ParseStreamStarted(string json)
    {
        var root = ParseJsonObject(json);
        EnsureType(root, "stream_started");
        EnsureProtocol(root);
        var channelCount = GetInt32(root, "channels");
        if (channelCount is < 1 or > 8) throw new WaveformProtocolException($"Unsupported channel count {channelCount}.");
        var channelOrder = GetStringArray(root, "channel_order");
        if (channelOrder.Length != channelCount)
            throw new WaveformProtocolException($"Stream channel count {channelCount} did not match channel order count {channelOrder.Length}.");

        var sampleFormat = GetString(root, "sample_format").ToLowerInvariant();
        if (sampleFormat != "f32-le")
            throw new WaveformProtocolException($"Unsupported sample format '{sampleFormat}'.");

        return new(
            GetString(root, "stream_id"),
            GetString(root, "source_id"),
            GetString(root, "source_kind"),
            GetInt32(root, "sample_rate_hz"),
            channelCount,
            channelOrder,
            sampleFormat,
            GetInt64(root, "window_duration_ns"));
    }

    public static WaveformStreamStoppedEvent ParseStreamStopped(string json)
    {
        var root = ParseJsonObject(json);
        EnsureType(root, "stream_stopped");
        EnsureProtocol(root);
        return new(
            GetString(root, "stream_id"),
            GetString(root, "reason"));
    }

    public static WaveformStreamErrorEvent ParseStreamError(string json)
    {
        var root = ParseJsonObject(json);
        EnsureType(root, "stream_error");
        EnsureProtocol(root);
        var retry = ParseRetryHint(GetNullableString(root, "retry"));
        var scopeElement = GetObjectOrDefault(root, "scope");
        string scopeType = "unknown";
        string? scopeId = null;
        if (scopeElement is not null)
        {
            scopeType = GetString(scopeElement.Value, "type");
            scopeId = GetNullableString(scopeElement.Value, "id");
        }
        return new(
            GetString(root, "kind"),
            scopeType,
            scopeId,
            retry);
    }

    public static WaveformBinaryFrame ParseWaveformBinary(ReadOnlySpan<byte> payload, int expectedChannels = 0)
    {
        if (payload.Length > MaxBinaryPayloadBytes) throw new WaveformProtocolException("Waveform binary frame exceeded size limit.");
        if (payload.Length < BinaryHeaderLength) throw new WaveformProtocolException("Waveform binary frame was too short.");
        if (!payload.Slice(0, 4).SequenceEqual("RSWF"u8)) throw new WaveformProtocolException("Waveform binary header magic was invalid.");

        var version = payload[4];
        if (version != 1) throw new WaveformProtocolException($"Unsupported waveform binary version {version}.");
        if (payload[5] != BinaryHeaderLength) throw new WaveformProtocolException("Waveform binary header length was unexpected.");

        var flags = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2));
        if (flags != 0) throw new WaveformProtocolException("Unsupported waveform binary flags.");

        var sequence = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(8, 8));
        var frameIndex = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(16, 8));
        var streamTimeNs = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(24, 8));
        var frameCount = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(32, 4));
        var channelCount = (int)BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(36, 2));
        var reserved = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(38, 2));
        if (reserved != 0) throw new WaveformProtocolException("Waveform binary reserved bytes were invalid.");

        if (channelCount < 1 || channelCount > 8) throw new WaveformProtocolException($"Unsupported channel count {channelCount}.");
        if (expectedChannels != 0 && channelCount != expectedChannels)
            throw new WaveformProtocolException($"Waveform channel count {channelCount} did not match stream metadata {expectedChannels}.");

        var expectedLength = checked((int)BinaryHeaderLength + (long)frameCount * channelCount * sizeof(float));
        if (expectedLength != payload.Length) throw new WaveformProtocolException("Waveform binary frame length did not match channel data.");

        var sampleCount = (int)frameCount * channelCount;
        var samples = new float[sampleCount];
        var sampleOffset = BinaryHeaderLength;
        for (int i = 0; i < sampleCount; i++)
        {
            var raw = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(sampleOffset + i * sizeof(float), sizeof(float)));
            var value = BitConverter.Int32BitsToSingle(raw);
            if (!float.IsFinite(value))
                throw new WaveformProtocolException("Waveform sample value was not finite.");
            samples[i] = value;
        }

        return new(sequence, frameIndex, streamTimeNs, frameCount, channelCount, samples);
    }

    private static JsonElement ParseJsonObject(string json)
    {
        try
        {
            var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new WaveformProtocolException("Waveform JSON event must be an object.");
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new WaveformProtocolException("Waveform JSON event was invalid.") { Source = ex.Source };
        }
    }

    private static void EnsureType(JsonElement root, string expectedType)
    {
        var type = GetString(root, "type");
        if (!string.Equals(type, expectedType, StringComparison.Ordinal))
            throw new WaveformProtocolException($"Expected waveform event '{expectedType}' but received '{type}'.");
    }

    private static void EnsureProtocol(JsonElement root)
    {
        var protocol = GetInt32(root, "protocol_version");
        if (protocol != 1) throw new WaveformProtocolException($"Unsupported protocol version {protocol}.");
    }

    private static JsonElement? GetObjectOrDefault(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element)) return null;
        if (element.ValueKind != JsonValueKind.Object) return null;
        return element;
    }

    private static string[] GetStringArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var arrayElement) || arrayElement.ValueKind != JsonValueKind.Array)
            throw new WaveformProtocolException($"Waveform event was missing required array '{name}'.");
        return arrayElement.EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();
    }

    private static string GetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new WaveformProtocolException($"Waveform event was missing required string '{name}'.");
        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
            throw new WaveformProtocolException($"Waveform event string '{name}' was empty.");
        return text;
    }

    private static string? GetNullableString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new WaveformProtocolException($"Waveform event had invalid string '{name}'.");
        return value.GetString();
    }

    private static int GetInt32(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            throw new WaveformProtocolException($"Waveform event was missing required integer '{name}'.");
        try { return value.GetInt32(); }
        catch (FormatException ex) { throw new WaveformProtocolException($"Waveform event integer '{name}' was invalid: {ex.Message}"); }
    }

    private static long GetInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            throw new WaveformProtocolException($"Waveform event was missing required integer '{name}'.");
        try { return value.GetInt64(); }
        catch (FormatException ex) { throw new WaveformProtocolException($"Waveform event integer '{name}' was invalid: {ex.Message}"); }
    }

    private static WaveformRetryHint ParseRetryHint(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "retry_now" => WaveformRetryHint.RetryNow,
            "retry_later" => WaveformRetryHint.RetryLater,
            "wait_for_source" => WaveformRetryHint.WaitForSource,
            "request_permission" => WaveformRetryHint.RequestPermission,
            "change_format" => WaveformRetryHint.ChangeFormat,
            "do_not_retry" => WaveformRetryHint.DoNotRetry,
            null or "null" => WaveformRetryHint.Unknown,
            _ => WaveformRetryHint.Unknown
        };
    }
}
