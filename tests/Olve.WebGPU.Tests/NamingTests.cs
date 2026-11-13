using Olve.WebGPU;
using Buffer = Olve.WebGPU.Buffer;

// A consumer's backend namespace ending in WebGPU must still reach the entry point by its plain name
namespace Consumer.Rendering.WebGPU;

public class NamingTests
{
    [Test]
    public async Task EntryPointResolvesInsideAWebGPUNamespace()
    {
        using var instance = Wgpu.CreateInstance();
        Buffer buffer = default;

        await Assert.That(instance.Handle).IsNotEqualTo(0);
        await Assert.That(buffer.Handle).IsEqualTo(0);
    }
}
