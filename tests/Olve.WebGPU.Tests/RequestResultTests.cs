namespace Olve.WebGPU.Tests;

public class RequestResultTests
{
    [Test]
    public async Task AFailedRequestGivesItsOwnStatusAndMessage()
    {
        var result = await AbortedMap.MapThenUnmapAsync(Console.WriteLine);

        await Assert.That(result.Status).IsEqualTo(MapAsyncStatus.Aborted);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Message).IsNotEmpty();
    }
}
