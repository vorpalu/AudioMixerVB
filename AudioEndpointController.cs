using System.Runtime.InteropServices;

namespace AudioMixerVB;

public sealed class AudioEndpointController
{
    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    // Windows SDK propsys.idl declares IPropertyStore as 886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99.
    // 00000138-0000-0000-C000-000000000046 is IPropertyStorage, not IPropertyStore.
    private static readonly Guid IID_IPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    private static readonly PropertyKey PkeyDeviceFriendlyName = new(
        new Guid(0xA45C254E, 0xDF1C, 0x4EFD, 0x80, 0x20, 0x67, 0xD1, 0x46, 0xA8, 0x50, 0xE0),
        14);

    public event EventHandler<string>? LogMessage;

    public IReadOnlyList<AudioEndpoint> GetRenderEndpoints()
        => GetEndpoints(EDataFlow.eRender, "Render");

    public IReadOnlyList<AudioEndpoint> GetCaptureEndpoints()
        => GetEndpoints(EDataFlow.eCapture, "Capture");

    private IReadOnlyList<AudioEndpoint> GetEndpoints(EDataFlow dataFlow, string dataFlowName)
    {
        var enumerator = IntPtr.Zero;
        var collection = IntPtr.Zero;

        try
        {
            enumerator = CreateDeviceEnumerator();

            var enumAudioEndpoints = GetComMethod<EnumAudioEndpointsDelegate>(
                enumerator,
                Vtable.IMMDeviceEnumerator.EnumAudioEndpoints);

            ThrowIfFailed(
                enumAudioEndpoints(enumerator, dataFlow, DEVICE_STATE.DEVICE_STATE_ACTIVE, out collection),
                "IMMDeviceEnumerator.EnumAudioEndpoints");

            var getCount = GetComMethod<DeviceCollectionGetCountDelegate>(
                collection,
                Vtable.IMMDeviceCollection.GetCount);

            ThrowIfFailed(getCount(collection, out var count), "IMMDeviceCollection.GetCount");

            var endpoints = new List<AudioEndpoint>((int)count);
            var item = GetComMethod<DeviceCollectionItemDelegate>(collection, Vtable.IMMDeviceCollection.Item);

            for (uint index = 0; index < count; index++)
            {
                var device = IntPtr.Zero;

                try
                {
                    ThrowIfFailed(item(collection, index, out device), $"IMMDeviceCollection.Item({index})");

                    var id = GetDeviceId(device);
                    var friendlyName = GetFriendlyName(device);

                    endpoints.Add(new AudioEndpoint
                    {
                        Id = id,
                        FriendlyName = friendlyName,
                        DataFlow = dataFlowName,
                        State = "Active"
                    });
                }
                finally
                {
                    ReleaseComPointer(ref device);
                }
            }

            return endpoints;
        }
        finally
        {
            ReleaseComPointer(ref collection);
            ReleaseComPointer(ref enumerator);
        }
    }

    public int GetVolumePercent(string endpointId)
    {
        return WithEndpointVolume(endpointId, volume =>
        {
            var getVolume = GetComMethod<GetMasterVolumeLevelScalarDelegate>(
                volume,
                Vtable.IAudioEndpointVolume.GetMasterVolumeLevelScalar);

            ThrowIfFailed(getVolume(volume, out var scalar), "IAudioEndpointVolume.GetMasterVolumeLevelScalar");
            return ScalarToPercent(scalar);
        });
    }

    public void SetVolumePercent(string endpointId, int volumePercent)
    {
        WithEndpointVolume(endpointId, volume =>
        {
            var setVolume = GetComMethod<SetMasterVolumeLevelScalarDelegate>(
                volume,
                Vtable.IAudioEndpointVolume.SetMasterVolumeLevelScalar);

            var scalar = Math.Clamp(volumePercent, 0, 100) / 100.0f;
            ThrowIfFailed(setVolume(volume, scalar, IntPtr.Zero), "IAudioEndpointVolume.SetMasterVolumeLevelScalar");
            return true;
        });
    }

    public bool GetMute(string endpointId)
    {
        return WithEndpointVolume(endpointId, volume =>
        {
            var getMute = GetComMethod<GetMuteDelegate>(volume, Vtable.IAudioEndpointVolume.GetMute);

            ThrowIfFailed(getMute(volume, out var muted), "IAudioEndpointVolume.GetMute");
            return muted;
        });
    }

