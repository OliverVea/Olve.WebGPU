using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace Olve.WebGPU.Benchmarks;

/// <summary>
/// The calls a frame makes thousands of times, through the binding and through a plain DllImport of the same function:
/// the difference is the binding's own cost. Each invocation records <see cref="Calls"/> calls into a fresh render pass.
/// </summary>
[MemoryDiagnoser]
public class PassBenchmarks
{
    private const int Calls = 1000;

    private Gpu _gpu = null!;
    private CommandEncoder _encoder;
    private RenderPassEncoder _pass;

    [GlobalSetup]
    public void Setup() => _gpu = new Gpu();

    [GlobalCleanup]
    public void Cleanup() => _gpu.Dispose();

    [IterationSetup]
    public void BeginPass()
    {
        _encoder = _gpu.Device.CreateCommandEncoder();
        _pass = _gpu.BeginPass(_encoder);
        _pass.SetPipeline(_gpu.Pipeline);
        _pass.SetVertexBuffer(0, _gpu.Vertices);
        _pass.SetBindGroup(0, _gpu.BindGroup, [0u]);
    }

    [IterationCleanup]
    public void EndPass()
    {
        _pass.End();
        using var commands = _encoder.Finish();
        _pass.Dispose();
        _encoder.Dispose();
    }

    [Benchmark(OperationsPerInvoke = Calls)]
    public void Draw()
    {
        for (var i = 0; i < Calls; i++)
        {
            _pass.Draw(3);
        }
    }

    [Benchmark(OperationsPerInvoke = Calls, Baseline = true)]
    public void DrawDllImport()
    {
        for (var i = 0; i < Calls; i++)
        {
            Native.wgpuRenderPassEncoderDraw(_pass.Handle, 3, 1, 0, 0);
        }
    }

    [Benchmark(OperationsPerInvoke = Calls)]
    public void SetVertexBuffer()
    {
        for (var i = 0; i < Calls; i++)
        {
            _pass.SetVertexBuffer(0, _gpu.Vertices);
        }
    }

    [Benchmark(OperationsPerInvoke = Calls)]
    public void SetVertexBufferDllImport()
    {
        for (var i = 0; i < Calls; i++)
        {
            Native.wgpuRenderPassEncoderSetVertexBuffer(_pass.Handle, 0, _gpu.Vertices.Handle, 0, ulong.MaxValue);
        }
    }

    [Benchmark(OperationsPerInvoke = Calls)]
    public void SetBindGroupWithDynamicOffset()
    {
        for (var i = 0; i < Calls; i++)
        {
            _pass.SetBindGroup(0, _gpu.BindGroup, [256u]);
        }
    }

    [Benchmark(OperationsPerInvoke = Calls)]
    public void SetBindGroupWithDynamicOffsetDllImport()
    {
        var offsets = new uint[] { 256 };
        var handle = GCHandle.Alloc(offsets, GCHandleType.Pinned);
        for (var i = 0; i < Calls; i++)
        {
            Native.wgpuRenderPassEncoderSetBindGroup(_pass.Handle, 0, _gpu.BindGroup.Handle, 1, handle.AddrOfPinnedObject());
        }

        handle.Free();
    }

    // The same functions, bound without the binding: blittable arguments, no unsafe code
    private static class Native
    {
        [DllImport("webgpu_dawn")]
        public static extern void wgpuRenderPassEncoderDraw(nint pass, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);

        [DllImport("webgpu_dawn")]
        public static extern void wgpuRenderPassEncoderSetVertexBuffer(nint pass, uint slot, nint buffer, ulong offset, ulong size);

        [DllImport("webgpu_dawn")]
        public static extern void wgpuRenderPassEncoderSetBindGroup(nint pass, uint groupIndex, nint group, nuint dynamicOffsetCount, nint dynamicOffsets);
    }
}
