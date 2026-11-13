using System.Text;

namespace Olve.WebGPU.Generator;

/// <summary>
/// The marshalling check: tests/Shared/MarshalCheck.g.cs and native/init_check.g.c.
/// For every descriptor the binding writes (ToNative), it fills each field with a distinct value, marshals it, and reads the native
/// struct back as bytes at the offsets the layout check proves. What it expects comes from dawn.json's fields (type, annotation,
/// length, optional), not from the classification ToNative is generated from, so a misclassified member fails instead of agreeing
/// with itself. The defaults half compares new() with webgpu.h's *_INIT macros, compiled by C.
/// </summary>
public sealed partial class Api
{
    private const int ArrayLength = 2;

    /// <summary>
    /// The structs ToNative writes: every descriptor, and the out structs a descriptor also passes in (Limits, as required limits),
    /// which EmitStruct gives a ToNative when their members are all values.
    /// </summary>
    private List<string> CheckedStructs() =>
        _reps.Where(r => !_skipped.Contains(r.Key) && E(r.Key).Category == "structure" && (r.Value != Generator.Rep.Out || WrittenOut(r.Key)))
            .Select(r => r.Key).OrderBy(n => n, StringComparer.Ordinal).ToList();

    private bool WrittenOut(string name) =>
        Members(E(name), Generator.Rep.Out, out _) is { } members && members.All(m => m.Kind is Kind.Hidden or Kind.Value or Kind.Bool or Kind.Handle or Kind.Pointer);

    public (string CSharp, string C) EmitMarshalCheck(Func<Entry, bool> inBothHeaders)
    {
        var structs = CheckedStructs();
        var cs = new StringBuilder();
        cs.Append($"{Raw.CsBanner}\n\n");
        cs.Append("using System.Runtime.CompilerServices;\nusing System.Runtime.InteropServices;\nusing Olve.WebGPU.Native;\n\nnamespace Olve.WebGPU.Tests;\n\n");
        cs.Append("/// <summary>Every descriptor the binding marshals, filled with distinct values and read back from native memory; see generator/Check.cs.</summary>\n");
        cs.Append("internal static partial class MarshalCheck\n{\n");

        // Entry points
        cs.Append("    public static void Fields(Results r)\n    {\n");
        foreach (var name in structs)
        {
            var c = Pascal(name);
            cs.Append($"        {{\n            var n = new Counter();\n            var v = Make{c}(n);\n            using var arena = Arena.Rent();\n");
            cs.Append($"            var raw = Marshalling.ToNative(v, arena);\n            n = new Counter();\n");
            cs.Append($"            Verify{c}(MemoryMarshal.AsBytes(new ReadOnlySpan<{Names.RawStruct(name)}>(in raw)), n, \"{c}\", r);\n        }}\n");
        }

        cs.Append("    }\n\n");

        var defaults = structs.Where(n => inBothHeaders(E(n))).ToList();
        cs.Append("    public static void Defaults(Results r)\n    {\n");
        for (var i = 0; i < defaults.Count; i++)
        {
            var name = defaults[i];
            var c = Pascal(name);
            var rawName = Names.RawStruct(name);
            cs.Append($"        {{\n            using var arena = Arena.Rent();\n            var raw = Marshalling.ToNative(new {c}(), arena);\n");
            cs.Append($"            var init = Init({i}, Unsafe.SizeOf<{rawName}>(), \"{c}\", r);\n");
            cs.Append($"            if (init is not null)\n            {{\n                Defaults{c}(MemoryMarshal.AsBytes(new ReadOnlySpan<{rawName}>(in raw)), init, \"{c}\", r);\n            }}\n        }}\n");
        }

        cs.Append("    }\n\n");

        foreach (var name in structs)
        {
            EmitMake(name, cs);
            EmitVerify(name, cs);
            EmitDefaults(name, cs);
        }

        cs.Append("}\n");

        var cFile = new StringBuilder($"/* {Raw.Banner[3..]} */\n#include <string.h>\n#include <webgpu/webgpu.h>\n\n");
        cFile.Append("/* Writes webgpu.h's WGPU_*_INIT of descriptor <index> (MarshalCheck.Defaults' order) to out, when not null; returns its size, or -1 */\n");
        cFile.Append("int wgpu_init_struct(int index, void* out) {\n    switch (index) {\n");
        for (var i = 0; i < defaults.Count; i++)
        {
            var rawName = Names.RawStruct(defaults[i]);
            cFile.Append($"    case {i}: {{ {rawName} v = {InitMacro(defaults[i])}; if (out) memcpy(out, &v, sizeof v); return (int)sizeof v; }}\n");
        }

        cFile.Append("    }\n    return -1;\n}\n");
        return (cs.ToString(), cFile.ToString());
    }

