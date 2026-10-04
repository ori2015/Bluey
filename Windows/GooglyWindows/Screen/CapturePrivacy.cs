using GooglyWindows.Automation;
using GooglyWindows.Logging;
namespace GooglyWindows.Screen;
internal static class CapturePrivacy
{
    private static int unsafeWindow;
    internal static void Exclude(nint handle)
    {
        if (!Native.SetWindowDisplayAffinity(handle, 0x11))
        {
            Interlocked.Exchange(ref unsafeWindow, 1);
            SafeLog.Event("Warning", "capture", "own_window_exclusion_failed");
        }
    }
    internal static void RequireExcludedWindows()
    {
        if (Volatile.Read(ref unsafeWindow) != 0) throw new InvalidOperationException("Windows couldn't exclude Bluey's cursor overlays from capture. Restart on a supported Windows 11 desktop before using screen observation.");
    }
}
