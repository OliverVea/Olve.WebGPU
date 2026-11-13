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

/// <summary>
/// Native memory for one call's descriptors: spans and strings are copied in, and given back when the call returns.
/// Calls on a thread share that thread's <see cref="ArenaStack"/>: a call marks where it starts and returns to the mark,
/// so a callback that calls into the binding during a call allocates above the caller's memory and gives back only its own.
/// </summary>
internal readonly unsafe struct Arena : IDisposable
{
    [ThreadStatic]
    private static ArenaStack? _threadStack;

    private readonly ArenaStack _stack;
    private readonly ArenaStack.Position _mark;

    private Arena(ArenaStack stack)
    {
        _stack = stack;
        _mark = stack.Top;
    }

    public static Arena Rent() => new(_threadStack ??= new ArenaStack());

    public T* Alloc<T>(T value) where T : unmanaged
    {
        var p = Alloc<T>(1);
        *p = value;
        return p;
    }

    public T* Alloc<T>(int count) where T : unmanaged =>
        count == 0 ? null : (T*)_stack.Alloc(checked((nuint)count * (nuint)sizeof(T)));

    public T* Copy<T>(ReadOnlySpan<T> values) where T : unmanaged
    {
        var p = Alloc<T>(values.Length);
        values.CopyTo(new Span<T>(p, values.Length));
        return p;
    }

    /// <summary>
    /// An empty span is webgpu.h's "no string" (null, WGPU_STRLEN), never an empty string.
    /// The copy is also NUL-terminated: emdawnwebgpu reads some strings (the canvas selector) up to the NUL and ignores the length.
    /// </summary>
    public WGPUStringView String(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty)
        {
            return new WGPUStringView { Data = null, Length = Interop.Strlen };
        }

        var p = Alloc<byte>(utf8.Length + 1);
        utf8.CopyTo(new Span<byte>(p, utf8.Length));
        return new WGPUStringView { Data = p, Length = (nuint)utf8.Length };
    }

    public void Dispose() => _stack.Return(_mark);
}

/// <summary>Zeroed native memory handed out from reused chunks, in stack order.</summary>
internal sealed unsafe class ArenaStack
{
    private const nuint ChunkSize = 4096;
    private const nuint Alignment = 16;

    private readonly List<(nint Start, nuint Size)> _chunks = [];
    private Position _top = new(-1, 0);

    public readonly record struct Position(int Chunk, nuint Offset);

    public Position Top => _top;

    public void* Alloc(nuint size)
    {
        var chunk = _top.Chunk;
        var offset = (_top.Offset + Alignment - 1) & ~(Alignment - 1);
        if (chunk < 0 || offset + size > _chunks[chunk].Size)
        {
            chunk = NextChunk(size);
            offset = 0;
        }

        var p = (byte*)_chunks[chunk].Start + offset;
        NativeMemory.Clear(p, size);
        _top = new Position(chunk, offset + size);
        return p;
    }

    /// <summary>Gives back everything above <paramref name="mark"/>. Oversized chunks above it are freed; the others stay for the next call.</summary>
    public void Return(Position mark)
    {
        for (var i = _chunks.Count - 1; i > mark.Chunk; i--)
        {
            if (_chunks[i].Size > ChunkSize)
            {
                NativeMemory.Free((void*)_chunks[i].Start);
                _chunks.RemoveAt(i);
            }
        }

        _top = mark;
    }

    /// <summary>The chunk after the top one, replaced by a bigger one if it can't hold <paramref name="size"/>.</summary>
    private int NextChunk(nuint size)
    {
        var next = _top.Chunk + 1;
        if (next < _chunks.Count && _chunks[next].Size >= size)
        {
            return next;
        }

        var chunkSize = Math.Max(ChunkSize, size);
        var chunk = ((nint)NativeMemory.Alloc(chunkSize), chunkSize);
        if (next < _chunks.Count)
        {
            NativeMemory.Free((void*)_chunks[next].Start);
            _chunks[next] = chunk;
        }
        else
        {
            _chunks.Add(chunk);
        }

        return next;
    }

    // A thread's stack is collected after the thread ends
    ~ArenaStack()
    {
        foreach (var (start, _) in _chunks)
        {
            NativeMemory.Free((void*)start);
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
