using System.Globalization;

namespace AudioMixerVB;

public enum SerialCommandType
{
    SetVolume,
    SetMute
}

public sealed class SerialCommand
{
    public SerialCommandType Type { get; init; }

    public string ChannelName { get; init; } = string.Empty;

    public int? VolumePercent { get; init; }

    public bool? IsMuted { get; init; }

    public static bool TryParse(string? line, out SerialCommand? command, out string? error)
    {
        command = null;
        error = null;

        var text = line?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (TryParseColonCommand(text, out command, out error) ||
            TryParseUniversalCommand(text, out command, out error))
        {
            return true;
        }

        error ??= "Unknown command format.";
        return false;
    }

    private static bool TryParseColonCommand(string text, out SerialCommand? command, out string? error)
    {
        command = null;
        error = null;

        var separatorIndex = text.IndexOf(':');
        if (separatorIndex < 0)
        {
            return false;
        }

        var left = text[..separatorIndex].Trim();
        var right = text[(separatorIndex + 1)..].Trim();

        if (left.StartsWith("MUTE_", StringComparison.OrdinalIgnoreCase))
        {
            var channelText = left["MUTE_".Length..];
            if (!TryNormalizeChannel(channelText, out var channelName))
            {
                error = $"Unknown channel '{channelText}'.";
                return false;
            }

            if (!TryParseMuteValue(right, out var muted))
            {
                error = $"Mute value must be 0/1, true/false, or on/off. Got '{right}'.";
                return false;
            }

            command = new SerialCommand
            {
                Type = SerialCommandType.SetMute,
                ChannelName = channelName,
                IsMuted = muted
            };
            return true;
        }

        if (!TryNormalizeChannel(left, out var volumeChannel))
        {
            error = $"Unknown channel '{left}'.";
            return false;
        }

        if (!TryParseVolume(right, out var volumePercent))
        {
            error = $"Volume must be an integer from 0 to 100. Got '{right}'.";
            return false;
        }

        command = new SerialCommand
        {
            Type = SerialCommandType.SetVolume,
            ChannelName = volumeChannel,
            VolumePercent = volumePercent
        };
        return true;
    }

    private static bool TryParseUniversalCommand(string text, out SerialCommand? command, out string? error)
    {
        command = null;
        error = null;

        var parts = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            return false;
        }

        if (!TryNormalizeChannel(parts[1], out var channelName))
        {
            error = $"Unknown channel '{parts[1]}'.";
            return false;
        }

        if (parts[0].Equals("SET", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseVolume(parts[2], out var volumePercent))
            {
                error = $"Volume must be an integer from 0 to 100. Got '{parts[2]}'.";
                return false;
            }

            command = new SerialCommand
            {
                Type = SerialCommandType.SetVolume,
                ChannelName = channelName,
                VolumePercent = volumePercent
            };
            return true;
        }

        if (parts[0].Equals("MUTE", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseMuteValue(parts[2], out var muted))
            {
                error = $"Mute value must be 0/1, true/false, or on/off. Got '{parts[2]}'.";
                return false;
            }

            command = new SerialCommand
            {
                Type = SerialCommandType.SetMute,
                ChannelName = channelName,
                IsMuted = muted
            };
            return true;
        }

        return false;
    }

    private static bool TryNormalizeChannel(string value, out string channelName)
    {
        channelName = MixerChannel.DefaultChannelNames.FirstOrDefault(
            channel => channel.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

        return channelName.Length > 0;
    }

    private static bool TryParseVolume(string value, out int volumePercent)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out volumePercent) &&
            volumePercent is >= 0 and <= 100;
    }

    private static bool TryParseMuteValue(string value, out bool muted)
    {
        muted = false;

        if (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("muted", StringComparison.OrdinalIgnoreCase))
        {
            muted = true;
            return true;
        }

        if (value.Equals("0", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("off", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("unmuted", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
