using Olve.WebGPU.Tests;

namespace Olve.WebGPU.Benchmarks;

/// <summary>A device with what a frame needs: a render target, a pipeline with a dynamic-offset bind group, and buffers.</summary>
public sealed class Gpu : IDisposable
{
    private static ReadOnlySpan<byte> Shader => """
        @group(0) @binding(0) var<uniform> offset: vec4f;
        @vertex fn vs(@location(0) p: vec2f) -> @builtin(position) vec4f { return vec4f(p, 0.0, 1.0) + offset; }
        @fragment fn fs() -> @location(0) vec4f { return vec4f(1.0); }
        """u8;

    public Instance Instance { get; }
    public Adapter Adapter { get; }
    public Device Device { get; }
    public Queue Queue { get; }
    public Texture Target { get; }
    public TextureView View { get; }
    public BindGroupLayout Layout { get; }
    public RenderPipeline Pipeline { get; }
    public Buffer Vertices { get; }
    public Buffer Uniforms { get; }
    public BindGroup BindGroup { get; }

    public Gpu()
    {
        Instance = Wgpu.CreateInstance();
        Adapter = OffscreenTriangle.RequestAdapterAsync(Instance, _ => { }).GetAwaiter().GetResult();
        if (!Instance.TryProcessEventsUntil(Adapter.RequestDeviceAsync(), TimeSpan.FromSeconds(30), out var result) || !result.TryGetValue(out var device))
        {
            throw new InvalidOperationException($"No WebGPU device: {result.Status}, {result.Message}");
        }

        Device = device;
        Queue = device.GetQueue();
        Target = device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment,
            Size = new Extent3D { Width = 64, Height = 64 },
            Format = TextureFormat.RGBA8Unorm,
        });
        View = Target.CreateView();
        using var module = device.CreateShaderModule(new ShaderModuleDescriptor { ShaderSourceWGSL = new ShaderSourceWGSL { Code = Shader } });
        Layout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
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
        using var pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [Layout] });
        VertexAttribute[] attributes = [new() { Format = VertexFormat.Float32x2, ShaderLocation = 0 }];
        Pipeline = device.CreateRenderPipeline(new RenderPipelineDescriptor
        {
            Layout = pipelineLayout,
            Vertex = new VertexState { Module = module, EntryPoint = "vs"u8, Buffers = [new VertexBufferLayout { ArrayStride = 8, Attributes = attributes }] },
            Fragment = new FragmentState { Module = module, EntryPoint = "fs"u8, Targets = [new ColorTargetState { Format = TextureFormat.RGBA8Unorm }] },
        });
        Vertices = device.CreateBuffer(new BufferDescriptor { Usage = BufferUsage.Vertex | BufferUsage.CopyDst, Size = 1024 });
        Uniforms = device.CreateBuffer(new BufferDescriptor { Usage = BufferUsage.Uniform | BufferUsage.CopyDst, Size = 512 });
        BindGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = Layout,
            Entries = [new BindGroupEntry { Binding = 0, Buffer = Uniforms, Size = 16 }],
        });
    }

    public RenderPassEncoder BeginPass(CommandEncoder encoder) => encoder.BeginRenderPass(new RenderPassDescriptor
    {
        ColorAttachments = [new RenderPassColorAttachment { View = View, LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store }],
    });

    public void Dispose()
    {
        BindGroup.Dispose();
        Uniforms.Dispose();
        Vertices.Dispose();
        Pipeline.Dispose();
        Layout.Dispose();
        View.Dispose();
        Target.Dispose();
        Queue.Dispose();
        Device.Destroy();
        Device.Dispose();
        Adapter.Dispose();
        Instance.Dispose();
    }
}
