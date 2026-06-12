using NAudio.Dsp;
using NAudio.Wave;

namespace AudioMixerVB;

/// <summary>
/// Resamples a capture stream to the mix rate while servo-adjusting the
/// effective input rate so the capture queue stays at its target depth.
/// Clock drift between a virtual cable and the physical output device is
/// corrected continuously and inaudibly (the correction is capped at ±0.4%,
/// about 7 cents of pitch) instead of by audibly skipping samples.
/// </summary>
public sealed class DriftCompensatingSampleProvider : ISampleProvider
{
    private const double MaxRateCorrection = 0.004;
    private const double CorrectionPerMsError = 0.0002;
    private const double BacklogSmoothing = 0.05;

    private readonly ISampleProvider source;
    private readonly WdlResampler resampler;
    private readonly Func<double> backlogMs;
    private readonly double targetBacklogMs;
    private readonly int channels;
    private double smoothedBacklogMs = double.NaN;

    public DriftCompensatingSampleProvider(
        ISampleProvider source,
        int outputSampleRate,
        Func<double> backlogMs,
        double targetBacklogMs)
    {
        this.source = source;
        this.backlogMs = backlogMs;
        this.targetBacklogMs = targetBacklogMs;
        channels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(outputSampleRate, channels);

        resampler = new WdlResampler();
        resampler.SetMode(true, 2, false);
        resampler.SetFilterParms();
        resampler.SetFeedMode(false);
        resampler.SetRates(source.WaveFormat.SampleRate, outputSampleRate);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var framesRequested = count / channels;
        if (framesRequested <= 0)
        {
            return 0;
        }

        // Smooth the measured queue depth: it oscillates by a capture packet
        // (~10 ms) between callbacks, and the servo should track the average,
        // not chase the ripple.
        var measured = backlogMs();
        smoothedBacklogMs = double.IsNaN(smoothedBacklogMs)
            ? measured
            : smoothedBacklogMs + BacklogSmoothing * (measured - smoothedBacklogMs);

        var correction = Math.Clamp(
            (smoothedBacklogMs - targetBacklogMs) * CorrectionPerMsError,
            -MaxRateCorrection,
            MaxRateCorrection);
        resampler.SetRates(source.WaveFormat.SampleRate * (1.0 + correction), WaveFormat.SampleRate);

        var framesNeeded = resampler.ResamplePrepare(framesRequested, channels, out var inBuffer, out var inBufferOffset);
        var framesRead = source.Read(inBuffer, inBufferOffset, framesNeeded * channels) / channels;
        var framesOut = resampler.ResampleOut(buffer, offset, framesRead, framesRequested, channels);
        if (framesOut < framesRequested)
        {
            Array.Clear(buffer, offset + framesOut * channels, (framesRequested - framesOut) * channels);
        }

        return framesRequested * channels;
    }
}
