using System.Diagnostics;
using System.Runtime.Versioning;
using Olve.Results;

namespace Olve.WebGPU;

public readonly partial record struct Instance
{
    /// <summary>
    /// Requests complete inside <see cref="ProcessEvents"/> (CallbackMode.AllowProcessEvents), like promises in the browser's event loop.
    /// A frame loop that processes events completes them on its own; before there is one, this pumps until the task is done.
    /// Between pumps it yields: in the browser that hands control back to the JS event loop, which resolves the promises.
    /// </summary>
    public async Task<T> ProcessEventsUntilAsync<T>(Task<T> task)
    {
        while (!task.IsCompleted)
        {
            ProcessEvents();
            if (!task.IsCompleted)
            {
                await Task.Delay(1);
            }
        }

        return await task;
    }

    /// <summary>
    /// Pumps <see cref="ProcessEvents"/> on this thread until the request completes, or fails once <paramref name="timeout"/> passes.
    /// A request that times out stays pending and completes in a later <see cref="ProcessEvents"/>.
    /// Not in the browser: blocking the thread keeps the JS event loop from resolving the request.
    /// </summary>
    [UnsupportedOSPlatform("browser")]
    public Result<T> ProcessEventsUntil<T>(Task<Result<T>> request, TimeSpan timeout) =>
        Pump(request, timeout) ? request.GetAwaiter().GetResult() : TimedOut(timeout);

    /// <inheritdoc cref="ProcessEventsUntil{T}(Task{Result{T}}, TimeSpan)"/>
    [UnsupportedOSPlatform("browser")]
    public Result ProcessEventsUntil(Task<Result> request, TimeSpan timeout) =>
        Pump(request, timeout) ? request.GetAwaiter().GetResult() : TimedOut(timeout);

    private bool Pump(Task request, TimeSpan timeout)
    {
        var start = Stopwatch.GetTimestamp();
        while (!request.IsCompleted)
        {
            ProcessEvents();
            if (request.IsCompleted)
            {
                break;
            }

            if (Stopwatch.GetElapsedTime(start) >= timeout)
            {
                return false;
            }

            Thread.Yield();
        }

        return true;
    }

    private static ResultProblem TimedOut(TimeSpan timeout) => new("the request didn't complete within {0}", timeout);
}
