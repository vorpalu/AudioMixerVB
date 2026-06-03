namespace AudioMixerVB;

public sealed class AppOutputRoutingResult
{
    public bool Success { get; set; }

    public string Status { get; set; } = "Manual required";

    public string Message { get; set; } = string.Empty;

    public string MethodName { get; set; } = string.Empty;

    public string HResult { get; set; } = string.Empty;

    public string ExceptionType { get; set; } = string.Empty;

    public string ExceptionMessage { get; set; } = string.Empty;

    public uint ProcessId { get; set; }

    public string ProcessName { get; set; } = string.Empty;

    public string TargetEndpointId { get; set; } = string.Empty;

    public string TargetEndpointFriendlyName { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string Flow { get; set; } = string.Empty;

    public string VerificationEndpointId { get; set; } = string.Empty;

    public string InterfaceImplementation { get; set; } = string.Empty;

    public bool RequiresManualFallback { get; set; } = true;

    public bool RequiresRestart { get; set; }

    public bool RequiresAppRestart { get; set; }

    public List<string> DiagnosticMessages { get; set; } = [];
}
