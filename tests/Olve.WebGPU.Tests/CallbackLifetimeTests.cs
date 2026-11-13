using System.Runtime.CompilerServices;
using Olve.Results;

namespace Olve.WebGPU.Tests;

public class CallbackLifetimeTests
{
    [Test]
    public async Task DeviceLostReleasesTheUncapturedErrorCallback()
    {
        using var instance = Wgpu.CreateInstance();
        var adapterResult = await instance.ProcessEventsUntil(instance.RequestAdapterAsync(new RequestAdapterOptions { ForceFallbackAdapter = true }));
        if (adapterResult.TryPickProblems(out _, out var adapter))
        {
            adapterResult = await instance.ProcessEventsUntil(instance.RequestAdapterAsync());
        }

        await Assert.That(adapterResult.TryPickProblems(out _, out adapter)).IsFalse();
        var (request, callback) = RequestDevice(adapter);
        var deviceResult = await instance.ProcessEventsUntil(request);
        await Assert.That(deviceResult.TryPickProblems(out _, out var device)).IsFalse();

        device.Destroy();
        device.Dispose();
        instance.ProcessEvents();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        await Assert.That(callback.IsAlive).IsFalse();
        adapter.Dispose();
    }

    // Not inlined, so no local of the test keeps the callback alive
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Task<Result<Device>> Request, WeakReference Callback) RequestDevice(Adapter adapter)
    {
        var errors = new List<string>();
        UncapturedErrorCallback callback = (_, _, message) => errors.Add(message);
        var request = adapter.RequestDeviceAsync(new DeviceDescriptor { UncapturedErrorCallbackInfo = new UncapturedErrorCallbackInfo { Callback = callback } });
        return (request, new WeakReference(callback));
    }
}
