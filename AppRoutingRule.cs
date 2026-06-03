namespace AudioMixerVB;

public sealed class AppRoutingRule
{
    public AppRoutingRule()
    {
    }

    public AppRoutingRule(string processName, string channelName)
    {
        ProcessName = processName;
        PreferredChannel = channelName;
        Enabled = true;
    }

    public string ProcessName { get; set; } = string.Empty;

    public string PreferredChannel { get; set; } = string.Empty;

    public string? PreferredEndpointId { get; set; }

    public string? PreferredEndpointFriendlyName { get; set; }

    public bool Enabled { get; set; } = true;

    public string? ChannelName
    {
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                PreferredChannel = value;
            }
        }
    }
}
