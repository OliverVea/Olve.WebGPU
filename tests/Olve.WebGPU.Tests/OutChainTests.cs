namespace Olve.WebGPU.Tests;

public class OutChainTests
{
    [Test]
    public async Task GetLimitsAlsoFillsTheCompatibilityModeLimits()
    {
        var (status, limits, compatibility) = await OutChains.AdapterLimitsAsync();

        await Assert.That(status).IsEqualTo(Status.Success);
        await Assert.That(compatibility.MaxStorageBuffersInVertexStage).IsNotEqualTo(Wgpu.LimitU32Undefined);
        await Assert.That(compatibility.MaxStorageBuffersInFragmentStage).IsLessThanOrEqualTo(limits.MaxStorageBuffersPerShaderStage);
        await Assert.That(compatibility.MaxStorageTexturesInFragmentStage).IsLessThanOrEqualTo(limits.MaxStorageTexturesPerShaderStage);
    }

    // Without the feature, Dawn returns Error and emdawnwebgpu Success: either way there are no configs
    [Test]
    public async Task SubgroupMatrixConfigsComeWithTheirFeature()
    {
        var (status, _, configs, hasFeature) = await OutChains.SubgroupMatrixAsync();

        await Assert.That(configs.Configs.Length > 0).IsEqualTo(hasFeature);
        if (hasFeature)
        {
            await Assert.That(status).IsEqualTo(Status.Success);
        }
    }
}
