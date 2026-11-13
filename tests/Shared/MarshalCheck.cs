using System.Runtime.InteropServices;
using System.Text;
using Olve.WebGPU.Native;

namespace Olve.WebGPU.Tests;

/// <summary>The hand-written half of <c>MarshalCheck.g.cs</c>: the values it fills in, and how it reads native memory without unsafe code.</summary>
internal static partial class MarshalCheck
{
    /// <summary>Fake handles and pointers: ToNative only copies them, nothing calls through them.</summary>
    private const nint HandleBase = 0x10000;

    /// <summary>Every field marshalled by the binding, and every default against webgpu.h's *_INIT.</summary>
    public static Results Run()
    {
        var r = new Results();
        Fields(r);
        Defaults(r);
        return r;
    }

    /// <summary>Distinct values, in the order Make and Verify both visit the fields.</summary>
    internal sealed class Counter
    {
        private int _n;

        public int Next() => ++_n;

        public byte[] String() => Encoding.UTF8.GetBytes($"s{Next()}");
    }

    internal sealed class Results
    {
        public int Checked { get; private set; }

        public List<string> Mismatches { get; } = [];

        public void Equal<T>(string path, T actual, T expected) where T : IEquatable<T>
        {
            Checked++;
            if (!actual.Equals(expected))
            {
                Mismatches.Add($"{path}: expected {expected}, got {actual}");
            }
        }

        /// <summary>The binding's default against webgpu.h's. Equals counts two NaNs as equal, whatever their bits.</summary>
        public void Same<T>(string path, T ours, T init) where T : IEquatable<T>
        {
            Checked++;
            if (!ours.Equals(init))
            {
                Mismatches.Add($"{path}: new() gives {ours}, webgpu.h's _INIT {init}");
            }
        }

        public nint? NotNull(string path, nint pointer)
        {
            Checked++;
            if (pointer == 0)
            {
                Mismatches.Add($"{path}: expected a pointer, got null");
                return null;
            }

            return pointer;
        }

        /// <summary>A string view: the bytes Make set, and the NUL after them that emdawnwebgpu relies on.</summary>
        public void String(string path, ReadOnlySpan<byte> b, int at, Counter n)
        {
            var expected = $"s{n.Next()}";
            var data = Read<nint>(b, at);
            var length = Read<nuint>(b, at + IntPtr.Size);
            Equal(path + ".length", length, (nuint)Encoding.UTF8.GetByteCount(expected));
            if (NotNull(path + ".data", data) is not { } p)
            {
                return;
            }

            var bytes = Native(p, expected.Length + 1);
            Equal(path, Encoding.UTF8.GetString(bytes, 0, expected.Length), expected);
            Equal(path + "[NUL]", bytes[expected.Length], (byte)0);
        }

        /// <summary>The chain's links by sType, ending in a null next.</summary>
        public Dictionary<uint, nint> Chain(string path, nint first, int expected)
        {
            var links = new Dictionary<uint, nint>();
            for (var p = first; p != 0 && links.Count <= expected; p = Marshal.ReadIntPtr(p, Off<WGPUChainedStruct>("Next")))
            {
                links[(uint)Marshal.ReadInt32(p, Off<WGPUChainedStruct>("SType"))] = p;
            }

            Equal(path + " links", links.Count, expected);
            return links;
        }

        public nint? Link(string path, Dictionary<uint, nint> links, uint sType)
        {
            Checked++;
            if (links.TryGetValue(sType, out var p))
            {
                return p;
            }

            Mismatches.Add($"{path}: not in the chain");
            return null;
        }
    }

    private static T Read<T>(ReadOnlySpan<byte> b, int offset) where T : struct => MemoryMarshal.Read<T>(b[offset..]);

    private static int Off<T>(string field) => (int)Marshal.OffsetOf<T>(field);

    private static byte[] Native(nint pointer, int size)
    {
        var bytes = new byte[size];
        Marshal.Copy(pointer, bytes, 0, size);
        return bytes;
    }

    /// <summary>webgpu.h's WGPU_*_INIT for descriptor <paramref name="index"/>, compiled by C (native/init_check.g.c).</summary>
    private static byte[]? Init(int index, int size, string path, Results r)
    {
        var native = wgpu_init_struct(index, null);
        r.Equal(path + " size", native, size);
        if (native != size)
        {
            return null;
        }

        var bytes = new byte[size];
        _ = wgpu_init_struct(index, bytes);
        return bytes;
    }

    [DllImport("webgpu_layout_check")]
    private static extern int wgpu_init_struct(int index, byte[]? init);
}
