using System.Buffers.Binary;

using Auraline.Host.Waveform;

namespace Auraline.Host.Tests;

public sealed class WaveformProtocolParserTests
{
    [Fact]
    public void ParseStreamStartedAcceptsValidEvent()
    {
        var json = @"{""type"":""stream_started"",""protocol_version"":1,""stream_id"":""stream-1"",""source_id"":""source-1"",""source_kind"":""playback"",""channels"":2,""channel_order"":[""left"",""right""],""sample_format"":""f32-le"",""sample_rate_hz"":48000,""window_duration_ns"":33333333}";

        var started = WaveformProtocolParser.ParseStreamStarted(json);

        Assert.Equal("stream-1", started.StreamId);
        Assert.Equal("source-1", started.SourceId);
        Assert.Equal("playback", started.SourceKind);
        Assert.Equal(2, started.ChannelCount);
        Assert.Equal("left", started.ChannelOrder[0]);
        Assert.Equal("right", started.ChannelOrder[1]);
        Assert.Equal("f32-le", started.SampleFormat);
        Assert.Equal(48000, started.SampleRateHz);
    }

    [Fact]
    public void ParseStreamStartedRejectsUnsupportedSampleFormat()
    {
        var json = @"{""type"":""stream_started"",""protocol_version"":1,""stream_id"":""stream-1"",""source_id"":""source-1"",""source_kind"":""playback"",""channels"":2,""channel_order"":[""left"",""right""],""sample_format"":""f64-le"",""sample_rate_hz"":48000,""window_duration_ns"":33333333}";
        Assert.Throws<WaveformProtocolException>(() => WaveformProtocolParser.ParseStreamStarted(json));
    }

    [Fact]
    public void ParseStreamStoppedReadsReason()
    {
        var json = @"{""type"":""stream_stopped"",""protocol_version"":1,""stream_id"":""stream-1"",""reason"":""source_lost""}";
        var stopped = WaveformProtocolParser.ParseStreamStopped(json);
        Assert.Equal("stream-1", stopped.StreamId);
        Assert.Equal("source_lost", stopped.Reason);
    }

    [Fact]
    public void ParseWaveformBinaryAcceptsValidPayload()
    {
        var payload = BuildBinaryFrame(frameCount: 2, channelCount: 2, samples: [0.1f, -0.1f, 0.2f, -0.2f]);
        var frame = WaveformProtocolParser.ParseWaveformBinary(payload, expectedChannels: 2);

        Assert.Equal(0ul, frame.Sequence);
        Assert.Equal(0ul, frame.FrameIndex);
        Assert.Equal(2u, frame.FrameCount);
        Assert.Equal(2, frame.ChannelCount);
        Assert.Equal(4, frame.Samples.Length);
    }

    [Fact]
    public void ParseWaveformBinaryRejectsTruncatedPayload()
    {
        var payload = BuildBinaryFrame(frameCount: 1, channelCount: 1, samples: [0.1f]);
        payload = payload[..(payload.Length - 5)];
        Assert.Throws<WaveformProtocolException>(() => WaveformProtocolParser.ParseWaveformBinary(payload, expectedChannels: 1));
    }

    [Fact]
    public void ParseWaveformBinaryRejectsInvalidHeaderMagic()
    {
        var payload = BuildBinaryFrame(frameCount: 1, channelCount: 1, samples: [0.1f]);
        payload[0] = (byte)'X';
        Assert.Throws<WaveformProtocolException>(() => WaveformProtocolParser.ParseWaveformBinary(payload, expectedChannels: 1));
    }

    [Fact]
    public void ParseWaveformBinaryRejectsChannelMismatch()
    {
        var payload = BuildBinaryFrame(frameCount: 1, channelCount: 2, samples: [0.1f, 0.2f]);
        Assert.Throws<WaveformProtocolException>(() => WaveformProtocolParser.ParseWaveformBinary(payload, expectedChannels: 1));
    }

    private static byte[] BuildBinaryFrame(int frameCount, int channelCount, float[] samples)
    {
        if (samples.Length != frameCount * channelCount) throw new ArgumentException("sample size mismatch", nameof(samples));

        var payload = new byte[WaveformProtocolParser.BinaryHeaderLength + samples.Length * sizeof(float)];
        payload[0] = (byte)'R';
        payload[1] = (byte)'S';
        payload[2] = (byte)'W';
        payload[3] = (byte)'F';
        payload[4] = 1;
        payload[5] = (byte)WaveformProtocolParser.BinaryHeaderLength;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6, 2), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(8, 8), 0ul);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(16, 8), 0ul);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(24, 8), 0ul);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(32, 4), (uint)frameCount);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(36, 2), (ushort)channelCount);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(38, 2), 0);

        var sampleOffset = WaveformProtocolParser.BinaryHeaderLength;
        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(sampleOffset + (i * sizeof(float)), 4), BitConverter.SingleToInt32Bits(samples[i]));
        }

        return payload;
    }
}
