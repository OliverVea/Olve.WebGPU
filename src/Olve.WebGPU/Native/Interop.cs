using System.Runtime.InteropServices;
using System.Text;

namespace Olve.WebGPU.Native;

internal static unsafe partial class Interop
{
    // Desktop loads webgpu_dawn.dll/.so/.dylib; in the browser the wasm P/Invoke table binds this module name
    // to the NativeFileReference webgpu_dawn.c, and the linker resolves the wgpu* symbols from emdawnwebgpu.
    public const string Library = "webgpu_dawn";

    public static readonly nuint Strlen = nuint.MaxValue;

    public static string ToManaged(WGPUStringView view)
    {
        if (view.Data == null)
        {
            return "";
        }

        return view.Length == Strlen
            ? Marshal.PtrToStringUTF8((nint)view.Data) ?? ""
            : Encoding.UTF8.GetString(view.Data, (int)view.Length);
    }
}

/// <summary>Native memory for one call's descriptors: spans and strings are copied in, and freed when the call returns.</summary>
internal sealed unsafe class Arena : IDisposable
{
    private List<nint>? _blocks;

    public T* Alloc<T>(T value) where T : unmanaged
    {
        var p = Alloc<T>(1);
        *p = value;
        return p;
    }

    public T* Alloc<T>(int count) where T : unmanaged
    {
        if (count == 0)
        {
            return null;
        }

        var p = (T*)NativeMemory.AllocZeroed((nuint)count, (nuint)sizeof(T));
        (_blocks ??= []).Add((nint)p);
        return p;
    }

    public T* Copy<T>(ReadOnlySpan<T> values) where T : unmanaged
    {
        var p = Alloc<T>(values.Length);
        values.CopyTo(new Span<T>(p, values.Length));
        return p;
    }

    /// <summary>An empty span is webgpu.h's "no string" (null, WGPU_STRLEN), never an empty string.</summary>
    public WGPUStringView String(ReadOnlySpan<byte> utf8) =>
        utf8.IsEmpty ? new WGPUStringView { Data = null, Length = Interop.Strlen } : new WGPUStringView { Data = Copy(utf8), Length = (nuint)utf8.Length };

    public void Dispose()
    {
        foreach (var block in _blocks ?? [])
        {
            NativeMemory.Free((void*)block);
        }
    }
}

internal static partial class Marshalling
{
    public static T[] Array<TNative, T>(ReadOnlySpan<TNative> natives, Func<TNative, T> map)
    {
        var values = new T[natives.Length];
        for (var i = 0; i < natives.Length; i++)
        {
            values[i] = map(natives[i]);
        }

        return values;
    }
}

/// <summary>
/// Userdata for native callbacks: a GCHandle to the managed target. Requests take (and free) theirs when they complete,
/// so a request that never completes leaks one handle; callback infos keep theirs for the life of the process.
/// </summary>
internal static unsafe partial class Callbacks
{
    public static void* Wrap(object target) => (void*)GCHandle.ToIntPtr(GCHandle.Alloc(target));

    public static T Get<T>(void* userdata) => (T)GCHandle.FromIntPtr((nint)userdata).Target!;

    public static T Take<T>(void* userdata)
    {
        var handle = GCHandle.FromIntPtr((nint)userdata);
        var target = (T)handle.Target!;
        handle.Free();
        return target;
    }
}
