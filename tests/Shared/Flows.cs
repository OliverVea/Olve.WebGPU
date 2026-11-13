using System.Runtime.InteropServices;

namespace Olve.WebGPU.Tests;

/// <summary>
/// Whole paths through the binding, shared by the desktop tests and the browser tests.
/// Each returns what it observed; the callers assert. A failed setup step throws <see cref="InvalidOperationException"/>.
/// </summary>
internal static class Flows
{
    private const int Count = 64;

    private static ReadOnlySpan<byte> Doubler => """
        @group(0) @binding(0) var<storage, read_write> values: array<u32>;

        @compute @workgroup_size(64) fn main(@builtin(global_invocation_id) id: vec3u) {
            values[id.x] = values[id.x] * 2u + 1u;
        }
        """u8;

    /// <summary>A compute shader writes a storage buffer, which is copied to a mappable buffer and read back.</summary>
    public static async Task<uint[]> ComputeAndReadBackAsync()
    {
        using var instance = Wgpu.CreateInstance();
        using var adapter = await OffscreenTriangle.RequestAdapterAsync(instance, _ => { });
        var device = await DeviceAsync(instance, adapter.RequestDeviceAsync());
        using var queue = device.GetQueue();

        var input = new uint[Count];
        for (var i = 0; i < Count; i++)
        {
            input[i] = (uint)i;
        }

        const ulong size = Count * sizeof(uint);
        using var storage = device.CreateBuffer(new BufferDescriptor { Usage = BufferUsage.Storage | BufferUsage.CopySrc | BufferUsage.CopyDst, Size = size });
        using var readback = device.CreateBuffer(new BufferDescriptor { Usage = BufferUsage.MapRead | BufferUsage.CopyDst, Size = size });
        queue.WriteBuffer(storage, 0, input.AsSpan());

        using var module = device.CreateShaderModule(new ShaderModuleDescriptor { ShaderSourceWGSL = new ShaderSourceWGSL { Code = Doubler } });
        using var pipeline = device.CreateComputePipeline(new ComputePipelineDescriptor { Compute = new ComputeState { Module = module, EntryPoint = "main"u8 } });
        using var layout = pipeline.GetBindGroupLayout(0);
        using var bindGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = layout,
            Entries = [new BindGroupEntry { Binding = 0, Buffer = storage }],
        });

        using var encoder = device.CreateCommandEncoder();
        using (var pass = encoder.BeginComputePass())
        {
            pass.SetPipeline(pipeline);
            pass.SetBindGroup(0, bindGroup);
            pass.DispatchWorkgroups(1);
            pass.End();
        }

        encoder.CopyBufferToBuffer(storage, 0, readback, 0, size);
        using var commands = encoder.Finish();
        queue.Submit([commands]);

        var mapped = await instance.ProcessEventsUntilAsync(readback.MapAsync(MapMode.Read, 0, (nuint)size));
        if (!mapped.IsSuccess)
        {
            throw new InvalidOperationException($"Error while mapping the readback buffer ({mapped.Status}): {mapped.Message}");
        }

        var output = MemoryMarshal.Cast<byte, uint>(readback.GetConstMappedRange(0, (nuint)size)).ToArray();
        readback.Unmap();
        device.Destroy();
        device.Dispose();
        return output;
    }

    /// <summary>The expected output of <see cref="ComputeAndReadBackAsync"/>.</summary>
    public static uint[] Doubled() => Enumerable.Range(0, Count).Select(i => (uint)i * 2 + 1).ToArray();

    /// <summary>An error scope catches a validation error, whose message names the descriptor's label; the uncaptured error callback isn't called.</summary>
    public static async Task<(PopErrorScopeResult Scope, int Uncaptured)> CatchValidationErrorAsync(string label)
    {
        using var instance = Wgpu.CreateInstance();
        using var adapter = await OffscreenTriangle.RequestAdapterAsync(instance, _ => { });
        var uncaptured = 0;
        var device = await DeviceAsync(instance, adapter.RequestDeviceAsync(new DeviceDescriptor
        {
            UncapturedErrorCallbackInfo = new UncapturedErrorCallbackInfo { Callback = (_, _, _) => uncaptured++ },
        }));

        device.PushErrorScope(ErrorFilter.Validation);

        // MapRead and MapWrite together are invalid
        device.CreateBuffer(new BufferDescriptor
        {
            Label = System.Text.Encoding.UTF8.GetBytes(label),
            Usage = BufferUsage.MapRead | BufferUsage.MapWrite,
            Size = 4,
        }).Dispose();
        var scope = await instance.ProcessEventsUntilAsync(device.PopErrorScopeAsync());

        device.Destroy();
        device.Dispose();
        return (scope, uncaptured);
    }

    /// <summary>Destroying a device calls its lost callback once, and completes its lost future.</summary>
    public static async Task<(int Calls, DeviceLostReason Reason, WaitStatus FutureStatus, bool FutureCompleted)> DestroyAsync()
    {
        using var instance = Wgpu.CreateInstance();
        using var adapter = await OffscreenTriangle.RequestAdapterAsync(instance, _ => { });
        var calls = 0;
        var lost = new TaskCompletionSource<DeviceLostReason>(TaskCreationOptions.RunContinuationsAsynchronously);
        var device = await DeviceAsync(instance, adapter.RequestDeviceAsync(new DeviceDescriptor
        {
            DeviceLostCallbackInfo = new DeviceLostCallbackInfo
            {
                Mode = CallbackMode.AllowProcessEvents,
                Callback = (_, reason, _) =>
                {
                    calls++;
                    lost.TrySetResult(reason);
                },
            },
        }));

        var future = device.GetLostFuture();
        device.Destroy();
        var reason = await instance.ProcessEventsUntilAsync(lost.Task);

        // More events don't call it again
        instance.ProcessEvents();
        instance.ProcessEvents();
        Span<FutureWaitInfo> futures = [new FutureWaitInfo { Future = future }];
        var status = instance.WaitAny(futures, 0);
        var completed = futures[0].Completed;

        device.Dispose();
        return (calls, reason, status, completed);
    }

    // Takes the request, not the descriptor: a descriptor is a ref struct, which an async method can't take
    private static async Task<Device> DeviceAsync(Instance instance, Task<RequestDeviceResult> request)
    {
        var result = await instance.ProcessEventsUntilAsync(request);
        return result.TryGetValue(out var device)
            ? device
            : throw new InvalidOperationException($"Error while requesting a WebGPU device ({result.Status}): {result.Message}");
    }
}