    public void SetMute(string endpointId, bool muted)
    {
        WithEndpointVolume(endpointId, volume =>
        {
            var setMute = GetComMethod<SetMuteDelegate>(volume, Vtable.IAudioEndpointVolume.SetMute);

            ThrowIfFailed(setMute(volume, muted, IntPtr.Zero), "IAudioEndpointVolume.SetMute");
            return true;
        });
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

    private string GetDeviceId(IntPtr device)
    {
        var getId = GetComMethod<DeviceGetIdDelegate>(device, Vtable.IMMDevice.GetId);
        var idPointer = IntPtr.Zero;

        try
        {
            ThrowIfFailed(getId(device, out idPointer), "IMMDevice.GetId");
            return Marshal.PtrToStringUni(idPointer) ?? string.Empty;
        }
        finally
        {
            if (idPointer != IntPtr.Zero)
            {
                CoTaskMemFree(idPointer);
            }
        }
    }

    private string GetFriendlyName(IntPtr device)
    {
        var propertyStore = IntPtr.Zero;
        var propertyValue = default(PropVariant);

        try
        {
            var openPropertyStore = GetComMethod<DeviceOpenPropertyStoreDelegate>(
                device,
                Vtable.IMMDevice.OpenPropertyStore);

            ThrowIfFailed(
                openPropertyStore(device, STGM.STGM_READ, out propertyStore),
                "IMMDevice.OpenPropertyStore(IPropertyStore)");

            _ = IID_IPropertyStore; // Keeps the official IID close to the OpenPropertyStore code path.

            var getValue = GetComMethod<PropertyStoreGetValueDelegate>(
                propertyStore,
                Vtable.IPropertyStore.GetValue);

            var friendlyNameKey = PkeyDeviceFriendlyName;
            ThrowIfFailed(
                getValue(propertyStore, ref friendlyNameKey, out propertyValue),
                "IPropertyStore.GetValue(PKEY_Device_FriendlyName)");

            return propertyValue.GetStringValue() ?? "(Unnamed endpoint)";
        }
        finally
        {
            propertyValue.Clear();
            ReleaseComPointer(ref propertyStore);
        }
    }

    private T WithEndpointVolume<T>(string endpointId, Func<IntPtr, T> action)
    {
        if (string.IsNullOrWhiteSpace(endpointId))
        {
            throw new ArgumentException("Endpoint ID cannot be empty.", nameof(endpointId));
        }

        var enumerator = IntPtr.Zero;
        var device = IntPtr.Zero;
        var endpointVolume = IntPtr.Zero;

        try
        {
            enumerator = CreateDeviceEnumerator();

            var getDevice = GetComMethod<EnumeratorGetDeviceDelegate>(
                enumerator,
                Vtable.IMMDeviceEnumerator.GetDevice);

            ThrowIfFailed(getDevice(enumerator, endpointId, out device), "IMMDeviceEnumerator.GetDevice");

            var activate = GetComMethod<DeviceActivateDelegate>(device, Vtable.IMMDevice.Activate);
            var iid = IID_IAudioEndpointVolume;

            ThrowIfFailed(
                activate(device, ref iid, CLSCTX.CLSCTX_ALL, IntPtr.Zero, out endpointVolume),
                "IMMDevice.Activate(IAudioEndpointVolume)");

            return action(endpointVolume);
        }
        finally
        {
            ReleaseComPointer(ref endpointVolume);
            ReleaseComPointer(ref device);
            ReleaseComPointer(ref enumerator);
        }
    }

    private static int ScalarToPercent(float scalar)
    {
        return Math.Clamp((int)Math.Round(scalar * 100.0f, MidpointRounding.AwayFromZero), 0, 100);
    }

    private void ThrowIfFailed(int hresult, string operation)
    {
        if (hresult >= 0)
        {
            return;
        }

        var message = Marshal.GetExceptionForHR(hresult)?.Message ?? "Unknown COM error.";
        LogMessage?.Invoke(this, $"{operation} failed: 0x{hresult:X8} ({message})");
        Marshal.ThrowExceptionForHR(hresult);
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
            public const int EnumAudioEndpoints = 3;
            public const int GetDefaultAudioEndpoint = 4;
            public const int GetDevice = 5;
            public const int RegisterEndpointNotificationCallback = 6;
            public const int UnregisterEndpointNotificationCallback = 7;
        }

        public static class IMMDeviceCollection
        {
            public const int GetCount = 3;
            public const int Item = 4;
        }

        public static class IMMDevice
        {
            public const int Activate = 3;
            public const int OpenPropertyStore = 4;
            public const int GetId = 5;
            public const int GetState = 6;
        }

        public static class IPropertyStore
        {
            public const int GetCount = 3;
            public const int GetAt = 4;
            public const int GetValue = 5;
            public const int SetValue = 6;
            public const int Commit = 7;
        }

        public static class IAudioEndpointVolume
        {
            public const int RegisterControlChangeNotify = 3;
            public const int UnregisterControlChangeNotify = 4;
            public const int GetChannelCount = 5;
            public const int SetMasterVolumeLevel = 6;
            public const int SetMasterVolumeLevelScalar = 7;
            public const int GetMasterVolumeLevel = 8;
            public const int GetMasterVolumeLevelScalar = 9;
            public const int SetChannelVolumeLevel = 10;
            public const int SetChannelVolumeLevelScalar = 11;
            public const int GetChannelVolumeLevel = 12;
            public const int GetChannelVolumeLevelScalar = 13;
            public const int SetMute = 14;
            public const int GetMute = 15;
        }
    }

