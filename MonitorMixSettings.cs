namespace AudioMixerVB;

public sealed class MonitorMixSettings
{
    public bool EnabledOnStartup { get; set; }

    public string? OutputEndpointId { get; set; }

    public string? OutputEndpointFriendlyName { get; set; }

    public string? GameCaptureEndpointId { get; set; }

    public string? GameCaptureEndpointFriendlyName { get; set; }

    public string? ChatCaptureEndpointId { get; set; }

    public string? ChatCaptureEndpointFriendlyName { get; set; }

    public string? MusicCaptureEndpointId { get; set; }

    public string? MusicCaptureEndpointFriendlyName { get; set; }

    public string? MediaCaptureEndpointId { get; set; }

    public string? MediaCaptureEndpointFriendlyName { get; set; }

    public Dictionary<string, float> ChannelGains { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, bool> ChannelMutes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public float MasterGain { get; set; } = 1.0f;

    public bool MasterMuted { get; set; }

    public int LatencyMs { get; set; } = 20;

    public string ChannelSliderMode { get; set; } = "Monitor Mix Gain";

    public string? GetCaptureEndpointId(string channelName)
        => channelName.ToUpperInvariant() switch
        {
            "GAME" => GameCaptureEndpointId,
            "CHAT" => ChatCaptureEndpointId,
            "MUSIC" => MusicCaptureEndpointId,
            "MEDIA" => MediaCaptureEndpointId,
            _ => null
        };

    public string? GetCaptureEndpointFriendlyName(string channelName)
        => channelName.ToUpperInvariant() switch
        {
            "GAME" => GameCaptureEndpointFriendlyName,
            "CHAT" => ChatCaptureEndpointFriendlyName,
            "MUSIC" => MusicCaptureEndpointFriendlyName,
            "MEDIA" => MediaCaptureEndpointFriendlyName,
            _ => null
        };

    public void SetCaptureEndpoint(string channelName, AudioEndpoint? endpoint)
    {
        switch (channelName.ToUpperInvariant())
        {
            case "GAME":
                GameCaptureEndpointId = endpoint?.Id;
                GameCaptureEndpointFriendlyName = endpoint?.FriendlyName;
                break;
            case "CHAT":
                ChatCaptureEndpointId = endpoint?.Id;
                ChatCaptureEndpointFriendlyName = endpoint?.FriendlyName;
                break;
            case "MUSIC":
                MusicCaptureEndpointId = endpoint?.Id;
                MusicCaptureEndpointFriendlyName = endpoint?.FriendlyName;
                break;
            case "MEDIA":
                MediaCaptureEndpointId = endpoint?.Id;
                MediaCaptureEndpointFriendlyName = endpoint?.FriendlyName;
                break;
        }
    }
}
