namespace AudioMixerVB.Interop;

public interface IAudioPolicyConfigFactory : IDisposable
{
    string InterfaceImplementation { get; }

    Guid InterfaceId { get; }

    int SetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, IntPtr deviceId);

    int GetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, out string deviceId);

    int ClearAllPersistedApplicationDefaultEndpoints();
}
