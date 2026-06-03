namespace AudioMixerVB.Interop;

public enum EDataFlow
{
    eRender = 0,
    eCapture = 1,
    eAll = 2,
    EDataFlow_enum_count = 3
}

[Flags]
public enum ERole
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2,
    ERole_enum_count = 3
}

internal static class HResult
{
    public const int S_OK = 0;

    public static bool Succeeded(int hresult) => hresult >= 0;

    public static string Format(int hresult) => $"0x{hresult:X8}";
}
