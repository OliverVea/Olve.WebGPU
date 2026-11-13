using System.Runtime.InteropServices;
using Olve.WebGPU.Native;

namespace Olve.WebGPU.Tests;

/// <summary>
/// Dawn native's C++ DawnInstanceDescriptor against the binding's layout, from native/dawn_layout_check.cpp built by each platform's
/// C++ compiler. Dawn native only (desktop, Android): the browser build excludes this folder.
/// </summary>
internal static class DawnLayout
{
    [DllImport("webgpu_layout_check")]
    private static extern int dawn_layout_values(long[]? values);

    /// <summary>The mismatches, as "name: C# value, C++ value"; empty when the layouts agree.</summary>
    public static List<string> Check()
    {
        var native = new long[dawn_layout_values(null)];
        _ = dawn_layout_values(native);
        var pointer = IntPtr.Size;

        (string Name, long Value)[] managed =
        [
            ("sizeof(DawnInstanceDescriptor)", Marshal.SizeOf<WGPUDawnInstanceDescriptor>()),
            ("additionalRuntimeSearchPaths", Offset("AdditionalRuntimeSearchPaths")),
            ("platform", Offset("Platform")),
            ("backendValidationLevel", Offset("BackendValidationLevel")),
            ("beginCaptureOnStartup", Offset("BeginCaptureOnStartup")),
            ("loggingCallbackInfo", Offset("LoggingCallbackInfo")),
            ("sizeof(span<string_view>)", 2 * pointer),
            ("span data offset", 0),
            ("sizeof(string_view)", 2 * pointer),
            ("string_view data offset", DawnMarshalling.StringViewLengthFirst ? pointer : 0),
            ("sizeof(BackendValidationLevel)", sizeof(BackendValidationLevel)),
            ("sizeof(bool)", sizeof(byte)),
            ("BackendValidationLevel::Disabled", (long)BackendValidationLevel.Disabled),
        ];

        if (native.Length != managed.Length)
        {
            return [$"count: C# {managed.Length}, C++ {native.Length}"];
        }

        return managed.Zip(native).Where(p => p.First.Value != p.Second).Select(p => $"{p.First.Name}: C# {p.First.Value}, C++ {p.Second}").ToList();
    }

    private static long Offset(string field) => Marshal.OffsetOf<WGPUDawnInstanceDescriptor>(field).ToInt64();
}
