namespace Olve.WebGPU.Tests;

/// <summary>A request's future, waited on after the request has finished: WaitAny with no timeout, which needs no instance feature.</summary>
internal static class FinishedFuture
{
    public static async Task<(WaitStatus Status, bool Completed)> WaitAsync()
    {
        using var instance = Wgpu.CreateInstance();
        var request = instance.RequestAdapterAsync(new RequestAdapterOptions(), out var future);
        var result = await instance.ProcessEventsUntilAsync(request);
        if (result.TryGetValue(out var adapter))
        {
            adapter.Dispose();
        }

        Span<FutureWaitInfo> futures = [new FutureWaitInfo { Future = future }];
        var status = instance.WaitAny(futures, 0);
        return (status, futures[0].Completed);
    }
}
