namespace AudioMixerVB;

public sealed class AudioAppSession
{
    public int ProcessId { get; set; }

    public string ProcessName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string CurrentEndpointId { get; set; } = string.Empty;

    public string CurrentEndpointFriendlyName { get; set; } = string.Empty;

    public float Volume { get; set; }

    public int VolumePercent => Math.Clamp((int)Math.Round(Volume * 100.0f, MidpointRounding.AwayFromZero), 0, 100);

    public bool IsMuted { get; set; }

    public string AssignedChannel { get; set; } = "None";

    public string? TargetEndpointId { get; set; }

    public string TargetEndpointFriendlyName { get; set; } = string.Empty;

    public string Status { get; set; } = "Active";
}
