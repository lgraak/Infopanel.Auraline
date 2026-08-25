namespace Auraline.Host.Waveform;

public sealed class WaveformProcessor
{
    private const double TargetPeak = 0.70;
    private const double SilenceThreshold = 0.003;
    private const double MinGain = 0.25;
    private const double MaxGain = 6.0;
    private const double GainAttack = 0.30;
    private const double GainDecay = 0.12;
    private const double SmoothAttack = 0.24;
    private const double SmoothDecay = 0.16;

    private readonly object _gate = new();
    private bool _hasSmoothed;
    private float[] _smoothed = [];
    private double _gain = 1.0;
    private double _maxMagnitude;

    public void Reset()
    {
        lock (_gate)
        {
            _hasSmoothed = false;
            _smoothed = [];
            _gain = 1.0;
            _maxMagnitude = 0;
        }
    }

    public WaveformProcessedFrame ProcessFrame(WaveformBinaryFrame frame, string streamId)
    {
        if (frame.ChannelCount <= 0) throw new ArgumentOutOfRangeException(nameof(frame.ChannelCount));
        if (frame.Samples.Length != frame.FrameCount * frame.ChannelCount)
            throw new ArgumentException("Frame payload does not match frame geometry.", nameof(frame));

        var frameCount = (int)frame.FrameCount;
        var mono = new float[frameCount];
        var channels = new float[frame.ChannelCount][];
        for (var c = 0; c < frame.ChannelCount; c++) channels[c] = new float[frameCount];

        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            double total = 0;
            for (var channel = 0; channel < frame.ChannelCount; channel++)
            {
                var sample = frame.Samples[frameIndex * frame.ChannelCount + channel];
                channels[channel][frameIndex] = sample;
                total += sample;
            }
            mono[frameIndex] = (float)(total / frame.ChannelCount);
        }

        var normalized = NormalizeAndClamp(mono);
        var smoothed = Smooth(normalized);
        var maxMagnitude = smoothed.Length == 0 ? 0.0 : smoothed.Max(Math.Abs);
        lock (_gate) _maxMagnitude = maxMagnitude;
        return new(streamId, frame.Sequence, frame.FrameIndex, frame.StreamTimeNs, smoothed, channels);
    }

    public double CurrentMaxMagnitude
    {
        get { lock (_gate) return _maxMagnitude; }
    }

    public int LastFrameSamples { get { lock (_gate) return _smoothed.Length; } }

    private float[] NormalizeAndClamp(float[] mono)
    {
        if (mono.Length == 0) return [];

        var peak = 0.0;
        foreach (var sample in mono) peak = Math.Max(peak, Math.Abs(sample));
        if (peak <= 0)
            return new float[mono.Length];

        var gain = _gain;
        var target = peak < SilenceThreshold ? 1.0 : Math.Clamp(TargetPeak / Math.Max(peak, SilenceThreshold), MinGain, MaxGain);
        var rate = peak < SilenceThreshold ? GainDecay : GainAttack;
        gain += (target - gain) * rate;
        if (!double.IsFinite(gain) || gain <= 0) gain = 1.0;
        gain = Math.Clamp(gain, MinGain, MaxGain);
        _gain = gain;

        var normalized = new float[mono.Length];
        for (var i = 0; i < mono.Length; i++)
        {
            var value = mono[i] * (float)gain;
            if (float.IsFinite(value))
                normalized[i] = Math.Clamp(value, -1f, 1f);
            else
                normalized[i] = 0f;
        }
        return normalized;
    }

    private float[] Smooth(float[] values)
    {
        if (_smoothed.Length != values.Length) _smoothed = new float[values.Length];
        if (values.Length == 0) return [];
        if (!_hasSmoothed)
        {
            for (var i = 0; i < values.Length; i++)
            {
                _smoothed[i] = values[i];
            }
            _hasSmoothed = true;
            return _smoothed;
        }

        var output = new float[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var target = values[i];
            var current = _smoothed[i];
            var factor = Math.Abs(target) > Math.Abs(current) ? SmoothAttack : SmoothDecay;
            var smoothed = current + (target - current) * (float)factor;
            _smoothed[i] = smoothed;
            if (float.IsFinite(smoothed))
                output[i] = smoothed;
            else
                output[i] = 0f;
        }
        return output;
    }
}
