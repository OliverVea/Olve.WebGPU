using Olve.WebGPU;
using Olve.WebGPU.Tests;

// The same checks as Olve.WebGPU.Tests, in the browser; the exit code becomes the page title (main.js)
var failures = 0;

var (count, mismatches) = NativeLayout.Check();
Console.WriteLine($"layout values checked: {count}, mismatches: {mismatches.Count}");
mismatches.ForEach(Console.WriteLine);
failures += mismatches.Count > 0 || count == 0 ? 1 : 0;

try
{
    var frame = await OffscreenTriangle.RenderAsync(Console.WriteLine);
    const uint centre = OffscreenTriangle.Size / 2;
    var corner = frame.Pixel(1, 1);
    var middle = frame.Pixel(centre, centre);
    Console.WriteLine($"readback on {frame.Adapter.Description} ({frame.Adapter.BackendType}): corner {string.Join(",", corner)}, centre {string.Join(",", middle)}");
    failures += corner.SequenceEqual(OffscreenTriangle.Clear) && middle.SequenceEqual(OffscreenTriangle.Fill) ? 0 : 1;
}
catch (InvalidOperationException e)
{
    Console.WriteLine(e.Message);
    failures++;
}

try
{
    var map = await AbortedMap.MapThenUnmapAsync(Console.WriteLine);
    Console.WriteLine($"map then unmap: {map.Status}: {map.Message}");
    failures += map.Status == MapAsyncStatus.Aborted && map.Message.Length > 0 ? 0 : 1;
}
catch (InvalidOperationException e)
{
    Console.WriteLine(e.Message);
    failures++;
}

var (waitStatus, waitCompleted) = await FinishedFuture.WaitAsync();
Console.WriteLine($"WaitAny on a finished request: {waitStatus}, completed {waitCompleted}");
failures += waitStatus == WaitStatus.Success && waitCompleted ? 0 : 1;

Console.WriteLine(failures == 0 ? "PASS" : $"FAIL ({failures})");
return failures;
