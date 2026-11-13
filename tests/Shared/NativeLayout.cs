using System.Runtime.InteropServices;

namespace Olve.WebGPU.Tests;

/// <summary>Compares <see cref="LayoutCheck"/> with the same values from the C compiler, built against the implementation's webgpu.h.</summary>
internal static class NativeLayout
{
    // Fills values (when not null) and returns how many there are; native/webgpu_layout_check.c
    [DllImport("webgpu_layout_check")]
    private static extern int wgpu_layout_values(long[]? values);

    public static (int Checked, List<string> Mismatches) Check()
    {
        var managed = LayoutCheck.Values();
        var native = new long[wgpu_layout_values(null)];
        _ = wgpu_layout_values(native);

        if (native.Length != managed.Length)
        {
            return (0, [$"count: C# {managed.Length}, C {native.Length}"]);
        }

        var mismatches = new List<string>();
        for (var i = 0; i < managed.Length; i++)
        {
            if (managed[i].Value != native[i])
            {
                mismatches.Add($"{managed[i].Name}: C# {managed[i].Value}, C {native[i]}");
            }
        }

        return (managed.Length, mismatches);
    }
}
