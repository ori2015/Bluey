using System.Runtime.InteropServices;
using GooglyWindows.Core.Automation;
using GooglyWindows.Core.Screen;
namespace GooglyWindows.Automation;
public sealed class ComputerControl
{
    private static void Send(params Native.Input[] inputs)
    {
        if (Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.Input>()) != inputs.Length)
            throw new InvalidOperationException("Windows blocked input. Bluey can't control elevated apps or the UAC desktop.");
    }
    private static Native.Input Mouse(uint flags, uint data = 0) => new() { Type = 0, Data = new() { Mouse = new() { Flags = flags, MouseData = data } } };
    private static Native.Input Key(ushort key, bool up = false, ushort scan = 0) => new() { Type = 1, Data = new() { Keyboard = new() { Key = key, Scan = scan, Flags = (uint)((up ? 2 : 0) | (scan != 0 ? 4 : 0) | (key is >= 0x21 and <= 0x2E or 0x5B ? 1 : 0)) } } };
    public static void RequireAccessibleWindow()
    {
        if (Native.Elevated(Native.GetForegroundWindow())) throw new InvalidOperationException("This window is elevated. Open it normally or perform the action yourself; Bluey won't bypass UAC.");
    }
    public static PixelPoint MousePosition { get { Native.GetCursorPos(out var p); return new(p.X, p.Y); } }
    public static void Move(PixelPoint p) { if (!Native.SetCursorPos((int)Math.Round(p.X), (int)Math.Round(p.Y))) throw new InvalidOperationException("Couldn't move the mouse."); }
    public async Task ClickAsync(PixelPoint p, int count, bool right, CancellationToken ct)
    {
        var saved = MousePosition;
        try
        {
            Move(p);
            for (var i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested(); RequireAccessibleWindow();
                var targetWindow = Native.GetAncestor(Native.WindowFromPoint(new Native.Point { X = (int)p.X, Y = (int)p.Y }), 2);
                if (targetWindow != 0 && Native.Elevated(targetWindow)) throw new InvalidOperationException("The target app is elevated. Bluey won't bypass UAC.");
                Send(Mouse(right ? 8u : 2u));
                try { await Task.Delay(35, ct); } finally { Send(Mouse(right ? 16u : 4u)); }
                if (i + 1 < count) await Task.Delay(80, ct);
            }
        }
        finally { Move(saved); }
    }
    public async Task DragAsync(PixelPoint from, PixelPoint to, CancellationToken ct, Func<PixelPoint, Task>? showPosition = null)
    {
        var saved = MousePosition; var down = false;
        try
        {
            RequireAccessibleWindow(); Move(from); Send(Mouse(2)); down = true;
            for (var i = 1; i <= 36; i++)
            {
                ct.ThrowIfCancellationRequested(); var position = new PixelPoint(from.X + (to.X - from.X) * i / 36, from.Y + (to.Y - from.Y) * i / 36);
                Move(position); if (showPosition is not null) await showPosition(position); await Task.Delay(16, ct);
            }
        }
        finally { if (down) Send(Mouse(4)); Move(saved); }
    }
    public async Task TypeAsync(string text, CancellationToken ct)
    {
        RequireAccessibleWindow(); ControlsReader.RequireNonSecureFocus();
        foreach (var c in text)
        {
            ct.ThrowIfCancellationRequested(); ControlsReader.RequireNonSecureFocus();
            if (c is '\n' or '\r') Press("enter"); else if (c == '\t') Press("tab"); else Send(Key(0, scan: c), Key(0, true, c));
            await Task.Delay(text.Length > 160 ? 2 : 10, ct);
        }
    }
    public static void Press(string combo)
    {
        RequireAccessibleWindow(); ControlsReader.RequireNonSecureFocus(); var keys = KeyboardShortcut.Parse(combo); var held = new List<ushort>();
        try { foreach (var k in keys) { Send(Key(k)); held.Add(k); } }
        finally { foreach (var k in held.AsEnumerable().Reverse()) Send(Key(k, true)); }
    }
    public static void Scroll(PixelPoint p, string direction, double amount)
    {
        RequireAccessibleWindow(); var saved = MousePosition;
        try { Move(p); var delta = (int)(amount * 120) * (direction is "up" or "right" ? 1 : -1); Send(Mouse(direction is "left" or "right" ? 0x1000u : 0x800u, unchecked((uint)delta))); }
        finally { Move(saved); }
    }
}
