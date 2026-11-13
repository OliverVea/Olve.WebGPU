using Olve.Results;
using Olve.WebGPU;
using Buffer = Olve.WebGPU.Buffer;

namespace Triangle;

/// <summary>A coloured triangle, drawn the same way on desktop and in the browser.</summary>
public sealed class TriangleRenderer(Instance instance, Surface surface, Device device, Queue queue, RenderPipeline pipeline, Buffer vertices)
{
    private static ReadOnlySpan<byte> Shader => """
        struct VertexOut {
            @builtin(position) position: vec4f,
            @location(0) color: vec3f,
        };

        @vertex fn vs(@location(0) position: vec2f, @location(1) color: vec3f) -> VertexOut {
            var out: VertexOut;
            out.position = vec4f(position, 0.0, 1.0);
            out.color = color;
            return out;
        }

        @fragment fn fs(input: VertexOut) -> @location(0) vec4f {
            return vec4f(input.color, 1.0);
        }
        """u8;

    private static readonly float[] Vertices =
    [
        0.0f, 0.6f, 1f, 0f, 0f,
        -0.6f, -0.6f, 0f, 1f, 0f,
        0.6f, -0.6f, 0f, 0f, 1f,
    ];

    private static readonly VertexAttribute[] Attributes =
    [
        new() { Format = VertexFormat.Float32x2, Offset = 0, ShaderLocation = 0 },
        new() { Format = VertexFormat.Float32x3, Offset = 8, ShaderLocation = 1 },
    ];

    public static async Task<Result<TriangleRenderer>> CreateAsync(Instance instance, Surface surface, uint width, uint height, Action<string> log)
    {
        var adapterRequest = instance.RequestAdapterAsync(new RequestAdapterOptions { CompatibleSurface = surface });
        if ((await instance.ProcessEventsUntil(adapterRequest)).TryPickProblems(out var problems, out var adapter))
        {
            return problems.Prepend("Error while requesting a WebGPU adapter");
        }

        _ = adapter.GetInfo(out var info);
        log($"adapter: {info.Description} ({info.BackendType}, {info.AdapterType})");
        if (info.BackendType == BackendType.Null)
        {
            return new ResultProblem("Only Dawn's Null backend is available: no usable GPU driver (is the Vulkan loader installed?)");
        }

        var deviceRequest = adapter.RequestDeviceAsync(new DeviceDescriptor
        {
            DeviceLostCallbackInfo = new DeviceLostCallbackInfo
            {
                Mode = CallbackMode.AllowProcessEvents,
                Callback = (_, reason, message) => log($"WebGPU device lost ({reason}): {message}"),
            },
            UncapturedErrorCallbackInfo = new UncapturedErrorCallbackInfo
            {
                Callback = (_, type, message) => log($"WebGPU {type} error: {message}"),
            },
        });
        if ((await instance.ProcessEventsUntil(deviceRequest)).TryPickProblems(out problems, out var device))
        {
            return problems.Prepend("Error while requesting a WebGPU device");
        }

        _ = surface.GetCapabilities(adapter, out var capabilities);
        if (capabilities.Formats.Length == 0)
        {
            return new ResultProblem("The surface supports no formats: it is invalid for this adapter");
        }

        // The shader writes the colours as they should appear, so skip formats that would sRGB-encode them (Vulkan lists BGRA8UnormSrgb first)
        var format = capabilities.Formats.FirstOrDefault(f => !f.ToString().EndsWith("Srgb"), capabilities.Formats[0]);
        log($"surface formats: {string.Join(", ", capabilities.Formats)}; using {format}");
        surface.Configure(new SurfaceConfiguration { Device = device, Format = format, Width = width, Height = height });

        using var module = device.CreateShaderModule(new ShaderModuleDescriptor
        {
            Label = "triangle"u8,
            ShaderSourceWGSL = new ShaderSourceWGSL { Code = Shader },
        });
        var pipeline = device.CreateRenderPipeline(new RenderPipelineDescriptor
        {
            Label = "triangle"u8,
            Vertex = new VertexState
            {
                Module = module,
                EntryPoint = "vs"u8,
                Buffers = [new VertexBufferLayout { ArrayStride = 5 * sizeof(float), Attributes = Attributes }],
            },
            Fragment = new FragmentState
            {
                Module = module,
                EntryPoint = "fs"u8,
                Targets = [new ColorTargetState { Format = format }],
            },
        });

        var vertices = device.CreateBuffer(new BufferDescriptor
        {
            Label = "vertices"u8,
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
            Size = (ulong)(Vertices.Length * sizeof(float)),
        });
        var queue = device.GetQueue();
        queue.WriteBuffer(vertices, 0, Vertices);
        adapter.Dispose();

        return new TriangleRenderer(instance, surface, device, queue, pipeline, vertices);
    }

    public void DrawFrame()
    {
        instance.ProcessEvents();

        var surfaceTexture = surface.GetCurrentTexture();
        using var texture = surfaceTexture.Texture;
        using var view = texture.CreateView();
        using var encoder = device.CreateCommandEncoder();

        using (var pass = encoder.BeginRenderPass(new RenderPassDescriptor
               {
                   ColorAttachments =
                   [
                       new RenderPassColorAttachment
                       {
                           View = view,
                           LoadOp = LoadOp.Clear,
                           StoreOp = StoreOp.Store,
                           ClearValue = new Color { R = 0.1, G = 0.1, B = 0.15, A = 1 },
                       },
                   ],
               }))
        {
            pass.SetPipeline(pipeline);
            pass.SetVertexBuffer(0, vertices);
            pass.Draw(3);
            pass.End();
        }

        using var commands = encoder.Finish();
        queue.Submit([commands]);

        // The browser presents when the frame callback returns; emdawnwebgpu aborts on an explicit present
        if (!OperatingSystem.IsBrowser())
        {
            _ = surface.Present();
        }
    }
}
