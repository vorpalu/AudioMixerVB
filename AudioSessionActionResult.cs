namespace AudioMixerVB;

public sealed class AudioSessionActionResult
{
    public string ProcessName { get; set; } = string.Empty;

    public int ProcessId { get; set; }

    public string EndpointFriendlyName { get; set; } = string.Empty;

    public string ChannelName { get; set; } = string.Empty;

    public int VolumePercent { get; set; }

    public bool? IsMuted { get; set; }

    public string Status { get; set; } = "Applied";

    public string? ErrorMessage { get; set; }
}
