using Olve.Results;
using Olve.WebGPU.Tests;

// The same checks as Olve.WebGPU.Tests, in the browser; the exit code becomes the page title (main.js)
var failures = 0;

var (count, mismatches) = NativeLayout.Check();
Console.WriteLine($"layout values checked: {count}, mismatches: {mismatches.Count}");
mismatches.ForEach(Console.WriteLine);
failures += mismatches.Count > 0 || count == 0 ? 1 : 0;

var result = await OffscreenTriangle.RenderAsync(Console.WriteLine);
if (result.TryPickProblems(out var problems, out var frame))
{
    problems.ToList().ForEach(p => Console.WriteLine(p.ToBriefString()));
    failures++;
}
else
{
    const uint centre = OffscreenTriangle.Size / 2;
    var corner = frame.Pixel(1, 1);
    var middle = frame.Pixel(centre, centre);
    Console.WriteLine($"readback on {frame.Adapter.Description} ({frame.Adapter.BackendType}): corner {string.Join(",", corner)}, centre {string.Join(",", middle)}");
    failures += corner.SequenceEqual(OffscreenTriangle.Clear) && middle.SequenceEqual(OffscreenTriangle.Fill) ? 0 : 1;
}

Console.WriteLine(failures == 0 ? "PASS" : $"FAIL ({failures})");
return failures;
