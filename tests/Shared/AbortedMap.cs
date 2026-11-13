namespace Olve.WebGPU.Tests;

/// <summary>A request that fails with a status of its own: unmapping a buffer before its map completes aborts the map.</summary>
internal static class AbortedMap
{
    public static async Task<BufferMapResult> MapThenUnmapAsync(Action<string> log)
    {
        using var instance = Wgpu.CreateInstance();
        using var adapter = await OffscreenTriangle.RequestAdapterAsync(instance, log);
        var deviceResult = await instance.ProcessEventsUntilAsync(adapter.RequestDeviceAsync());
        if (!deviceResult.TryGetValue(out var device))
        {
            throw new InvalidOperationException($"Error while requesting a WebGPU device ({deviceResult.Status}): {deviceResult.Message}");
        }

        using var buffer = device.CreateBuffer(new BufferDescriptor { Usage = BufferUsage.MapRead | BufferUsage.CopyDst, Size = 16 });
        var map = buffer.MapAsync(MapMode.Read, 0, 16);
        buffer.Unmap();
        var result = await instance.ProcessEventsUntilAsync(map);

        device.Destroy();
        device.Dispose();
        return result;
    }
}
