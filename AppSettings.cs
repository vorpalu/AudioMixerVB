namespace AudioMixerVB;

public sealed class AppSettings
{
    public List<MixerChannel> Channels { get; set; } = CreateDefaultChannels();

    public List<AppRoutingRule> RoutingRules { get; set; } = CreateDefaultRoutingRules();

    public bool RoutingRulesInitialized { get; set; } = true;

    public string ChannelVolumeMode { get; set; } = "ApplicationSessions";

    public MonitorMixSettings MonitorMix { get; set; } = new();

    public StreamMixSettings StreamMix { get; set; } = new();

    public bool EnableExperimentalAutomaticRouting { get; set; }

    public bool AutoApplyRoutingRules { get; set; }

    public string? SelectedComPort { get; set; }

    public int SerialBaudRate { get; set; } = 115200;

    public void EnsureDefaults()
    {
        Channels ??= [];

        var current = Channels
            .Where(channel => !string.IsNullOrWhiteSpace(channel.Name))
            .GroupBy(channel => channel.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        Channels = MixerChannel.DefaultChannelNames
            .Select(name => current.TryGetValue(name, out var channel) ? NormalizeChannel(channel, name) : new MixerChannel(name))
            .ToList();

        RoutingRules ??= [];
        if (RoutingRules.Count == 0 && !RoutingRulesInitialized)
        {
            RoutingRules = CreateDefaultRoutingRules();
            RoutingRulesInitialized = true;
        }
        else
        {
            RoutingRules = RoutingRules
                .Where(rule => !string.IsNullOrWhiteSpace(rule.ProcessName) && IsKnownChannel(rule.PreferredChannel))
                .GroupBy(rule => rule.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(group => NormalizeRoutingRule(group.First()))
                .OrderBy(rule => rule.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            RoutingRulesInitialized = true;
        }

        if (string.IsNullOrWhiteSpace(ChannelVolumeMode))
        {
            ChannelVolumeMode = "ApplicationSessions";
        }

        MonitorMix ??= new MonitorMixSettings();
        MonitorMix.ChannelGains = new Dictionary<string, float>(
            MonitorMix.ChannelGains ?? [],
            StringComparer.OrdinalIgnoreCase);
        MonitorMix.ChannelMutes = new Dictionary<string, bool>(
            MonitorMix.ChannelMutes ?? [],
            StringComparer.OrdinalIgnoreCase);
        MonitorMix.MasterGain = Math.Clamp(MonitorMix.MasterGain, 0f, 1f);
        MonitorMix.LatencyMs = Math.Clamp(MonitorMix.LatencyMs, 10, 500);
        MonitorMix.ChannelSliderMode = "Monitor Mix Gain";

        foreach (var channelName in MixerChannel.DefaultChannelNames)
        {
            if (!MonitorMix.ChannelGains.ContainsKey(channelName))
            {
                MonitorMix.ChannelGains[channelName] = 0.5f;
            }
            else
            {
                MonitorMix.ChannelGains[channelName] = Math.Clamp(MonitorMix.ChannelGains[channelName], 0f, 1f);
            }

            if (!MonitorMix.ChannelMutes.ContainsKey(channelName))
            {
                MonitorMix.ChannelMutes[channelName] = false;
            }
        }

        StreamMix ??= new StreamMixSettings();
        StreamMix.ChannelGains = new Dictionary<string, float>(
            StreamMix.ChannelGains ?? [],
            StringComparer.OrdinalIgnoreCase);
        StreamMix.ChannelMutes = new Dictionary<string, bool>(
            StreamMix.ChannelMutes ?? [],
            StringComparer.OrdinalIgnoreCase);
        StreamMix.MasterGain = Math.Clamp(StreamMix.MasterGain, 0f, 1f);
        StreamMix.LatencyMs = Math.Clamp(StreamMix.LatencyMs, 10, 500);

        foreach (var channelName in MixerChannel.DefaultChannelNames)
        {
            if (!StreamMix.ChannelGains.ContainsKey(channelName))
            {
                StreamMix.ChannelGains[channelName] = 1.0f;
            }
            else
            {
                StreamMix.ChannelGains[channelName] = Math.Clamp(StreamMix.ChannelGains[channelName], 0f, 1f);
            }

            if (!StreamMix.ChannelMutes.ContainsKey(channelName))
            {
                StreamMix.ChannelMutes[channelName] = false;
            }
        }

        if (SerialBaudRate <= 0)
        {
            SerialBaudRate = 115200;
        }
    }

    public static List<MixerChannel> CreateDefaultChannels()
        => MixerChannel.DefaultChannelNames.Select(name => new MixerChannel(name)).ToList();

    public static List<AppRoutingRule> CreateDefaultRoutingRules()
    {
        return
        [
            new AppRoutingRule("spotify.exe", "Music"),
            new AppRoutingRule("discord.exe", "Chat"),
            new AppRoutingRule("chrome.exe", "Media"),
            new AppRoutingRule("game.exe", "Game")
        ];
    }

    private static MixerChannel NormalizeChannel(MixerChannel channel, string defaultName)
    {
        channel.Name = defaultName;
        channel.VolumePercent = Math.Clamp(channel.VolumePercent, 0, 100);
        return channel;
    }

    private static AppRoutingRule NormalizeRoutingRule(AppRoutingRule rule)
    {
        rule.ProcessName = NormalizeProcessName(rule.ProcessName);
        rule.PreferredChannel = MixerChannel.DefaultChannelNames.First(channel =>
            channel.Equals(rule.PreferredChannel, StringComparison.OrdinalIgnoreCase));
        return rule;
    }

    private static bool IsKnownChannel(string? channelName)
    {
        return MixerChannel.DefaultChannelNames.Any(channel =>
            channel.Equals(channelName, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeProcessName(string processName)
    {
        var normalized = processName.Trim();
        if (normalized.Equals("System Sounds", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("pid:", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : normalized + ".exe";
    }
}
