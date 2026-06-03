namespace AudioMixerVB;

public sealed class MixerChannel
{
    public const int DefaultVolumePercent = 50;

    public static readonly string[] DefaultChannelNames = ["Game", "Chat", "Media", "Music"];

    public MixerChannel()
    {
    }

    public MixerChannel(string name)
    {
        Name = name;
        VolumePercent = DefaultVolumePercent;
    }

    public string Name { get; set; } = string.Empty;

    public string? SelectedEndpointId { get; set; }

    public string? SelectedEndpointName { get; set; }

    public int VolumePercent { get; set; } = DefaultVolumePercent;

    public bool IsMuted { get; set; }
}
