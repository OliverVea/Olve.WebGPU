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

try
{
    var output = await Flows.ComputeAndReadBackAsync();
    var ok = output.SequenceEqual(Flows.Doubled());
    Console.WriteLine($"compute and read back: {(ok ? "matches" : string.Join(",", output.Take(8)) + "...")}");
    failures += ok ? 0 : 1;

    const string label = "deliberately-invalid-buffer";
    var (scope, uncaptured) = await Flows.CatchValidationErrorAsync(label);
    var caught = scope.TryGetValue(out var type) && type == ErrorType.Validation && scope.Message.Contains(label) && uncaptured == 0;
    Console.WriteLine($"error scope: {scope.Status}, {type}, uncaptured {uncaptured}: {scope.Message}");
    failures += caught ? 0 : 1;

    var (calls, reason, futureStatus, futureCompleted) = await Flows.DestroyAsync();
    Console.WriteLine($"destroy: lost callback {calls}x ({reason}), lost future {futureStatus}, completed {futureCompleted}");
    failures += calls == 1 && reason == DeviceLostReason.Destroyed && futureStatus == WaitStatus.Success && futureCompleted ? 0 : 1;
}
catch (InvalidOperationException e)
{
    Console.WriteLine(e.Message);
    failures++;
}

Console.WriteLine(failures == 0 ? "PASS" : $"FAIL ({failures})");
return failures;
