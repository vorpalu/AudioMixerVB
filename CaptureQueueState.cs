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

    private readonly double baseTargetMs;
    private double peakPacketMs;
    private long lastDecayTicks = Environment.TickCount64;

    public CaptureQueueState(double baseTargetMs)
    {
        this.baseTargetMs = baseTargetMs;
    }

    public double EffectiveTargetMs => Math.Max(baseTargetMs, peakPacketMs + PacketMarginMs);

    public void OnPacket(double packetMs)
    {
        var now = Environment.TickCount64;
        var elapsedSeconds = (now - lastDecayTicks) / 1000.0;
        if (elapsedSeconds > 0)
        {
            peakPacketMs *= Math.Pow(PeakDecayPerSecond, elapsedSeconds);
            lastDecayTicks = now;
        }

        if (packetMs > peakPacketMs)
        {
            peakPacketMs = packetMs;
        }
    }
}
