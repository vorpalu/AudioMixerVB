namespace AudioMixerVB;

public sealed class StreamMixSettings
{
    public bool EnabledOnStartup { get; set; }

    public string? OutputEndpointId { get; set; }

    public string? OutputEndpointFriendlyName { get; set; }

    public float MasterGain { get; set; } = 1.0f;

    public bool MasterMuted { get; set; }

    public Dictionary<string, float> ChannelGains { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, bool> ChannelMutes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int LatencyMs { get; set; } = 20;
}
