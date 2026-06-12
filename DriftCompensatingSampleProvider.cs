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
    private const double MaxRateCorrection = 0.01;
    private const double CorrectionPerMsError = 0.0002;
    private const double BacklogSmoothing = 0.05;
    private const int StatsLogIntervalMs = 30000;

    private readonly ISampleProvider source;
    private readonly WdlResampler resampler;
    private readonly Func<double> backlogMs;
    private readonly Func<double> targetBacklogMs;
    private readonly string name;
    private readonly Action<string>? log;
    private readonly int channels;
    private double smoothedBacklogMs = double.NaN;
    private long lastStatsLogTicks = Environment.TickCount64;

    public DriftCompensatingSampleProvider(
        ISampleProvider source,
        int outputSampleRate,
        Func<double> backlogMs,
        Func<double> targetBacklogMs,
        string name,
        Action<string>? log)
    {
        this.source = source;
        this.backlogMs = backlogMs;
        this.targetBacklogMs = targetBacklogMs;
        this.name = name;
        this.log = log;
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
            (smoothedBacklogMs - targetBacklogMs()) * CorrectionPerMsError,
            -MaxRateCorrection,
            MaxRateCorrection);
        resampler.SetRates(source.WaveFormat.SampleRate * (1.0 + correction), WaveFormat.SampleRate);

        // A correction pinned at the cap means the source clock is outside the
        // servo's reach and the queue will dry out or grow - surface that.
        var now = Environment.TickCount64;
        if (log is not null && now - lastStatsLogTicks >= StatsLogIntervalMs)
        {
            lastStatsLogTicks = now;
            log($"{name} servo: backlog {smoothedBacklogMs:F0} ms, target {targetBacklogMs():F0} ms, rate correction {correction * 100:+0.00;-0.00}%");
        }

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
