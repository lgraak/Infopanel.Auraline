using Auraline.Host.Waveform;

namespace Auraline.Host.Tests;

public sealed class WaveformProcessorTests
{
    [Fact]
    public void ProcessFramePreservesChannelGeometry()
    {
        var processor = new WaveformProcessor();
        var frame = new WaveformBinaryFrame(123ul, 4ul, 0ul, 2u, 2, [1f, 2f, -1f, -2f]);

        var processed = processor.ProcessFrame(frame, "stream");

        Assert.Equal(2, processed.MonoSamples.Length);
        Assert.Equal(new[] { 1f, -1f }, processed.ChannelSamples[0]);
        Assert.Equal(new[] { 2f, -2f }, processed.ChannelSamples[1]);
        Assert.True(processed.ChannelSamples[0].Length == 2);
        Assert.True(processed.ChannelSamples[1].Length == 2);
    }

    [Fact]
    public void ProcessFrameCombinesChannelsAndAvoidsOverflow()
    {
        var processor = new WaveformProcessor();
        var frame = new WaveformBinaryFrame(1ul, 2ul, 0ul, 1u, 1, [10_000f]);

        var processed = processor.ProcessFrame(frame, "stream");
        Assert.Single(processed.MonoSamples);
        Assert.Equal(1f, Math.Clamp(processed.MonoSamples[0], -1f, 1f));
    }

    [Fact]
    public void ProcessFrameResetClearsHistory()
    {
        var processor = new WaveformProcessor();
        var frameOne = new WaveformBinaryFrame(1ul, 0ul, 0ul, 1u, 1, [1f]);
        _ = processor.ProcessFrame(frameOne, "stream");
        Assert.True(processor.CurrentMaxMagnitude > 0);

        processor.Reset();
        Assert.Equal(0, processor.CurrentMaxMagnitude);

        var frameTwo = new WaveformBinaryFrame(2ul, 0ul, 0ul, 1u, 1, [0.5f]);
        var processed = processor.ProcessFrame(frameTwo, "stream");
        Assert.True(Math.Abs(processed.MonoSamples[0]) > 0);
        Assert.True(processed.MonoSamples.Length == 1);
    }
}
