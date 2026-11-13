namespace Olve.WebGPU.Tests;

/// <summary>Out chains: the binding links an empty struct into an out struct, as C does, and the implementation fills both.</summary>
internal static class OutChains
{
    public static async Task<(Status Status, Limits Limits, CompatibilityModeLimits Compatibility)> AdapterLimitsAsync()
    {
        using var instance = Wgpu.CreateInstance();
        using var adapter = await OffscreenTriangle.RequestAdapterAsync(instance, _ => { });
        var status = adapter.GetLimits(out var limits, out var compatibility);
        return (status, limits, compatibility);
    }

    public static async Task<(Status Status, AdapterInfo Info, AdapterPropertiesSubgroupMatrixConfigs Configs, bool HasFeature)> SubgroupMatrixAsync()
    {
        using var instance = Wgpu.CreateInstance();
        using var adapter = await OffscreenTriangle.RequestAdapterAsync(instance, _ => { });
        var status = adapter.GetInfo(out var info, out var configs);
        return (status, info, configs, adapter.HasFeature(FeatureName.ChromiumExperimentalSubgroupMatrix));
    }
}
