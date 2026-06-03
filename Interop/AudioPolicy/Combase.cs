using System.Runtime.InteropServices;

namespace AudioMixerVB.Interop;

internal static class Combase
{
    [DllImport("combase.dll", PreserveSig = true)]
    public static extern int RoGetActivationFactory(
        IntPtr activatableClassId,
        [In] ref Guid iid,
        out IntPtr factory);

    [DllImport("combase.dll", PreserveSig = true)]
    public static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        uint length,
        out IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    public static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    public static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    public static IntPtr CreateHString(string value)
    {
        var result = WindowsCreateString(value, (uint)value.Length, out var hstring);
        if (!HResult.Succeeded(result))
        {
            throw new COMException("WindowsCreateString failed.", result);
        }

        return hstring;
    }

    public static string ReadHStringAndDelete(IntPtr hstring)
    {
        if (hstring == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            var buffer = WindowsGetStringRawBuffer(hstring, out var length);
            return buffer == IntPtr.Zero || length == 0
                ? string.Empty
                : Marshal.PtrToStringUni(buffer, checked((int)length)) ?? string.Empty;
        }
        finally
        {
            _ = WindowsDeleteString(hstring);
        }
    }
}
