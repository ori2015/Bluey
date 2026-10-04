using System.Runtime.InteropServices;
using System.Text;
using GooglyWindows.Core.Screen;
namespace GooglyWindows.Automation;
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public readonly PixelRect Pixels => new(Left, Top, Right - Left, Bottom - Top); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct Monitor { public uint Size; public Rect Bounds, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput { public int X, Y; public uint MouseData, Flags, Time; public nint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public nint ExtraInfo; }
    internal delegate bool MonitorCallback(nint monitor, nint dc, ref Rect rect, nint data);
    internal delegate bool WindowCallback(nint window, nint data);
    [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(nint monitor, ref Monitor info);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint window, StringBuilder text, int count);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(WindowCallback callback, nint data);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("advapi32.dll", SetLastError = true)] internal static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)] internal static extern bool GetTokenInformation(nint token, int type, out int information, int size, out int returned);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(nint handle);
    internal static string WindowTitle(nint window) { var text = new StringBuilder(1024); GetWindowText(window, text, text.Capacity); return text.ToString(); }
    internal static bool Elevated(nint window)
    {
        GetWindowThreadProcessId(window, out var pid); var process = OpenProcess(0x1000, false, pid);
        if (process == 0) return true;
        try { if (!OpenProcessToken(process, 8, out var token)) return true;
            try { return !GetTokenInformation(token, 20, out var elevated, 4, out _) || elevated != 0; } finally { CloseHandle(token); } }
        finally { CloseHandle(process); }
    }
    internal static List<MonitorInfo> Monitors()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(0, 0, (nint handle, nint dc, ref Rect rect, nint data) =>
        {
            var info = new Monitor { Size = (uint)Marshal.SizeOf<Monitor>(), Device = "" }; GetMonitorInfo(handle, ref info);
            GetDpiForMonitor(handle, 0, out var x, out var y); list.Add(new(handle, info.Bounds.Pixels, x == 0 ? 96 : x, y == 0 ? 96 : y, (info.Flags & 1) != 0)); return true;
        }, 0); return list;
    }
    internal static PixelRect DesktopBounds()
    {
        var monitors = Monitors(); if (monitors.Count == 0) throw new InvalidOperationException("No monitors available.");
        var x = monitors.Min(m => m.Bounds.X); var y = monitors.Min(m => m.Bounds.Y);
        return new(x, y, monitors.Max(m => m.Bounds.X + m.Bounds.Width) - x, monitors.Max(m => m.Bounds.Y + m.Bounds.Height) - y);
    }
}
