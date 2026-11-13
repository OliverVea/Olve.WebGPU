namespace Olve.WebGPU.Tests;

public class NativeTests
{
    [Test]
    public async Task StructLayoutsAndEnumValuesMatchTheHeader()
    {
        var (count, mismatches) = NativeLayout.Check();

        await Assert.That(mismatches).IsEmpty();
        await Assert.That(count).IsEqualTo(LayoutCheck.Values().Length);
    }

    [Test]
    public async Task DisposingADefaultHandleDoesNothing()
    {
        // wgpu*Release(NULL) crashes Dawn, so the test host only survives this if Dispose skips the call
        default(Instance).Dispose();
        default(Device).Dispose();
        default(Buffer).Dispose();

        await Assert.That(default(Buffer) == new Buffer(0)).IsTrue();
    }

    [Test]
    public async Task TriangleRendersOffscreenAndReadsBack()
    {
        var result = await OffscreenTriangle.RenderAsync(Console.WriteLine);
        if (result.TryPickProblems(out var problems, out var frame))
        {
            Assert.Fail(string.Join("\n", problems.Select(p => p.ToBriefString())));
            return;
        }

        Console.WriteLine($"rendered on {frame.Adapter.Description} ({frame.Adapter.BackendType}, {frame.Adapter.AdapterType})");
        const uint centre = OffscreenTriangle.Size / 2;
        await Assert.That(frame.Pixel(1, 1)).IsEquivalentTo(OffscreenTriangle.Clear);
        await Assert.That(frame.Pixel(centre, centre)).IsEquivalentTo(OffscreenTriangle.Fill);
    }
}
