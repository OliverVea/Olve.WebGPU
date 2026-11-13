namespace Olve.WebGPU;

public readonly partial record struct Instance
{
    /// <summary>
    /// Requests complete inside <see cref="ProcessEvents"/> (CallbackMode.AllowProcessEvents), like promises in the browser's event loop.
    /// A frame loop that processes events completes them on its own; before there is one, this pumps until the task is done.
    /// Between pumps it yields: in the browser that hands control back to the JS event loop, which resolves the promises.
    /// </summary>
    public async Task<T> ProcessEventsUntil<T>(Task<T> task)
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
}
