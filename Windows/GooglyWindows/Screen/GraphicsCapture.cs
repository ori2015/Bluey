using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using GooglyWindows.Core.Screen;
using Bitmap = System.Drawing.Bitmap;
namespace GooglyWindows.Screen;

public sealed class GraphicsCapture
{
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, in Guid iid);
        nint CreateForMonitor(nint monitor, in Guid iid);
    }
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string source, int length, out nint value);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, in Guid iid, out nint factory);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(nint adapter, int driver, nint software, uint flags, nint levels, uint count, uint sdk, out nint device, out int level, out nint context);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgi, out nint device);
    private static GraphicsCaptureItem Item(nint monitor)
    {
        const string name = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(name, name.Length, out var hstring));
        nint factory = 0, item = 0;
        try
        {
            var iid = typeof(IGraphicsCaptureItemInterop).GUID;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstring, iid, out factory));
            var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory);
            try { item = interop.CreateForMonitor(monitor, new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760")); return WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(item); }
            finally { Marshal.ReleaseComObject(interop); }
        }
        finally { if (item != 0) Marshal.Release(item); if (factory != 0) Marshal.Release(factory); WindowsDeleteString(hstring); }
    }
    private static IDirect3DDevice Device()
    {
        var hr = D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out var device, out _, out var context);
        if (hr < 0) Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, 5, 0, 0x20, 0, 0, 7, out device, out _, out context)); // WARP fallback.
        nint dxgi = 0, projected = 0;
        try
        {
            var iid = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C"); Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in iid, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out projected));
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(projected);
        }
        finally { if (projected != 0) Marshal.Release(projected); if (dxgi != 0) Marshal.Release(dxgi); if (context != 0) Marshal.Release(context); Marshal.Release(device); }
    }
    public async Task<Bitmap> CaptureAsync(MonitorInfo monitor, CancellationToken ct)
    {
        if (!GraphicsCaptureSession.IsSupported()) throw new InvalidOperationException("Windows Graphics Capture isn't supported on this desktop.");
        using var device = Device(); var item = Item(monitor.Handle);
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);
        using var session = pool.CreateCaptureSession(item); session.IsCursorCaptureEnabled = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var completion = new TaskCompletionSource<SoftwareBitmap>(TaskCreationOptions.RunContinuationsAsynchronously); var reading = 0;
        var copyTask = Task.CompletedTask;
        pool.FrameArrived += (sender, _) =>
        {
            if (Interlocked.Exchange(ref reading, 1) != 0) return;
            copyTask = CopyAsync(sender);
        };
        async Task CopyAsync(Direct3D11CaptureFramePool sender)
        {
            try { using var frame = sender.TryGetNextFrame(); if (frame is null) throw new InvalidOperationException("No capture frame.");
                var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Ignore).AsTask(timeout.Token); completion.TrySetResult(bitmap); }
            catch (Exception e) { completion.TrySetException(e); }
        }
        session.StartCapture();
        SoftwareBitmap software;
        try { software = await completion.Task.WaitAsync(timeout.Token); }
        catch { session.Dispose(); await copyTask; if (completion.Task.IsCompletedSuccessfully) completion.Task.Result.Dispose(); throw; }
        using (software)
        {
            var buffer = new Windows.Storage.Streams.Buffer((uint)(software.PixelWidth * software.PixelHeight * 4)); software.CopyToBuffer(buffer);
            using var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer); var bytes = new byte[buffer.Length]; reader.ReadBytes(bytes);
            var bitmap = new Bitmap(software.PixelWidth, software.PixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, bitmap.PixelFormat);
            try { for (var y = 0; y < bitmap.Height; y++) Marshal.Copy(bytes, y * bitmap.Width * 4, data.Scan0 + y * data.Stride, bitmap.Width * 4); }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }
    }
}
