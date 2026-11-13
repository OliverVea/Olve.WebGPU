namespace Olve.WebGPU.Tests;

public class FlowTests
{
    [Test]
    public async Task AComputeShaderWritesABufferThatMapsBack()
    {
        var output = await Flows.ComputeAndReadBackAsync();

        await Assert.That(output).IsEquivalentTo(Flows.Doubled());
    }

    [Test]
    public async Task AnErrorScopeCatchesAValidationErrorNamingTheLabel()
    {
        const string label = "deliberately-invalid-buffer";

        var (scope, uncaptured) = await Flows.CatchValidationErrorAsync(label);

        await Assert.That(scope.TryGetValue(out var type)).IsTrue().Because($"{scope.Status}: {scope.Message}");
        await Assert.That(type).IsEqualTo(ErrorType.Validation);
        await Assert.That(scope.Message).Contains(label);
        await Assert.That(uncaptured).IsEqualTo(0);
    }

    [Test]
    public async Task DestroyingADeviceCallsTheLostCallbackOnce()
    {
        var (calls, reason, futureStatus, futureCompleted) = await Flows.DestroyAsync();

        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(reason).IsEqualTo(DeviceLostReason.Destroyed);
        await Assert.That(futureStatus).IsEqualTo(WaitStatus.Success);
        await Assert.That(futureCompleted).IsTrue();
    }
}
