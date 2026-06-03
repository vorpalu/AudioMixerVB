namespace AudioMixerVB;

public sealed class AudioEndpoint
{
    public string Id { get; set; } = string.Empty;

    public string FriendlyName { get; set; } = string.Empty;

    public string DataFlow { get; set; } = "Render";

    public string State { get; set; } = "Active";

    public string DisplayName => string.IsNullOrWhiteSpace(FriendlyName) ? Id : FriendlyName;

    public override string ToString() => DisplayName;
}
