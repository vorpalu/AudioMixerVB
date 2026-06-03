namespace AudioMixerVB;

public sealed class AudioAppGroup
{
    public string ProcessName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public List<AudioAppSession> Sessions { get; set; } = [];

    public List<int> ProcessIds { get; set; } = [];

    public string AssignedChannel { get; set; } = "None";

    public string? TargetEndpointId { get; set; }

    public string TargetEndpointFriendlyName { get; set; } = string.Empty;

    public string Status { get; set; } = "Active";

    public float Volume { get; set; }

    public int SessionCount => Sessions.Count;

    public int VolumePercent => Math.Clamp((int)Math.Round(Volume * 100.0f, MidpointRounding.AwayFromZero), 0, 100);

    public string CurrentEndpointsSummary { get; set; } = string.Empty;

    public bool HasTargetEndpoint
        => !string.IsNullOrWhiteSpace(TargetEndpointId);

    public bool HasAnySessionOnTarget()
    {
        if (!HasTargetEndpoint)
        {
            return false;
        }

        return Sessions.Any(session =>
            string.Equals(session.CurrentEndpointId, TargetEndpointId, StringComparison.OrdinalIgnoreCase));
    }

    public bool AreAllSessionsOnTarget()
    {
        if (!HasTargetEndpoint || Sessions.Count == 0)
        {
            return false;
        }

        return Sessions.All(session =>
            string.Equals(session.CurrentEndpointId, TargetEndpointId, StringComparison.OrdinalIgnoreCase));
    }
}
