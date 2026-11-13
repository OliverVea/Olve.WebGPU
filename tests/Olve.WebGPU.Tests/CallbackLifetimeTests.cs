using System.Runtime.CompilerServices;

namespace Olve.WebGPU.Tests;

public class CallbackLifetimeTests
{
    [Test]
    public async Task DeviceLostReleasesTheUncapturedErrorCallback()
    {
        using var instance = Wgpu.CreateInstance();
        using var adapter = await OffscreenTriangle.RequestAdapterAsync(instance, Console.WriteLine);
        var (request, callback) = RequestDevice(adapter);
        var deviceResult = await instance.ProcessEventsUntilAsync(request);
        await Assert.That(deviceResult.TryGetValue(out var device)).IsTrue().Because(deviceResult.Message);

        device.Destroy();
        device.Dispose();
        instance.ProcessEvents();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        await Assert.That(callback.IsAlive).IsFalse();
    }

    // Not inlined, so no local of the test keeps the callback alive
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Task<RequestDeviceResult> Request, WeakReference Callback) RequestDevice(Adapter adapter)
    {
        var errors = new List<string>();
        UncapturedErrorCallback callback = (_, _, message) => errors.Add(message);
        var request = adapter.RequestDeviceAsync(new DeviceDescriptor { UncapturedErrorCallbackInfo = new UncapturedErrorCallbackInfo { Callback = callback } });
        return (request, new WeakReference(callback));
    }
}