    private static string InitMacro(string name) => "WGPU_" + string.Join("_", name.Split(' ').Select(w => w.ToUpperInvariant())) + "_INIT";

    // ---------------------------------------------------------------- dawn.json's view of a field

    private enum Leaf
    {
        Skip, Count, Handle, Bool, Pointer, Scalar, String, Struct, StructPointer, Array,
    }

    private Leaf LeafOf(Entry s, Field f)
    {
        var type = _m.Resolve(f.Type);
        var category = Category(type);
        if (s.Members.Any(o => o.Length == f.Name))
        {
            return Leaf.Count;
        }

        return (category, f.Annotation, f.Length is not null) switch
        {
            ("callback info", _, _) => Leaf.Skip,
            ("object", "value", false) => Leaf.Handle,
            ("native", "value", false) when type == "bool" => Leaf.Bool,
            ("native", "*", false) when type == "void" => Leaf.Pointer,
            ("structure", "value", false) when type == "string view" => Leaf.String,
            ("structure", "value", false) => Leaf.Struct,
            ("structure", "const*", false) => Leaf.StructPointer,
            (_, "const*", true) => Leaf.Array,
            (_, "value", false) => Leaf.Scalar,
            _ => throw new InvalidOperationException($"marshal check: {s.Name}.{f.Name} ({f.Type} {f.Annotation}) has no expectation"),
        };
    }

    /// <summary>The C# type a scalar of this dawn.json type is read as: its width and signedness in webgpu.h.</summary>
    private string Wire(string type) => _m.Resolve(type) switch
    {
        "uint32_t" or "bool" => "uint",
        "int32_t" or "int" => "int",
        "uint64_t" => "ulong",
        "uint16_t" => "ushort",
        "uint8_t" => "byte",
        "size_t" => "nuint",
        "float" => "float",
        "double" => "double",
        var t when Category(t) == "enum" => "uint",
        var t when Category(t) == "bitmask" => "ulong",
        var t when Category(t) == "object" => "nint",
        var t => throw new InvalidOperationException($"marshal check: no wire type for {t}"),
    };

    private string Offset(string owner, string field) => $"Off<{Names.RawStruct(owner)}>(\"{Names.Camel(Names.LowerCamel(field))}\")";

    // ---------------------------------------------------------------- Make: every field set, in dawn.json's order

    private void EmitMake(string name, StringBuilder sb)
    {
        var s = E(name);
        var c = Pascal(name);
        sb.Append($"    private static {c} Make{c}(Counter n) => new()\n    {{\n");
        foreach (var f in s.Members)
        {
            var leaf = LeafOf(s, f);
            if (leaf is Leaf.Skip or Leaf.Count)
            {
                continue;
            }

            sb.Append($"        {MemberName(s, f)} = {MakeValue(f, leaf)},\n");
        }

        foreach (var chain in Chains(name))
        {
            sb.Append($"        {Pascal(chain)} = Make{Pascal(chain)}(n),\n");
        }

        sb.Append("    };\n\n");
    }

    private string MakeValue(Field f, Leaf leaf)
    {
        var type = _m.Resolve(f.Type);
        return leaf switch
        {
            Leaf.Handle => $"new global::Olve.WebGPU.{Pascal(type)}(HandleBase + n.Next())",
            Leaf.Bool => "true",
            Leaf.Pointer => "HandleBase + n.Next()",
            Leaf.Scalar => $"({Scalar(type)})n.Next()",
            Leaf.String => "n.String()",
            Leaf.Struct or Leaf.StructPointer => $"Make{Pascal(type)}(n)",
            Leaf.Array => $"new {Element(type)}[] {{ {string.Join(", ", Enumerable.Repeat(ElementValue(type), ArrayLength))} }}",
            _ => throw new InvalidOperationException(),
        };
    }

