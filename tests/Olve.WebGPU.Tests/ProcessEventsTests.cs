namespace Olve.WebGPU.Tests;

public class ProcessEventsTests
{
    [Test]
    public async Task SynchronousPumpCompletesARequestOnThisThread()
    {
        using var instance = Wgpu.CreateInstance();

        // The fallback adapter covers machines without a GPU, such as CI runners
        var completed = instance.TryProcessEventsUntil(instance.RequestAdapterAsync(), TimeSpan.FromSeconds(30), out var result);
        if (completed && !result.IsSuccess)
        {
            completed = instance.TryProcessEventsUntil(instance.RequestAdapterAsync(new RequestAdapterOptions { ForceFallbackAdapter = true }), TimeSpan.FromSeconds(30), out result);
        }

        await Assert.That(completed).IsTrue();
        await Assert.That(result.TryGetValue(out var adapter)).IsTrue().Because($"{result.Status}: {result.Message}");
        adapter.Dispose();
    }

    [Test]
    public async Task SynchronousPumpReturnsFalseOnceTheTimeoutPasses()
    {
        using var instance = Wgpu.CreateInstance();
        var never = new TaskCompletionSource<QueueWorkDoneResult>();

        var completed = instance.TryProcessEventsUntil(never.Task, TimeSpan.Zero, out var result);

        await Assert.That(completed).IsFalse();
        await Assert.That(result).IsEqualTo(default(QueueWorkDoneResult));
    }
}
