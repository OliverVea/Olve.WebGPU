using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;

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
    /// Pumps <see cref="ProcessEvents"/> on this thread until <paramref name="request"/> completes, and gives its result;
    /// false once <paramref name="timeout"/> passes. A request that times out stays pending and completes in a later <see cref="ProcessEvents"/>.
    /// Not in the browser: blocking the thread keeps the JS event loop from resolving the request.
    /// </summary>
    [UnsupportedOSPlatform("browser")]
    public bool TryProcessEventsUntil<T>(Task<T> request, TimeSpan timeout, [MaybeNullWhen(false)] out T result)
    {
        if (!Pump(request, timeout))
        {
            result = default;
            return false;
        }

        result = request.GetAwaiter().GetResult();
        return true;
    }

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
}
