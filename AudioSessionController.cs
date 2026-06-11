using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AudioMixerVB;

public sealed class AudioSessionController
{
    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid IID_IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    private static readonly Guid IID_IAudioSessionControl2 = new("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D");
    private static readonly Guid IID_ISimpleAudioVolume = new("87CE5498-68D6-44E5-9215-6DA47EF883D8");

    public event EventHandler<string>? LogMessage;

    public IReadOnlyList<AudioAppSession> GetAudioSessions(IReadOnlyList<AudioEndpoint> endpoints)
    {
        var sessions = new List<AudioAppSession>();

        EnumerateSessions(endpoints, context =>
        {
            sessions.Add(ReadSessionInfo(context));
        });

        return sessions
            .OrderBy(session => session.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(session => session.ProcessId)
            .ThenBy(session => session.CurrentEndpointFriendlyName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<AudioSessionActionResult> SetSessionVolumeForProcesses(
        IReadOnlyList<AudioEndpoint> endpoints,
        IEnumerable<string> processNames,
        string channelName,
        int volumePercent)
    {
        var targets = processNames
            .Select(NormalizeProcessName)
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var results = new List<AudioSessionActionResult>();
        if (targets.Count == 0)
        {
            return results;
        }

        var scalar = Math.Clamp(volumePercent, 0, 100) / 100.0f;

        EnumerateSessions(endpoints, context =>
        {
            var session = ReadSessionInfo(context);
            if (!targets.Contains(session.ProcessName))
            {
                return;
            }

            try
            {
                var setMasterVolume = GetComMethod<SimpleAudioVolumeSetMasterVolumeDelegate>(
                    context.SimpleAudioVolume,
                    Vtable.ISimpleAudioVolume.SetMasterVolume);

                ThrowIfFailed(
                    setMasterVolume(context.SimpleAudioVolume, scalar, IntPtr.Zero),
                    "ISimpleAudioVolume.SetMasterVolume");

                results.Add(new AudioSessionActionResult
                {
                    ProcessName = session.ProcessName,
                    ProcessId = session.ProcessId,
                    EndpointFriendlyName = session.CurrentEndpointFriendlyName,
                    ChannelName = channelName,
                    VolumePercent = Math.Clamp(volumePercent, 0, 100),
                    Status = "Applied"
                });
            }
            catch (Exception ex)
            {
                results.Add(CreateErrorResult(session, channelName, Math.Clamp(volumePercent, 0, 100), null, ex));
            }
        });

        return results;
    }

    public IReadOnlyList<AudioSessionActionResult> SetSessionMuteForProcesses(
        IReadOnlyList<AudioEndpoint> endpoints,
        IEnumerable<string> processNames,
        string channelName,
        bool muted)
    {
        var targets = processNames
            .Select(NormalizeProcessName)
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var results = new List<AudioSessionActionResult>();
        if (targets.Count == 0)
        {
            return results;
        }

        EnumerateSessions(endpoints, context =>
        {
            var session = ReadSessionInfo(context);
            if (!targets.Contains(session.ProcessName))
            {
                return;
            }

            try
            {
                var setMute = GetComMethod<SimpleAudioVolumeSetMuteDelegate>(
                    context.SimpleAudioVolume,
                    Vtable.ISimpleAudioVolume.SetMute);

                ThrowIfFailed(setMute(context.SimpleAudioVolume, muted, IntPtr.Zero), "ISimpleAudioVolume.SetMute");

                results.Add(new AudioSessionActionResult
                {
                    ProcessName = session.ProcessName,
                    ProcessId = session.ProcessId,
                    EndpointFriendlyName = session.CurrentEndpointFriendlyName,
                    ChannelName = channelName,
                    IsMuted = muted,
                    Status = "Applied"
                });
            }
            catch (Exception ex)
            {
                results.Add(CreateErrorResult(session, channelName, session.VolumePercent, muted, ex));
            }
        });

        return results;
    }

    private void EnumerateSessions(IReadOnlyList<AudioEndpoint> endpoints, Action<SessionContext> action)
    {
        var enumerator = IntPtr.Zero;

        try
        {
            enumerator = CreateDeviceEnumerator();
            EnumerateSessions(enumerator, endpoints, action);
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"Audio session enumeration error: {ex.Message}");
        }
        finally
        {
            ReleaseComPointer(ref enumerator);
        }
    }

    private void EnumerateSessions(IntPtr enumerator, IReadOnlyList<AudioEndpoint> endpoints, Action<SessionContext> action)
    {
        foreach (var endpoint in endpoints)
        {
            var device = IntPtr.Zero;
            var sessionManager = IntPtr.Zero;
            var sessionEnumerator = IntPtr.Zero;

            try
            {
                device = GetDevice(enumerator, endpoint.Id);
                sessionManager = ActivateAudioSessionManager(device);
                sessionEnumerator = GetSessionEnumerator(sessionManager);

                var getCount = GetComMethod<AudioSessionEnumeratorGetCountDelegate>(
                    sessionEnumerator,
                    Vtable.IAudioSessionEnumerator.GetCount);

                ThrowIfFailed(getCount(sessionEnumerator, out var sessionCount), "IAudioSessionEnumerator.GetCount");

                var getSession = GetComMethod<AudioSessionEnumeratorGetSessionDelegate>(
                    sessionEnumerator,
                    Vtable.IAudioSessionEnumerator.GetSession);

                for (var index = 0; index < sessionCount; index++)
                {
                    var sessionControl = IntPtr.Zero;
                    var sessionControl2 = IntPtr.Zero;
                    var simpleAudioVolume = IntPtr.Zero;

                    try
                    {
                        ThrowIfFailed(
                            getSession(sessionEnumerator, index, out sessionControl),
                            $"IAudioSessionEnumerator.GetSession({index})");

                        sessionControl2 = QueryInterface(
                            sessionControl,
                            IID_IAudioSessionControl2,
                            "IAudioSessionControl2");

                        simpleAudioVolume = QueryInterface(
                            sessionControl,
                            IID_ISimpleAudioVolume,
                            "ISimpleAudioVolume");

                        action(new SessionContext(endpoint, sessionControl2, simpleAudioVolume));
                    }
                    catch (Exception ex)
                    {
                        LogMessage?.Invoke(this, $"Audio session read error on {endpoint.FriendlyName}: {ex.Message}");
                    }
                    finally
                    {
                        ReleaseComPointer(ref simpleAudioVolume);
                        ReleaseComPointer(ref sessionControl2);
                        ReleaseComPointer(ref sessionControl);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Audio session enumeration error on {endpoint.FriendlyName}: {ex.Message}");
            }
            finally
            {
                ReleaseComPointer(ref sessionEnumerator);
                ReleaseComPointer(ref sessionManager);
                ReleaseComPointer(ref device);
            }
        }
    }

    private AudioAppSession ReadSessionInfo(SessionContext context)
    {
        var processId = GetProcessId(context.SessionControl2);
        var processName = GetProcessName(processId);
        var displayName = GetDisplayName(context.SessionControl2);
        var volume = GetSessionVolume(context.SimpleAudioVolume);
        var muted = GetSessionMute(context.SimpleAudioVolume);

        return new AudioAppSession
        {
            ProcessId = processId,
            ProcessName = processName,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? processName : displayName,
            CurrentEndpointId = context.Endpoint.Id,
            CurrentEndpointFriendlyName = context.Endpoint.FriendlyName,
            Volume = volume,
            IsMuted = muted,
            Status = "Active"
        };
    }

    private IntPtr CreateDeviceEnumerator()
    {
        var clsid = CLSID_MMDeviceEnumerator;
        var iid = IID_IMMDeviceEnumerator;

        ThrowIfFailed(
            CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX.CLSCTX_ALL, ref iid, out var enumerator),
            "CoCreateInstance(CLSID_MMDeviceEnumerator, IID_IMMDeviceEnumerator)");

        if (enumerator == IntPtr.Zero)
        {
            throw new InvalidOperationException("CoCreateInstance returned a null IMMDeviceEnumerator pointer.");
        }

        return enumerator;
    }

    private IntPtr GetDevice(IntPtr enumerator, string endpointId)
    {
        var getDevice = GetComMethod<EnumeratorGetDeviceDelegate>(
            enumerator,
            Vtable.IMMDeviceEnumerator.GetDevice);

        ThrowIfFailed(getDevice(enumerator, endpointId, out var device), "IMMDeviceEnumerator.GetDevice");
        return device;
    }

    private IntPtr ActivateAudioSessionManager(IntPtr device)
    {
        var activate = GetComMethod<DeviceActivateDelegate>(device, Vtable.IMMDevice.Activate);
        var iid = IID_IAudioSessionManager2;

        ThrowIfFailed(
            activate(device, ref iid, CLSCTX.CLSCTX_ALL, IntPtr.Zero, out var sessionManager),
            "IMMDevice.Activate(IAudioSessionManager2)");

        return sessionManager;
    }

    private IntPtr GetSessionEnumerator(IntPtr sessionManager)
    {
        var getSessionEnumerator = GetComMethod<AudioSessionManager2GetSessionEnumeratorDelegate>(
            sessionManager,
            Vtable.IAudioSessionManager2.GetSessionEnumerator);

        ThrowIfFailed(
            getSessionEnumerator(sessionManager, out var sessionEnumerator),
            "IAudioSessionManager2.GetSessionEnumerator");

        return sessionEnumerator;
    }

    private int GetProcessId(IntPtr sessionControl2)
    {
        var getProcessId = GetComMethod<AudioSessionControl2GetProcessIdDelegate>(
            sessionControl2,
            Vtable.IAudioSessionControl2.GetProcessId);

        ThrowIfFailed(getProcessId(sessionControl2, out var processId), "IAudioSessionControl2.GetProcessId");
        return unchecked((int)processId);
    }

    private string GetDisplayName(IntPtr sessionControl2)
    {
        var getDisplayName = GetComMethod<AudioSessionControlGetStringDelegate>(
            sessionControl2,
            Vtable.IAudioSessionControl.GetDisplayName);

        return GetCoTaskMemString(sessionControl2, getDisplayName, "IAudioSessionControl.GetDisplayName");
    }

    private float GetSessionVolume(IntPtr simpleAudioVolume)
    {
        var getMasterVolume = GetComMethod<SimpleAudioVolumeGetMasterVolumeDelegate>(
            simpleAudioVolume,
            Vtable.ISimpleAudioVolume.GetMasterVolume);

        ThrowIfFailed(getMasterVolume(simpleAudioVolume, out var volume), "ISimpleAudioVolume.GetMasterVolume");
        return Math.Clamp(volume, 0.0f, 1.0f);
    }

    private bool GetSessionMute(IntPtr simpleAudioVolume)
    {
        var getMute = GetComMethod<SimpleAudioVolumeGetMuteDelegate>(
            simpleAudioVolume,
            Vtable.ISimpleAudioVolume.GetMute);

        ThrowIfFailed(getMute(simpleAudioVolume, out var muted), "ISimpleAudioVolume.GetMute");
        return muted;
    }

    private string GetCoTaskMemString(
        IntPtr self,
        AudioSessionControlGetStringDelegate getter,
        string operation)
    {
        var stringPointer = IntPtr.Zero;

        try
        {
            var hresult = getter(self, out stringPointer);
            if (hresult < 0)
            {
                LogComError(operation, hresult);
                return string.Empty;
            }

            return Marshal.PtrToStringUni(stringPointer) ?? string.Empty;
        }
        finally
        {
            if (stringPointer != IntPtr.Zero)
            {
                CoTaskMemFree(stringPointer);
            }
        }
    }

    private IntPtr QueryInterface(IntPtr unknown, Guid interfaceId, string interfaceName)
    {
        var iid = interfaceId;
        var hresult = Marshal.QueryInterface(unknown, in iid, out var queried);
        if (hresult >= 0 && queried != IntPtr.Zero)
        {
            return queried;
        }

        LogComError($"QueryInterface({interfaceName})", hresult);
        Marshal.ThrowExceptionForHR(hresult);
        return IntPtr.Zero;
    }

    private void ThrowIfFailed(int hresult, string operation)
    {
        if (hresult >= 0)
        {
            return;
        }

        LogComError(operation, hresult);
        Marshal.ThrowExceptionForHR(hresult);
    }

    private void LogComError(string operation, int hresult)
    {
        var message = Marshal.GetExceptionForHR(hresult)?.Message ?? "Unknown COM error.";
        LogMessage?.Invoke(this, $"{operation} failed: 0x{hresult:X8} ({message})");
    }

    private static AudioSessionActionResult CreateErrorResult(
        AudioAppSession session,
        string channelName,
        int volumePercent,
        bool? isMuted,
        Exception exception)
    {
        return new AudioSessionActionResult
        {
            ProcessName = session.ProcessName,
            ProcessId = session.ProcessId,
            EndpointFriendlyName = session.CurrentEndpointFriendlyName,
            ChannelName = channelName,
            VolumePercent = volumePercent,
            IsMuted = isMuted,
            Status = "Error",
            ErrorMessage = exception.Message
        };
    }

    private static string GetProcessName(int processId)
    {
        if (processId <= 0)
        {
            return "System Sounds";
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return NormalizeProcessName(process.ProcessName);
        }
        catch
        {
            return $"pid:{processId}";
        }
    }

    private static string NormalizeProcessName(string processName)
    {
        var normalized = processName.Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : normalized + ".exe";
    }

    private static T GetComMethod<T>(IntPtr comPointer, int vtableIndex)
        where T : Delegate
    {
        if (comPointer == IntPtr.Zero)
        {
            throw new InvalidOperationException("COM pointer is null.");
        }

        var vtable = Marshal.ReadIntPtr(comPointer);
        var methodPointer = Marshal.ReadIntPtr(vtable, vtableIndex * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(methodPointer);
    }

    private static void ReleaseComPointer(ref IntPtr comPointer)
    {
        if (comPointer == IntPtr.Zero)
        {
            return;
        }

        Marshal.Release(comPointer);
        comPointer = IntPtr.Zero;
    }

    private static class Vtable
    {
        public static class IMMDeviceEnumerator
        {
            public const int GetDevice = 5;
        }

        public static class IMMDevice
        {
            public const int Activate = 3;
        }

        public static class IAudioSessionManager2
        {
            public const int GetSessionEnumerator = 5;
        }

        public static class IAudioSessionEnumerator
        {
            public const int GetCount = 3;
            public const int GetSession = 4;
        }

        public static class IAudioSessionControl
        {
            public const int GetDisplayName = 4;
        }

        public static class IAudioSessionControl2
        {
            public const int GetProcessId = 14;
        }

        public static class ISimpleAudioVolume
        {
            public const int SetMasterVolume = 3;
            public const int GetMasterVolume = 4;
            public const int SetMute = 5;
            public const int GetMute = 6;
        }
    }

    private sealed class SessionContext(
        AudioEndpoint endpoint,
        IntPtr sessionControl2,
        IntPtr simpleAudioVolume)
    {
        public AudioEndpoint Endpoint { get; } = endpoint;

        public IntPtr SessionControl2 { get; } = sessionControl2;

        public IntPtr SimpleAudioVolume { get; } = simpleAudioVolume;
    }

    [Flags]
    private enum CLSCTX : uint
    {
        CLSCTX_INPROC_SERVER = 0x1,
        CLSCTX_INPROC_HANDLER = 0x2,
        CLSCTX_LOCAL_SERVER = 0x4,
        CLSCTX_REMOTE_SERVER = 0x10,
        CLSCTX_ALL = CLSCTX_INPROC_SERVER | CLSCTX_INPROC_HANDLER | CLSCTX_LOCAL_SERVER | CLSCTX_REMOTE_SERVER
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumeratorGetDeviceDelegate(
        IntPtr self,
        [MarshalAs(UnmanagedType.LPWStr)] string id,
        out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DeviceActivateDelegate(
        IntPtr self,
        ref Guid interfaceId,
        CLSCTX clsContext,
        IntPtr activationParams,
        out IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int AudioSessionManager2GetSessionEnumeratorDelegate(IntPtr self, out IntPtr sessionEnumerator);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int AudioSessionEnumeratorGetCountDelegate(IntPtr self, out int count);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int AudioSessionEnumeratorGetSessionDelegate(IntPtr self, int index, out IntPtr sessionControl);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int AudioSessionControlGetStringDelegate(IntPtr self, out IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int AudioSessionControl2GetProcessIdDelegate(IntPtr self, out uint processId);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SimpleAudioVolumeSetMasterVolumeDelegate(IntPtr self, float level, IntPtr eventContext);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SimpleAudioVolumeGetMasterVolumeDelegate(IntPtr self, out float level);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SimpleAudioVolumeSetMuteDelegate(
        IntPtr self,
        [MarshalAs(UnmanagedType.Bool)] bool muted,
        IntPtr eventContext);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SimpleAudioVolumeGetMuteDelegate(
        IntPtr self,
        [MarshalAs(UnmanagedType.Bool)] out bool muted);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(
        ref Guid rclsid,
        IntPtr pUnkOuter,
        CLSCTX dwClsContext,
        ref Guid riid,
        out IntPtr ppv);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoTaskMemFree(IntPtr pointer);
}
