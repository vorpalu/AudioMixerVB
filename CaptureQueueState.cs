namespace AudioMixerVB;

/// <summary>
/// Tracks how a capture endpoint actually delivers data so the queue target
/// can adapt: a cable that pushes large bursts needs a proportionally larger
/// cushion than one that delivers steady 10 ms packets. The peak decays so a
/// one-off burst does not pin the channel latency up forever.
/// Fields are doubles written by the capture thread and read by the playback
/// thread; the project is x64-only, so aligned double access is atomic.
/// </summary>
public sealed class CaptureQueueState
{
    private const double PeakDecayPerSecond = 0.98;
    private const double PacketMarginMs = 5;
    private const double GapMarginMs = 10;
    private const double MaxTrackedGapMs = 150;

    private readonly double baseTargetMs;
    private double peakPacketMs;
    private double peakGapMs;
    private double peakReadMs;
    private long lastPacketTicks;
    private long lastDecayTicks = Environment.TickCount64;

    public CaptureQueueState(double baseTargetMs)
    {
        this.baseTargetMs = baseTargetMs;
    }

    // The queue level sawtooths: it rises by one capture packet and is drained
    // in output-read gulps. The servo pins the AVERAGE at the target, so the
    // minimum dips below it by roughly (packet + gulp); the cushion must cover
    // both or the beat between the two cadences starves playback every few
    // seconds even though the average looks healthy.
    public double EffectiveTargetMs => Math.Max(
        baseTargetMs,
        Math.Max(peakPacketMs + peakReadMs + PacketMarginMs, peakGapMs + GapMarginMs));

    public void OnRead(double readMs)
    {
        if (readMs > peakReadMs)
        {
            peakReadMs = readMs;
        }
    }

    public void OnPacket(double packetMs)
    {
        var now = Environment.TickCount64;
        var elapsedSeconds = (now - lastDecayTicks) / 1000.0;
        if (elapsedSeconds > 0)
        {
            var decay = Math.Pow(PeakDecayPerSecond, elapsedSeconds);
            peakPacketMs *= decay;
            peakGapMs *= decay;
            lastDecayTicks = now;
        }

        if (packetMs > peakPacketMs)
        {
            peakPacketMs = packetMs;
        }

        // Pauses between packets are delivery jitter the cushion must outlive.
        // Anything longer is the stream going idle, which the dry-prefill
        // already covers - buffering against it would only add latency.
        if (lastPacketTicks != 0)
        {
            double gapMs = now - lastPacketTicks;
            if (gapMs <= MaxTrackedGapMs && gapMs > peakGapMs)
            {
                peakGapMs = gapMs;
            }
        }

        lastPacketTicks = now;
    }
}