    private string Element(string type) => Category(type) switch
    {
        "object" => $"global::Olve.WebGPU.{Pascal(type)}",
        "structure" => Pascal(type),
        _ => Scalar(type)!,
    };

    private string ElementValue(string type) => Category(type) switch
    {
        "object" => $"new global::Olve.WebGPU.{Pascal(type)}(HandleBase + n.Next())",
        "structure" => $"Make{Pascal(type)}(n)",
        _ => $"({Scalar(type)})n.Next()",
    };

    private List<string> Chains(string name) =>
        _chains.GetValueOrDefault(name, []).Where(c => Friendly(c.Name) && _reps[name] != Generator.Rep.Out).Select(c => c.Name).ToList();

    // ---------------------------------------------------------------- Verify: the same values, read from native memory

    private void EmitVerify(string name, StringBuilder sb)
    {
        var s = E(name);
        var c = Pascal(name);
        sb.Append($"    private static void Verify{c}(ReadOnlySpan<byte> b, Counter n, string path, Results r)\n    {{\n");
        if (s.Chained is not null)
        {
            sb.Append($"        r.Equal(path + \".chain.sType\", Read<uint>(b, {Offset(name, "chain")} + Off<WGPUChainedStruct>(\"SType\")), (uint)SType.{c});\n");
        }

        foreach (var f in s.Members)
        {
            var leaf = LeafOf(s, f);
            var path = $"path + \".{Names.LowerCamel(f.Name)}\"";
            var at = Offset(name, f.Name);
            var type = _m.Resolve(f.Type);
            switch (leaf)
            {
                case Leaf.Skip:
                    break;
                case Leaf.Count:
                    sb.Append($"        r.Equal({path}, (ulong)Read<{Wire(f.Type)}>(b, {at}), {ArrayLength}UL);\n");
                    break;
                case Leaf.Handle or Leaf.Pointer:
                    sb.Append($"        r.Equal({path}, Read<nint>(b, {at}), HandleBase + n.Next());\n");
                    break;
                case Leaf.Bool:
                    sb.Append($"        r.Equal({path}, Read<uint>(b, {at}), 1u);\n");
                    break;
                case Leaf.Scalar:
                    sb.Append($"        r.Equal({path}, Read<{Wire(f.Type)}>(b, {at}), ({Wire(f.Type)})n.Next());\n");
                    break;
                case Leaf.String:
                    sb.Append($"        r.String({path}, b, {at}, n);\n");
                    break;
                case Leaf.Struct:
                    sb.Append($"        Verify{Pascal(type)}(b[{at}..], n, {path}, r);\n");
                    break;
                case Leaf.StructPointer:
                    sb.Append($"        if (r.NotNull({path}, Read<nint>(b, {at})) is {{ }} p_{Names.LowerCamel(f.Name)})\n        {{\n");
                    sb.Append($"            Verify{Pascal(type)}(Native(p_{Names.LowerCamel(f.Name)}, Unsafe.SizeOf<{Names.RawStruct(type)}>()), n, {path}, r);\n        }}\n");
                    break;
                case Leaf.Array:
                    var local = $"a_{Names.LowerCamel(f.Name)}";
                    var size = Category(type) == "structure" ? $"Unsafe.SizeOf<{Names.RawStruct(type)}>()" : $"Unsafe.SizeOf<{Wire(type)}>()";
                    sb.Append($"        if (r.NotNull({path}, Read<nint>(b, {at})) is {{ }} {local})\n        {{\n");
                    sb.Append($"            for (var i = 0; i < {ArrayLength}; i++)\n            {{\n");
                    sb.Append($"                var e = Native({local} + i * {size}, {size});\n");
                    sb.Append(Category(type) switch
                    {
                        "structure" => $"                Verify{Pascal(type)}(e, n, {path} + $\"[{{i}}]\", r);\n",
                        "object" => $"                r.Equal({path} + $\"[{{i}}]\", Read<nint>(e, 0), HandleBase + n.Next());\n",
                        _ => $"                r.Equal({path} + $\"[{{i}}]\", Read<{Wire(type)}>(e, 0), ({Wire(type)})n.Next());\n",
                    });
                    sb.Append("            }\n        }\n");
                    break;
            }
        }

        var chains = Chains(name);
        if (chains.Count > 0)
        {
            // The chain's order isn't webgpu.h's to specify: find each link by its sType, in the order Make set them
            sb.Append($"        var links = r.Chain(path + \".nextInChain\", Read<nint>(b, {Offset(name, "next in chain")}), {chains.Count});\n");
            foreach (var chain in chains)
            {
                var cc = Pascal(chain);
                sb.Append($"        if (r.Link(path + \".{cc}\", links, (uint)SType.{cc}) is {{ }} l_{cc})\n        {{\n");
                sb.Append($"            Verify{cc}(Native(l_{cc}, Unsafe.SizeOf<{Names.RawStruct(chain)}>()), n, path + \".{cc}\", r);\n        }}\n");
            }
        }
        else if (s.Extensible is not null)
        {
            sb.Append($"        r.Equal(path + \".nextInChain\", Read<nint>(b, {Offset(name, "next in chain")}), 0);\n");
        }

        sb.Append("    }\n\n");
    }

