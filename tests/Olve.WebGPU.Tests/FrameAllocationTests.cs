namespace Olve.WebGPU.Tests;

public class FrameAllocationTests
{
    private static ReadOnlySpan<byte> Shader => """
        @group(0) @binding(0) var<uniform> offset: vec4f;

        @vertex fn vs(@location(0) position: vec2f) -> @builtin(position) vec4f {
            return vec4f(position, 0.0, 1.0) + offset;
        }

        @fragment fn fs() -> @location(0) vec4f {
            return vec4f(1.0);
        }
        """u8;

    [Test]
    public async Task AFrameAllocatesNoManagedMemory()
    {
        using var instance = Wgpu.CreateInstance();
        using var adapter = await OffscreenTriangle.RequestAdapterAsync(instance, Console.WriteLine);
        var errors = new List<string>();
        var deviceResult = await instance.ProcessEventsUntilAsync(adapter.RequestDeviceAsync(new DeviceDescriptor
        {
            UncapturedErrorCallbackInfo = new UncapturedErrorCallbackInfo { Callback = (_, _, message) => errors.Add(message) },
        }));
        await Assert.That(deviceResult.TryGetValue(out var device)).IsTrue().Because(deviceResult.Message);

        using var queue = device.GetQueue();
        using var target = device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment,
            Size = new Extent3D { Width = 16, Height = 16 },
            Format = TextureFormat.RGBA8Unorm,
        });
        using var view = target.CreateView();
        using var module = device.CreateShaderModule(new ShaderModuleDescriptor { ShaderSourceWGSL = new ShaderSourceWGSL { Code = Shader } });
        using var layout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Vertex,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, HasDynamicOffset = true },
                },
            ],
        });
        using var pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [layout] });
        VertexAttribute[] attributes = [new() { Format = VertexFormat.Float32x2, ShaderLocation = 0 }];
        using var pipeline = device.CreateRenderPipeline(new RenderPipelineDescriptor
        {
            Layout = pipelineLayout,
            Vertex = new VertexState
            {
                Module = module,
                EntryPoint = "vs"u8,
                Buffers = [new VertexBufferLayout { ArrayStride = 2 * sizeof(float), Attributes = attributes }],
            },
            Fragment = new FragmentState
            {
                Module = module,
                EntryPoint = "fs"u8,
                Targets = [new ColorTargetState { Format = TextureFormat.RGBA8Unorm }],
            },
        });
        using var vertices = device.CreateBuffer(new BufferDescriptor { Usage = BufferUsage.Vertex | BufferUsage.CopyDst, Size = 64 });
        using var uniforms = device.CreateBuffer(new BufferDescriptor { Usage = BufferUsage.Uniform | BufferUsage.CopyDst, Size = 512 });
        using var bindGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = layout,
            Entries = [new BindGroupEntry { Binding = 0, Buffer = uniforms, Size = 16 }],
        });
        float[] offset = [0.1f, 0, 0, 0];

        void Frame()
        {
            queue.WriteBuffer(uniforms, 256, offset.AsSpan());
            using var encoder = device.CreateCommandEncoder(new CommandEncoderDescriptor { Label = "frame"u8 });
            using (var pass = encoder.BeginRenderPass(new RenderPassDescriptor
                   {
                       Label = "pass"u8,
                       ColorAttachments =
                       [
                           new RenderPassColorAttachment
                           {
                               View = view,
                               LoadOp = LoadOp.Clear,
                               StoreOp = StoreOp.Store,
                               ClearValue = new Color { R = 0, G = 0, B = 0, A = 1 },
                           },
                       ],
                   }))
            {
                pass.SetPipeline(pipeline);
                pass.SetVertexBuffer(0, vertices);
                pass.SetBindGroup(0, bindGroup, [256u]);
                pass.Draw(3);
                pass.End();
            }

            using var commands = encoder.Finish();
            queue.Submit([commands]);
            instance.ProcessEvents();
        }

        // The first frame sets up the thread's arena; the frames after it reuse everything.
        // Nothing awaits between the two reads, so the counter is this thread's.
        Frame();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            Frame();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(errors).IsEmpty();
        await Assert.That(allocated).IsEqualTo(0);
        device.Destroy();
        device.Dispose();
    }
}
