using System.Collections.Concurrent;
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
/// Userdata for native callbacks. Requests pass a GCHandle and take (and free) it when they complete,
/// so a request that never completes leaks one handle.
/// Callback infos pass an id that is never reused, and the device lost callback unregisters the device's ids:
/// a callback that still arrives afterwards (emdawnwebgpu doesn't clear them) finds nothing instead of a freed handle.
/// </summary>
internal static unsafe partial class Callbacks
{
    private static readonly ConcurrentDictionary<nint, object> Registered = new();
    private static long _lastId;

    public static void* Wrap(object target) => (void*)GCHandle.ToIntPtr(GCHandle.Alloc(target));

    public static void* Register(object target)
    {
        var id = (nint)Interlocked.Increment(ref _lastId);
        Registered[id] = target;
        return (void*)id;
    }

    public static T? Find<T>(void* id) where T : class => Registered.TryGetValue((nint)id, out var target) ? (T)target : null;

    public static void Unregister(void* id) => Registered.TryRemove((nint)id, out _);

    public static T Take<T>(void* userdata)
    {
        var handle = GCHandle.FromIntPtr((nint)userdata);
        var target = (T)handle.Target!;
        handle.Free();
        return target;
    }
}
