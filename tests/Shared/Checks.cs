namespace Olve.WebGPU.Tests;

/// <summary>
/// The checks a runner without a test framework runs: the browser and Android runners call this, so they check the same things.
/// Returns the number of failed checks; logs each check's result.
/// </summary>
internal static class Checks
{
    /// <param name="dawnLayout">Dawn native (Android): DawnLayout.Check, which the browser build doesn't have.</param>
    public static async Task<int> RunAsync(Action<string> log, Func<List<string>>? dawnLayout)
    {
        var failures = 0;

        if (dawnLayout is not null)
        {
            var dawn = dawnLayout();
            log($"DawnInstanceDescriptor layout: {(dawn.Count == 0 ? "matches C++" : string.Join("; ", dawn))}");
            failures += dawn.Count == 0 ? 0 : 1;
        }

        var (count, mismatches) = NativeLayout.Check();
        log($"layout values checked: {count}, mismatches: {mismatches.Count}");
        mismatches.ForEach(log);
        failures += mismatches.Count > 0 || count == 0 ? 1 : 0;

        var marshal = MarshalCheck.Run();
        log($"marshalled values checked: {marshal.Checked}, mismatches: {marshal.Mismatches.Count}");
        marshal.Mismatches.ForEach(log);
        failures += marshal.Mismatches.Count > 0 || marshal.Checked == 0 ? 1 : 0;

        try
        {
            var frame = await OffscreenTriangle.RenderAsync(log);
            const uint centre = OffscreenTriangle.Size / 2;
            var corner = frame.Pixel(1, 1);
            var middle = frame.Pixel(centre, centre);
            log($"readback on {frame.Adapter.Description} ({frame.Adapter.BackendType}): corner {string.Join(",", corner)}, centre {string.Join(",", middle)}");
            failures += corner.SequenceEqual(OffscreenTriangle.Clear) && middle.SequenceEqual(OffscreenTriangle.Fill) ? 0 : 1;
        }
        catch (InvalidOperationException e)
        {
            log(e.Message);
            failures++;
        }

        try
        {
            var map = await AbortedMap.MapThenUnmapAsync(log);
            log($"map then unmap: {map.Status}: {map.Message}");
            failures += map.Status == MapAsyncStatus.Aborted && map.Message.Length > 0 ? 0 : 1;
        }
        catch (InvalidOperationException e)
        {
            log(e.Message);
            failures++;
        }

        var (waitStatus, waitCompleted) = await FinishedFuture.WaitAsync();
        log($"WaitAny on a finished request: {waitStatus}, completed {waitCompleted}");
        failures += waitStatus == WaitStatus.Success && waitCompleted ? 0 : 1;

        try
        {
            var output = await Flows.ComputeAndReadBackAsync();
            var ok = output.SequenceEqual(Flows.Doubled());
            log($"compute and read back: {(ok ? "matches" : string.Join(",", output.Take(8)) + "...")}");
            failures += ok ? 0 : 1;

            const string label = "deliberately-invalid-buffer";
            var (scope, uncaptured) = await Flows.CatchValidationErrorAsync(label);
            var caught = scope.TryGetValue(out var type) && type == ErrorType.Validation && scope.Message.Contains(label) && uncaptured == 0;
            log($"error scope: {scope.Status}, {type}, uncaptured {uncaptured}: {scope.Message}");
            failures += caught ? 0 : 1;

            var (calls, reason, futureStatus, futureCompleted) = await Flows.DestroyAsync();
            log($"destroy: lost callback {calls}x ({reason}), lost future {futureStatus}, completed {futureCompleted}");
            failures += calls == 1 && reason == DeviceLostReason.Destroyed && futureStatus == WaitStatus.Success && futureCompleted ? 0 : 1;
        }
        catch (InvalidOperationException e)
        {
            log(e.Message);
            failures++;
        }

        var (limitsStatus, limits, compatibility) = await OutChains.AdapterLimitsAsync();
        log($"GetLimits with CompatibilityModeLimits: {limitsStatus}, {compatibility}");
        failures += limitsStatus == Status.Success && compatibility.MaxStorageBuffersInVertexStage != Wgpu.LimitU32Undefined
            && compatibility.MaxStorageBuffersInFragmentStage <= limits.MaxStorageBuffersPerShaderStage ? 0 : 1;

        var (infoStatus, _, configs, hasSubgroupMatrix) = await OutChains.SubgroupMatrixAsync();
        log($"GetInfo with AdapterPropertiesSubgroupMatrixConfigs: {infoStatus}, feature {hasSubgroupMatrix}, {configs.Configs.Length} configs");
        failures += configs.Configs.Length > 0 == hasSubgroupMatrix && (!hasSubgroupMatrix || infoStatus == Status.Success) ? 0 : 1;

        return failures;
    }
}
