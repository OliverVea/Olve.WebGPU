namespace Olve.WebGPU.Tests;

public class FutureTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Test]
    public async Task WaitAnyCompletesARequestWithoutProcessEvents()
    {
        using var instance = Wgpu.CreateInstance(new InstanceDescriptor { RequiredFeatures = [InstanceFeatureName.TimedWaitAny] });
        var request = instance.RequestAdapterAsync(new RequestAdapterOptions(), out var future);

        // The request returns as soon as it completes; the timeout only bounds a hang
        Span<FutureWaitInfo> futures = [new FutureWaitInfo { Future = future }];
        var status = instance.WaitAny(futures, (ulong)Timeout.TotalNanoseconds);
        var completed = futures[0].Completed;

        await Assert.That(status).IsEqualTo(WaitStatus.Success);
        await Assert.That(completed).IsTrue();
        await Assert.That(request.IsCompleted).IsTrue();
        (await request).Adapter.Dispose();
    }

    [Test]
    public async Task WaitAnyWithATimeoutNeedsTheTimedWaitAnyFeature()
    {
        using var instance = Wgpu.CreateInstance();
        var request = instance.RequestAdapterAsync(new RequestAdapterOptions(), out var future);

        Span<FutureWaitInfo> futures = [new FutureWaitInfo { Future = future }];
        var status = instance.WaitAny(futures, (ulong)Timeout.TotalNanoseconds);
        var completed = futures[0].Completed;

        await Assert.That(status).IsEqualTo(WaitStatus.Error);
        await Assert.That(completed).IsFalse();
        (await instance.ProcessEventsUntilAsync(request)).Adapter.Dispose();
    }

    [Test]
    public async Task WaitAnyWithNoTimeoutSeesAFinishedRequest()
    {
        var (status, completed) = await FinishedFuture.WaitAsync();

        await Assert.That(status).IsEqualTo(WaitStatus.Success);
        await Assert.That(completed).IsTrue();
    }
}
