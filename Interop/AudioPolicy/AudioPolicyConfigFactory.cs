using System.Runtime.InteropServices;

namespace AudioMixerVB.Interop;

public static class AudioPolicyConfigFactory
{
    // Undocumented WinRT audio policy class used by EarTrumpet for per-app output preferences.
    // It is activated manually and called through raw vtable slots to avoid managed IInspectable marshalling.
    internal const string AudioPolicyConfigClassId = "Windows.Media.Internal.AudioPolicyConfig";

    private const int Version21H2Build = 21390;

    private static readonly Guid IidFor21H2 = new("ab3d4648-e242-459f-b02f-541c70306324");
    private static readonly Guid IidForDownlevel = new("2a59116d-6c4f-45e0-a74f-707e3fef9258");

    public static IAudioPolicyConfigFactory Create()
    {
        var is21H2OrNewer = Environment.OSVersion.Version.Build >= Version21H2Build;
        return new RawAudioPolicyConfigFactory(
            is21H2OrNewer ? IidFor21H2 : IidForDownlevel,
            is21H2OrNewer ? "RawAudioPolicyConfigFactoryFor21H2" : "RawAudioPolicyConfigFactoryForDownlevel");
    }
}

internal sealed class RawAudioPolicyConfigFactory : IAudioPolicyConfigFactory
{
    // WinRT interface layout: IUnknown(3) + IInspectable(3) + 19 undocumented members before routing methods.
    private const int SetPersistedDefaultAudioEndpointSlot = 25;
    private const int GetPersistedDefaultAudioEndpointSlot = 26;
    private const int ClearAllPersistedApplicationDefaultEndpointsSlot = 27;

    private readonly IntPtr factoryPointer;
    private bool disposed;

    public RawAudioPolicyConfigFactory(Guid interfaceId, string implementationName)
    {
        InterfaceId = interfaceId;
        InterfaceImplementation = implementationName;

        var classIdHString = IntPtr.Zero;
        try
        {
            classIdHString = Combase.CreateHString(AudioPolicyConfigFactory.AudioPolicyConfigClassId);
            var activationResult = Combase.RoGetActivationFactory(classIdHString, ref interfaceId, out factoryPointer);
            if (!HResult.Succeeded(activationResult))
            {
                throw new COMException(
                    $"RoGetActivationFactory failed for {AudioPolicyConfigFactory.AudioPolicyConfigClassId}, IID={interfaceId}.",
                    activationResult);
            }

            if (factoryPointer == IntPtr.Zero)
            {
                throw new COMException(
                    $"RoGetActivationFactory returned a null factory pointer for IID={interfaceId}.",
                    unchecked((int)0x80004003));
            }
        }
        finally
        {
            if (classIdHString != IntPtr.Zero)
            {
                _ = Combase.WindowsDeleteString(classIdHString);
            }
        }
    }

    public string InterfaceImplementation { get; }

    public Guid InterfaceId { get; }

    public int SetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, IntPtr deviceId)
    {
        ThrowIfDisposed();
        var method = GetDelegate<SetPersistedDefaultAudioEndpointDelegate>(SetPersistedDefaultAudioEndpointSlot);
        return method(factoryPointer, processId, flow, role, deviceId);
    }

    public int GetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, out string deviceId)
    {
        ThrowIfDisposed();
        var method = GetDelegate<GetPersistedDefaultAudioEndpointDelegate>(GetPersistedDefaultAudioEndpointSlot);
        var result = method(factoryPointer, processId, flow, role, out var hstring);
        deviceId = Combase.ReadHStringAndDelete(hstring);
        return result;
    }

    public int ClearAllPersistedApplicationDefaultEndpoints()
    {
        ThrowIfDisposed();
        var method = GetDelegate<ClearAllPersistedApplicationDefaultEndpointsDelegate>(
            ClearAllPersistedApplicationDefaultEndpointsSlot);
        return method(factoryPointer);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (factoryPointer != IntPtr.Zero)
        {
            _ = Marshal.Release(factoryPointer);
        }
    }

    private TDelegate GetDelegate<TDelegate>(int slot)
        where TDelegate : Delegate
    {
        var vtable = Marshal.ReadIntPtr(factoryPointer);
        var methodPointer = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(methodPointer);
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(RawAudioPolicyConfigFactory));
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetPersistedDefaultAudioEndpointDelegate(
        IntPtr self,
        uint processId,
        EDataFlow flow,
        ERole role,
        IntPtr deviceId);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetPersistedDefaultAudioEndpointDelegate(
        IntPtr self,
        uint processId,
        EDataFlow flow,
        ERole role,
        out IntPtr deviceId);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ClearAllPersistedApplicationDefaultEndpointsDelegate(IntPtr self);
}
