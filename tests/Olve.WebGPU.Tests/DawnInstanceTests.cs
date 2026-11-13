namespace Olve.WebGPU.Tests;

public class DawnInstanceTests
{
    [Test]
    public async Task DawnInstanceDescriptorMatchesTheCppLayout()
    {
        await Assert.That(DawnLayout.Check()).IsEmpty();
    }

    [Test]
    public async Task DawnsInstanceLogGoesToTheLoggingCallback()
    {
        var messages = new List<(LoggingType Type, string Message)>();
        using var instance = Wgpu.CreateInstance(new InstanceDescriptor(), new DawnInstanceDescriptor
        {
            LoggingCallback = (type, message) =>
            {
                lock (messages)
                {
                    messages.Add((type, message));
                }
            },
        });

        // Without the TimedWaitAny feature, Dawn logs a timed wait as an error on the instance's log
        var status = instance.WaitAny([], 1);

        await Assert.That(status).IsEqualTo(WaitStatus.Error);
        await Assert.That(messages).IsEquivalentTo([(LoggingType.Error, "Timeout waits are either not enabled or not supported.")]);
    }

}
