using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Olve.Results;
using Olve.WebGPU;
using Triangle;

// The triangle on a canvas, through emdawnwebgpu. main.js drives the frames with requestAnimationFrame.
[SupportedOSPlatform("browser")]
public static partial class Entry
{
    private static TriangleRenderer? _renderer;

    public static async Task Main()
    {
        var instance = Wgpu.CreateInstance();
        var surface = instance.CreateSurface(new SurfaceDescriptor
        {
            EmscriptenSurfaceSourceCanvasHTMLSelector = new EmscriptenSurfaceSourceCanvasHTMLSelector { Selector = "#canvas"u8 },
        });
        var created = await TriangleRenderer.CreateAsync(instance, surface, 640, 480, Console.WriteLine);
        if (created.TryPickProblems(out var problems, out var renderer))
        {
            foreach (var problem in problems)
            {
                Console.WriteLine(problem.ToBriefString());
            }

            return;
        }

        _renderer = renderer;
        Console.WriteLine("renderer ready");
    }

    /// <summary>Draws a frame once the renderer is ready; the browser presents when the callback returns.</summary>
    [JSExport]
    public static bool Frame()
    {
        if (_renderer is null)
        {
            return false;
        }

        _renderer.DrawFrame();
        return true;
    }
}
