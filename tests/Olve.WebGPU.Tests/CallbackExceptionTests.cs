using System.Diagnostics;

namespace Olve.WebGPU.Tests;

public class CallbackExceptionTests
{
    [Test]
    public async Task AnExceptionInACallbackEndsTheProcessWithIt()
    {
        // The process ends, so the throwing test runs in a child process: this test assembly, filtered to that one test
        var host = Environment.ProcessPath!;
        var start = new ProcessStartInfo(host) { RedirectStandardError = true, RedirectStandardOutput = true };
        if (Path.GetFileNameWithoutExtension(host) == "dotnet")
        {
            start.ArgumentList.Add(typeof(CallbackExceptionTests).Assembly.Location);
        }

        start.ArgumentList.Add("--treenode-filter");
        start.ArgumentList.Add($"/*/*/{nameof(CallbackExceptionTests)}/{nameof(ThrowsInTheUncapturedErrorCallback)}");

        using var child = Process.Start(start)!;
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await child.WaitForExitAsync(timeout.Token);
        var stderr = await error;
        Console.WriteLine(await output);
        Console.WriteLine(stderr);

        await Assert.That(child.ExitCode).IsNotEqualTo(0);
        await Assert.That(stderr).Contains("Olve.WebGPU: an exception was thrown in UncapturedErrorCallback. Callbacks are called from native code and must not throw.");
        await Assert.That(stderr).Contains("thrown by the callback");
    }

    /// <summary>Only runs filtered, in the child process of <see cref="AnExceptionInACallbackEndsTheProcessWithIt"/>: it ends the process.</summary>
    [Test]
    [Explicit]
    public async Task ThrowsInTheUncapturedErrorCallback()
    {
        using var instance = Wgpu.CreateInstance();
        using var adapter = await OffscreenTriangle.RequestAdapterAsync(instance, Console.WriteLine);
        var deviceResult = await instance.ProcessEventsUntilAsync(adapter.RequestDeviceAsync(new DeviceDescriptor
        {
            UncapturedErrorCallbackInfo = new UncapturedErrorCallbackInfo
            {
                Callback = (_, _, _) => throw new InvalidOperationException("thrown by the callback"),
            },
        }));
        await Assert.That(deviceResult.TryGetValue(out var device)).IsTrue().Because(deviceResult.Message);

        // MapRead and MapWrite together are invalid, so Dawn calls the uncaptured error callback
        device.CreateBuffer(new BufferDescriptor { Usage = BufferUsage.MapRead | BufferUsage.MapWrite, Size = 4 }).Dispose();
        for (var i = 0; i < 10; i++)
        {
            instance.ProcessEvents();
        }
    }
}
