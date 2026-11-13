using System.Text;

namespace Olve.WebGPU.Tests;

public class ArenaTests
{
    [Test]
    public async Task LabelsLargerThanAChunkAndNestedCallsCopyCorrectly()
    {
        using var instance = Wgpu.CreateInstance();
        var adapterResult = await instance.ProcessEventsUntilAsync(instance.RequestAdapterAsync(new RequestAdapterOptions { ForceFallbackAdapter = true }));
        if (adapterResult.TryPickProblems(out _, out var adapter))
        {
            adapterResult = await instance.ProcessEventsUntilAsync(instance.RequestAdapterAsync());
        }

        await Assert.That(adapterResult.TryPickProblems(out _, out adapter)).IsFalse();

        // Dawn puts the descriptor's label in the validation message, so the messages show what the arena copied
        var outer = "outer-" + new string('o', 6000);
        var inner = "inner-" + new string('i', 9000);
        var messages = new List<string>();
        Device device = default;
        var deviceResult = await instance.ProcessEventsUntilAsync(adapter.RequestDeviceAsync(new DeviceDescriptor
        {
            UncapturedErrorCallbackInfo = new UncapturedErrorCallbackInfo
            {
                Callback = (_, _, message) =>
                {
                    messages.Add(message);
                    if (messages.Count == 1)
                    {
                        // A call into the binding from a callback, possibly during the outer call
                        device.CreateBuffer(InvalidBuffer(inner)).Dispose();
                    }
                },
            },
        }));
        await Assert.That(deviceResult.TryPickProblems(out _, out device)).IsFalse();

        device.CreateBuffer(InvalidBuffer(outer)).Dispose();
        for (var i = 0; i < 10 && messages.Count < 2; i++)
        {
            instance.ProcessEvents();
        }

        await Assert.That(messages).Count().IsEqualTo(2);
        await Assert.That(messages[0]).Contains(outer);
        await Assert.That(messages[1]).Contains(inner);

        // After the first call, the thread's arena is reused: a call with a descriptor allocates no managed memory
        var label = "label"u8.ToArray();
        device.CreateCommandEncoder(new CommandEncoderDescriptor { Label = label }).Dispose();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            device.CreateCommandEncoder(new CommandEncoderDescriptor { Label = label }).Dispose();
        }

        await Assert.That(GC.GetAllocatedBytesForCurrentThread() - before).IsEqualTo(0);
        device.Destroy();
        device.Dispose();
        adapter.Dispose();
    }

    // MapRead and MapWrite together are invalid
    private static BufferDescriptor InvalidBuffer(string label) => new()
    {
        Label = Encoding.UTF8.GetBytes(label),
        Usage = BufferUsage.MapRead | BufferUsage.MapWrite,
        Size = 4,
    };
}
