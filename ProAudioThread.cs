using System.Runtime.InteropServices;

namespace AudioMixerVB;

/// <summary>
/// Registers the calling thread with MMCSS under the "Pro Audio" task so the
/// Windows scheduler gives it the same priority boost real audio engines get.
/// Reduces scheduling jitter, which is what causes dropouts at small buffers.
/// </summary>
internal static class ProAudioThread
{
    [ThreadStatic]
    private static bool registered;

    public static void Register()
    {
        if (registered)
        {
            return;
        }

        registered = true;
        var taskIndex = 0;
        _ = AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex);
    }

    [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref int taskIndex);
}
