using Olve.Results;

namespace Olve.WebGPU.Tests;

public class ProcessEventsTests
{
    [Test]
    public async Task SynchronousPumpCompletesARequestOnThisThread()
    {
        using var instance = Wgpu.CreateInstance();

        // The fallback adapter covers machines without a GPU, such as CI runners
        var result = instance.ProcessEventsUntil(instance.RequestAdapterAsync(), TimeSpan.FromSeconds(30));
        if (result.TryPickProblems(out _, out var adapter))
        {
            result = instance.ProcessEventsUntil(instance.RequestAdapterAsync(new RequestAdapterOptions { ForceFallbackAdapter = true }), TimeSpan.FromSeconds(30));
        }

        await Assert.That(result.TryPickProblems(out var problems, out adapter)).IsFalse().Because(string.Join("; ", problems?.Select(p => p.ToBriefString()) ?? []));
        adapter.Dispose();
    }

    [Test]
    public async Task SynchronousPumpTimesOutWithAProblem()
    {
        using var instance = Wgpu.CreateInstance();
        var never = new TaskCompletionSource<Result>();

        var result = instance.ProcessEventsUntil(never.Task, TimeSpan.FromMilliseconds(10));

        await Assert.That(result.TryPickProblems(out _)).IsTrue();
    }
}