    private enum EDataFlow : int
    {
        eRender = 0,
        eCapture = 1,
        eAll = 2
    }

    [Flags]
    private enum DEVICE_STATE : uint
    {
        DEVICE_STATE_ACTIVE = 0x00000001,
        DEVICE_STATE_DISABLED = 0x00000002,
        DEVICE_STATE_NOTPRESENT = 0x00000004,
        DEVICE_STATE_UNPLUGGED = 0x00000008,
        DEVICE_STATEMASK_ALL = 0x0000000F
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

    private enum STGM : uint
    {
        STGM_READ = 0x00000000
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey(Guid formatId, uint propertyId)
    {
        public readonly Guid FormatId = formatId;

        public readonly uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        private readonly ushort valueType;

        [FieldOffset(8)]
        private readonly IntPtr pointerValue;

        public string? GetStringValue()
        {
            return valueType switch
            {
                30 => Marshal.PtrToStringAnsi(pointerValue),
                31 => Marshal.PtrToStringUni(pointerValue),
                _ => null
            };
        }

        public void Clear()
        {
            if (valueType != 0)
            {
                PropVariantClear(ref this);
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAudioEndpointsDelegate(
        IntPtr self,
        EDataFlow dataFlow,
        DEVICE_STATE stateMask,
        out IntPtr devices);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumeratorGetDeviceDelegate(
        IntPtr self,
        [MarshalAs(UnmanagedType.LPWStr)] string id,
        out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DeviceCollectionGetCountDelegate(IntPtr self, out uint count);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DeviceCollectionItemDelegate(IntPtr self, uint index, out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DeviceActivateDelegate(
        IntPtr self,
        ref Guid interfaceId,
        CLSCTX clsContext,
        IntPtr activationParams,
        out IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DeviceOpenPropertyStoreDelegate(IntPtr self, STGM access, out IntPtr propertyStore);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DeviceGetIdDelegate(IntPtr self, out IntPtr id);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PropertyStoreGetValueDelegate(IntPtr self, ref PropertyKey key, out PropVariant value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetMasterVolumeLevelScalarDelegate(IntPtr self, out float level);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetMasterVolumeLevelScalarDelegate(IntPtr self, float level, IntPtr eventContext);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetMuteDelegate(IntPtr self, [MarshalAs(UnmanagedType.Bool)] out bool muted);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetMuteDelegate(
        IntPtr self,
        [MarshalAs(UnmanagedType.Bool)] bool muted,
        IntPtr eventContext);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(
        ref Guid rclsid,
        IntPtr pUnkOuter,
        CLSCTX dwClsContext,
        ref Guid riid,
        out IntPtr ppv);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoTaskMemFree(IntPtr pointer);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int PropVariantClear(ref PropVariant propVariant);
}
