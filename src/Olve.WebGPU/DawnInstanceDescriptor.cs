using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Olve.WebGPU.Native;
using static Olve.WebGPU.Native.Interop;

namespace Olve.WebGPU;

/// <summary>Dawn's <c>dawn::native::BackendValidationLevel</c>: how much the backend API's own validation layers check.</summary>
public enum BackendValidationLevel
{
    Full = 0,
    Partial = 1,
    Disabled = 2,
}

/// <summary>Dawn's <c>WGPULoggingType</c>.</summary>
public enum LoggingType : uint
{
    Verbose = 1,
    Info = 2,
    Warning = 3,
    Error = 4,
}

/// <summary>Dawn's <c>WGPULoggingCallback</c>. Dawn may call it on any thread.</summary>
public delegate void LoggingCallback(LoggingType type, string message);

/// <summary>
/// Dawn's <c>dawn::native::DawnInstanceDescriptor</c> (<c>dawn/native/DawnNative.h</c>), chained into an
/// <see cref="InstanceDescriptor"/> by <see cref="Wgpu.CreateInstance(in InstanceDescriptor, in DawnInstanceDescriptor)"/>.
/// It's C++, not in dawn.json, so it's mapped by hand; the desktop tests compare its layout with the C++ compiler's.
/// Desktop only: emdawnwebgpu ignores instance chains.
/// </summary>
public ref struct DawnInstanceDescriptor()
{
    /// <summary>
    /// Folders Dawn also loads backend libraries from (vulkan-1.dll, d3dcompiler_47.dll, libvulkan.so.1), before its defaults.
    /// On Windows, Dawn's own search can't find System32: pass <c>Environment.SystemDirectory</c> for the Vulkan backend.
    /// </summary>
    public ReadOnlySpan<string> AdditionalRuntimeSearchPaths { get; init; }

    /// <summary>The default is <see cref="Olve.WebGPU.BackendValidationLevel.Disabled"/>, as in Dawn's C++ constructor.</summary>
    public BackendValidationLevel BackendValidationLevel { get; init; } = BackendValidationLevel.Disabled;

    public bool BeginCaptureOnStartup { get; init; }

    /// <summary>
    /// Receives the instance's messages (backend discovery, adapter warnings) instead of stderr.
    /// The callback stays registered for the rest of the process: Dawn gives no signal when the instance stops using it.
    /// </summary>
    public LoggingCallback? LoggingCallback { get; init; }
}

public static unsafe partial class Wgpu
{
    /// <summary>webgpu.h's <c>wgpuCreateInstance</c>, with Dawn's <see cref="DawnInstanceDescriptor"/> first in the descriptor's chain.</summary>
    [UnsupportedOSPlatform("browser")]
    public static Instance CreateInstance(in InstanceDescriptor descriptor, in DawnInstanceDescriptor dawn)
    {
        using var arena = Arena.Rent();
        var native = Marshalling.ToNative(descriptor, arena);
        var link = arena.Alloc(DawnMarshalling.ToNative(dawn, arena));
        link->Chain.Next = native.NextInChain;
        native.NextInChain = &link->Chain;
        return new Instance(wgpuCreateInstance(arena.Alloc(native)));
    }
}

/// <summary>dawn::native::DawnInstanceDescriptor as the C++ compilers lay it out: the same on MSVC and the Itanium ABI.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WGPUDawnInstanceDescriptor
{
    public WGPUChainedStruct Chain;

    // std::span<std::string_view>: a pointer and a count on every standard library
    public void* AdditionalRuntimeSearchPaths;
    public nuint AdditionalRuntimeSearchPathCount;
    public void* Platform;
    public BackendValidationLevel BackendValidationLevel;

    // A C++ bool: one byte
    public byte BeginCaptureOnStartup;
    public WGPULoggingCallbackInfo LoggingCallbackInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WGPULoggingCallbackInfo
{
    public WGPUChainedStruct* NextInChain;
    public nint Callback;
    public void* Userdata1;
    public void* Userdata2;
}

internal static unsafe class DawnMarshalling
{
    public const SType DawnInstanceDescriptorSType = (SType)0x00050006;

    /// <summary>
    /// std::string_view is a pointer and a length, in the standard library's order: libstdc++ (Dawn's Linux build) puts the length
    /// first, libc++ (macOS) and MSVC's STL the pointer. The desktop tests compare this with the C++ compiler.
    /// </summary>
    public static bool StringViewLengthFirst => OperatingSystem.IsLinux();

    public static WGPUDawnInstanceDescriptor ToNative(in DawnInstanceDescriptor v, Arena arena)
    {
        var paths = v.AdditionalRuntimeSearchPaths;
        var views = arena.Alloc<nint>(paths.Length * 2);
        for (var i = 0; i < paths.Length; i++)
        {
            var bytes = Encoding.UTF8.GetBytes(paths[i]);
            var text = arena.Copy<byte>(bytes);
            views[2 * i + (StringViewLengthFirst ? 1 : 0)] = (nint)text;
            views[2 * i + (StringViewLengthFirst ? 0 : 1)] = bytes.Length;
        }

        return new WGPUDawnInstanceDescriptor
        {
            Chain = new WGPUChainedStruct { SType = DawnInstanceDescriptorSType },
            AdditionalRuntimeSearchPaths = views,
            AdditionalRuntimeSearchPathCount = (nuint)paths.Length,
            BackendValidationLevel = v.BackendValidationLevel,
            BeginCaptureOnStartup = v.BeginCaptureOnStartup ? (byte)1 : (byte)0,
            LoggingCallbackInfo = v.LoggingCallback is null
                ? default
                : new WGPULoggingCallbackInfo { Callback = LoggingEntry, Userdata1 = Callbacks.Register(v.LoggingCallback) },
        };
    }

    private static nint LoggingEntry => (nint)(delegate* unmanaged<LoggingType, WGPUStringView, void*, void*, void>)&OnLogging;

    [UnmanagedCallersOnly]
    private static void OnLogging(LoggingType type, WGPUStringView message, void* userdata1, void* userdata2)
    {
        try
        {
            Callbacks.Find<LoggingCallback>(userdata1)?.Invoke(type, ToManaged(message));
        }
        catch (Exception exception)
        {
            Callbacks.Escaped(nameof(LoggingCallback), exception);
        }
    }
}