    // ---------------------------------------------------------------- Defaults: new() against webgpu.h's *_INIT

    private void EmitDefaults(string name, StringBuilder sb)
    {
        var s = E(name);
        var c = Pascal(name);
        sb.Append($"    private static void Defaults{c}(ReadOnlySpan<byte> ours, ReadOnlySpan<byte> init, string path, Results r)\n    {{\n");
        if (s.Extensible is not null)
        {
            sb.Append($"        r.Same(path + \".nextInChain\", Read<nint>(ours, {Offset(name, "next in chain")}), Read<nint>(init, {Offset(name, "next in chain")}));\n");
        }

        if (s.Chained is not null)
        {
            sb.Append($"        r.Same(path + \".chain.sType\", Read<uint>(ours, {Offset(name, "chain")} + Off<WGPUChainedStruct>(\"SType\")), Read<uint>(init, {Offset(name, "chain")} + Off<WGPUChainedStruct>(\"SType\")));\n");
        }

        foreach (var f in s.Members)
        {
            var leaf = LeafOf(s, f);
            var path = $"path + \".{Names.LowerCamel(f.Name)}\"";
            var at = Offset(name, f.Name);
            var type = _m.Resolve(f.Type);
            switch (leaf)
            {
                // The binding's own choices: callback modes, and a required struct pointer it allocates where C can only say NULL
                case Leaf.Skip or Leaf.StructPointer when !f.Optional:
                    break;
                case Leaf.Count or Leaf.Scalar:
                    sb.Append($"        r.Same({path}, Read<{Wire(f.Type)}>(ours, {at}), Read<{Wire(f.Type)}>(init, {at}));\n");
                    break;
                case Leaf.Bool:
                    sb.Append($"        r.Same({path}, Read<uint>(ours, {at}), Read<uint>(init, {at}));\n");
                    break;
                case Leaf.Handle or Leaf.Pointer or Leaf.StructPointer or Leaf.Array:
                    sb.Append($"        r.Same({path}, Read<nint>(ours, {at}), Read<nint>(init, {at}));\n");
                    break;
                case Leaf.String:
                    sb.Append($"        r.Same({path} + \".data\", Read<nint>(ours, {at}), Read<nint>(init, {at}));\n");
                    sb.Append($"        r.Same({path} + \".length\", Read<nuint>(ours, {at} + IntPtr.Size), Read<nuint>(init, {at} + IntPtr.Size));\n");
                    break;
                case Leaf.Struct:
                    sb.Append($"        Defaults{Pascal(type)}(ours[{at}..], init[{at}..], {path}, r);\n");
                    break;
            }
        }

        sb.Append("    }\n\n");
    }
}
