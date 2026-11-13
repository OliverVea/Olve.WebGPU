using BenchmarkDotNet.Attributes;

namespace Olve.WebGPU.Benchmarks;

/// <summary>Calls with descriptors (marshalled through the thread's arena), a queue write, a request, and a whole frame.</summary>
[MemoryDiagnoser]
public class DeviceBenchmarks
{
    private readonly float[] _offset = [0.1f, 0, 0, 0];
    private Gpu _gpu = null!;

    [GlobalSetup]
    public void Setup() => _gpu = new Gpu();

    [GlobalCleanup]
    public void Cleanup() => _gpu.Dispose();

    [Benchmark]
    public void CreateCommandEncoder() => _gpu.Device.CreateCommandEncoder(new CommandEncoderDescriptor { Label = "encoder"u8 }).Dispose();

    [Benchmark]
    public void CreateBindGroup() => _gpu.Device.CreateBindGroup(new BindGroupDescriptor
    {
        Layout = _gpu.Layout,
        Entries = [new BindGroupEntry { Binding = 0, Buffer = _gpu.Uniforms, Size = 16 }],
    }).Dispose();

    [Benchmark]
    public void BeginAndEndRenderPass()
    {
        using var encoder = _gpu.Device.CreateCommandEncoder();
        using var pass = _gpu.BeginPass(encoder);
        pass.End();
    }

    [Benchmark]
    public void WriteBuffer16Bytes() => _gpu.Queue.WriteBuffer(_gpu.Uniforms, 0, _offset.AsSpan());

    [Benchmark]
    public void ProcessEventsIdle() => _gpu.Instance.ProcessEvents();

    /// <summary>A frame: a render pass with 100 draws, a uniform write, a submit and ProcessEvents.</summary>
    [Benchmark]
    public void Frame()
    {
        _gpu.Queue.WriteBuffer(_gpu.Uniforms, 256, _offset.AsSpan());
        using var encoder = _gpu.Device.CreateCommandEncoder();
        using (var pass = _gpu.BeginPass(encoder))
        {
            pass.SetPipeline(_gpu.Pipeline);
            pass.SetVertexBuffer(0, _gpu.Vertices);
            pass.SetBindGroup(0, _gpu.BindGroup, [256u]);
            for (var i = 0; i < 100; i++)
            {
                pass.Draw(3);
            }

            pass.End();
        }

        using var commands = encoder.Finish();
        _gpu.Queue.Submit([commands]);
        _gpu.Instance.ProcessEvents();
    }

    /// <summary>A request round trip: OnSubmittedWorkDone, pumped until its task completes.</summary>
    [Benchmark]
    public QueueWorkDoneResult SubmittedWorkDone()
    {
        var done = _gpu.Queue.OnSubmittedWorkDoneAsync();
        while (!done.IsCompleted)
        {
            _gpu.Instance.ProcessEvents();
        }

        return done.Result;
    }
}
