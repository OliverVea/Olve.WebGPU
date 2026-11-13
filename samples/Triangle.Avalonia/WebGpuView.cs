using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Olve.WebGPU;
using Triangle.Desktop;
using Color = Olve.WebGPU.Color;

namespace Triangle.Avalonia;

/// <summary>
/// A WebGPU viewport in an Avalonia layout. Avalonia's NativeControlHost gives it a native child window, which becomes a WebGPU surface;
/// it draws a frame each time Avalonia asks for one. The native window always draws on top of Avalonia's own rendering, so
/// Avalonia controls can sit next to the viewport but not over it.
/// </summary>
public sealed class WebGpuView : NativeControlHost
{
    private Instance _instance;
    private Surface _surface;
    private TriangleRenderer? _renderer;
    private int _frame;
    private bool _attached;

    /// <summary>The number of frames drawn so far, after each frame.</summary>
    public event Action<int>? FrameDrawn;

    /// <summary>The renderer couldn't be created; the reason went to the console.</summary>
    public event Action? Failed;

    /// <summary>The blue of the background, from 0 to 1.</summary>
    public double BackgroundBlue
    {
        set
        {
            if (_renderer is not null)
            {
                _renderer.ClearColor = _renderer.ClearColor with { B = value };
            }
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = base.CreateNativeControlCore(parent);
        _instance = Wgpu.CreateInstance();
        _surface = _instance.CreateSurface(SurfaceFor(handle, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));
        _attached = true;
        _ = StartAsync();
        return handle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _attached = false;
        _renderer = null;
        _surface.Dispose();
        _instance.Dispose();
        base.DestroyNativeControlCore(control);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var (width, height) = PixelSize();
        _renderer?.Resize(width, height);
    }

    private async Task StartAsync()
    {
        var (width, height) = PixelSize();
        _renderer = await TriangleRenderer.CreateAsync(_instance, _surface, width, height, Console.WriteLine);
        if (_renderer is null)
        {
            Failed?.Invoke();
            return;
        }

        Console.WriteLine("renderer ready");
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan time)
    {
        if (!_attached || _renderer is null)
        {
            return;
        }

        _renderer.DrawFrame();
        FrameDrawn?.Invoke(++_frame);
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);
    }

    /// <summary>The control's size in device pixels: the surface's size.</summary>
    private (uint Width, uint Height) PixelSize()
    {
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        return ((uint)Math.Max(1, Bounds.Width * scale), (uint)Math.Max(1, Bounds.Height * scale));
    }

    /// <summary>The surface source for the native control Avalonia made on this platform.</summary>
    private static SurfaceDescriptor SurfaceFor(IPlatformHandle handle, double scale) => handle.HandleDescriptor switch
    {
        "HWND" => new SurfaceDescriptor
        {
            SurfaceSourceWindowsHWND = new SurfaceSourceWindowsHWND { Hinstance = GetModuleHandle(null), Hwnd = handle.Handle },
        },
        "NSView" => new SurfaceDescriptor
        {
            SurfaceSourceMetalLayer = new SurfaceSourceMetalLayer { Layer = MetalLayer.ForView(handle.Handle, scale) },
        },
        // An X11 window ID is the server's, so a display connection of our own can name it
        "XID" => new SurfaceDescriptor
        {
            SurfaceSourceXlibWindow = new SurfaceSourceXlibWindow { Display = XOpenDisplay(null), Window = (ulong)handle.Handle },
        },
        var other => throw new PlatformNotSupportedException($"No WebGPU surface source for a {other} native control"),
    };

    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("libX11.so.6")]
    private static extern nint XOpenDisplay(string? displayName);
}
