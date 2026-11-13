using Olve.Results;

namespace Olve.WebGPU.Tests;

/// <summary>Draws a triangle into an offscreen texture and reads it back: the whole path from instance to mapped buffer, without a window.</summary>
internal static class OffscreenTriangle
{
    public const uint Size = 64;

    // Rows of a texture-to-buffer copy are 256-byte aligned
    public const uint BytesPerRow = 256;

    public static readonly byte[] Clear = [0, 0, 255, 255];
    public static readonly byte[] Fill = [51, 204, 102, 255];

    private static ReadOnlySpan<byte> Shader => """
        @vertex fn vs(@location(0) position: vec2f) -> @builtin(position) vec4f {
            return vec4f(position, 0.0, 1.0);
        }

        @fragment fn fs() -> @location(0) vec4f {
            return vec4f(0.2, 0.8, 0.4, 1.0);
        }
        """u8;

    private static readonly float[] Vertices = [0f, 0.8f, -0.8f, -0.8f, 0.8f, -0.8f];

    private static readonly VertexAttribute[] Attributes = [new() { Format = VertexFormat.Float32x2, ShaderLocation = 0 }];

    public sealed record Frame(AdapterInfo Adapter, byte[] Pixels)
    {
        public byte[] Pixel(uint x, uint y) => Pixels.AsSpan((int)(y * BytesPerRow + x * 4), 4).ToArray();
    }

    public static async Task<Result<Frame>> RenderAsync(Action<string> log)
    {
        var instance = Wgpu.CreateInstance();
        if ((await RequestAdapterAsync(instance, log)).TryPickProblems(out var problems, out var adapter))
        {
            return problems;
        }

        _ = adapter.GetInfo(out var info);
        var deviceRequest = adapter.RequestDeviceAsync(new DeviceDescriptor
        {
            UncapturedErrorCallbackInfo = new UncapturedErrorCallbackInfo
            {
                Callback = (_, type, message) => log($"WebGPU {type} error: {message}"),
            },
        });
        if ((await instance.ProcessEventsUntil(deviceRequest)).TryPickProblems(out problems, out var device))
        {
            return problems.Prepend("Error while requesting a WebGPU device");
        }

        var queue = device.GetQueue();
        using var target = device.CreateTexture(new TextureDescriptor
        {
            Label = "target"u8,
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc,
            Size = new Extent3D { Width = Size, Height = Size },
            Format = TextureFormat.RGBA8Unorm,
        });
        using var view = target.CreateView();
        using var module = device.CreateShaderModule(new ShaderModuleDescriptor
        {
            Label = "triangle"u8,
            ShaderSourceWGSL = new ShaderSourceWGSL { Code = Shader },
        });
        using var pipeline = device.CreateRenderPipeline(new RenderPipelineDescriptor
        {
            Vertex = new VertexState
            {
                Module = module,
                EntryPoint = "vs"u8,
                Buffers = [new VertexBufferLayout { ArrayStride = 2 * sizeof(float), Attributes = Attributes }],
            },
            Fragment = new FragmentState
            {
                Module = module,
                EntryPoint = "fs"u8,
                Targets = [new ColorTargetState { Format = TextureFormat.RGBA8Unorm }],
            },
        });

        using var vertices = device.CreateBuffer(new BufferDescriptor
        {
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
            Size = (ulong)(Vertices.Length * sizeof(float)),
        });
        queue.WriteBuffer(vertices, 0, Vertices.AsSpan());

        using var readback = device.CreateBuffer(new BufferDescriptor
        {
            Usage = BufferUsage.CopyDst | BufferUsage.MapRead,
            Size = BytesPerRow * Size,
        });

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
                           ClearValue = new Color { R = 0, G = 0, B = 1, A = 1 },
                       },
                   ],
               }))
        {
            pass.SetPipeline(pipeline);
            pass.SetVertexBuffer(0, vertices);
            pass.Draw(3);
            pass.End();
        }

        encoder.CopyTextureToBuffer(
            new TexelCopyTextureInfo { Texture = target },
            new TexelCopyBufferInfo { Buffer = readback, Layout = new TexelCopyBufferLayout { BytesPerRow = BytesPerRow, RowsPerImage = Size } },
            new Extent3D { Width = Size, Height = Size });
        using var commands = encoder.Finish();
        queue.Submit([commands]);

        var size = (nuint)(BytesPerRow * Size);
        if ((await instance.ProcessEventsUntil(readback.MapAsync(MapMode.Read, 0, size))).TryPickProblems(out problems))
        {
            return problems.Prepend("Error while mapping the readback buffer");
        }

        // webgpu.h's WGPU_WHOLE_MAP_SIZE: the rest of the buffer
        var pixels = readback.GetConstMappedRange(0, nuint.MaxValue).ToArray();
        readback.Unmap();

        // An unmapped buffer has no mapped range: webgpu.h returns null, which is an empty span
        if (!readback.GetConstMappedRange(0, size).IsEmpty)
        {
            return new ResultProblem("GetConstMappedRange on an unmapped buffer returned a non-empty span");
        }

        queue.Dispose();
        device.Destroy();
        device.Dispose();
        adapter.Dispose();
        instance.Dispose();
        return new Frame(info, pixels);
    }

    /// <summary>The default adapter, or the fallback (software) one where there is no GPU, such as a CI runner.</summary>
    private static async Task<Result<Adapter>> RequestAdapterAsync(Instance instance, Action<string> log)
    {
        foreach (var fallback in new[] { false, true })
        {
            var kind = fallback ? "fallback" : "default";
            var request = instance.RequestAdapterAsync(new RequestAdapterOptions { ForceFallbackAdapter = fallback });
            if ((await instance.ProcessEventsUntil(request)).TryPickProblems(out var problems, out var adapter))
            {
                log($"no {kind} adapter: {string.Join("; ", problems.Select(p => p.ToBriefString()))}");
                continue;
            }

            _ = adapter.GetInfo(out var info);
            log($"{kind} adapter: {info.Description} ({info.BackendType}, {info.AdapterType}, vendor {info.Vendor})");

            // Dawn's Null backend accepts everything and renders nothing
            if (info.BackendType == BackendType.Null)
            {
                adapter.Dispose();
                continue;
            }

            return adapter;
        }

        return new ResultProblem("No WebGPU adapter other than the Null backend: is a GPU driver (or the Vulkan loader and lavapipe) installed?");
    }
}
