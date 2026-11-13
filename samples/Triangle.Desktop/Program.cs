using Olve.Results;
using Olve.WebGPU;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Triangle;

// The triangle in a Silk.NET window, through Dawn native.
//   --frames N                    closes the window after N frames
//   --glfw-platform wayland|x11   picks GLFW's platform on Linux (GLFW 3.4 prefers Wayland, which the prebuilt Dawn lacks)
var frameLimit = GetOption("--frames") is { } n ? int.Parse(n) : int.MaxValue;
var exitCode = 0;

if (GetOption("--glfw-platform") is { } platform)
{
    // GLFW_PLATFORM = GLFW_PLATFORM_WAYLAND or GLFW_PLATFORM_X11
    Glfw.GetApi().InitHint((InitHint)0x00050003, platform == "wayland" ? 0x00060003 : 0x00060004);
}

var options = WindowOptions.Default with
{
    Size = new Vector2D<int>(640, 480),
    Title = "Olve.WebGPU triangle",
    API = GraphicsAPI.None,
};
using var window = Window.Create(options);
TriangleRenderer? renderer = null;
var frame = 0;

window.Load += () =>
{
    var instance = WebGPU.CreateInstance();
    var surface = instance.CreateSurface(window.Native! switch
    {
        { Win32: { } win32 } => new SurfaceDescriptor
        {
            SurfaceSourceWindowsHWND = new SurfaceSourceWindowsHWND { Hinstance = win32.HInstance, Hwnd = win32.Hwnd },
        },
        { Wayland: { } wayland } => new SurfaceDescriptor
        {
            SurfaceSourceWaylandSurface = new SurfaceSourceWaylandSurface { Display = wayland.Display, Surface = wayland.Surface },
        },
        { X11: { } x11 } => new SurfaceDescriptor
        {
            SurfaceSourceXlibWindow = new SurfaceSourceXlibWindow { Display = x11.Display, Window = (ulong)x11.Window },
        },
        var native => throw new PlatformNotSupportedException($"No surface source for {native.Kind} windows"),
    });

    // Requests complete inside ProcessEvents, so on desktop the task is done (or nearly) by the time it returns
    var created = TriangleRenderer.CreateAsync(instance, surface, 640, 480, Console.WriteLine).GetAwaiter().GetResult();
    if (created.TryPickProblems(out var problems, out var value))
    {
        foreach (var problem in problems)
        {
            Console.WriteLine(problem.ToBriefString());
        }

        exitCode = 1;
        window.Close();
        return;
    }

    renderer = value;
    Console.WriteLine("renderer ready");
};

window.Render += _ =>
{
    renderer?.DrawFrame();
    frame++;
    if (frame >= frameLimit)
    {
        window.Close();
    }
};

window.Run();
return exitCode;

string? GetOption(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
