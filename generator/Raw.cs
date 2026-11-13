using System.Text;

namespace Olve.WebGPU.Generator;

public sealed record RawFunction(string Name, string Return, IReadOnlyList<(string Type, string Name)> Params);

/// <summary>
/// The C ABI as dawn.json implies it: the members and parameters webgpu.h adds that dawn.json leaves out
/// (nextInChain, chain, userdata, self, AddRef/Release, FreeMembers), structs, enums with their value blocks, and functions.
/// </summary>
public sealed class Raw
{
    private readonly Model _model;
    private readonly Func<IReadOnlyList<string>, bool> _types;
    private readonly Func<IReadOnlyList<string>, bool> _functions;

    public List<Entry> Enums { get; } = [];
    public Dictionary<string, List<(string Type, string Name)>> Structs { get; } = new();
    public List<RawFunction> Functions { get; } = [];
    public HashSet<string> Included { get; } = [];

    public Raw(Model model, Func<IReadOnlyList<string>, bool> types, Func<IReadOnlyList<string>, bool> functions)
    {
        _model = model;
        _types = types;
        _functions = functions;

        foreach (var e in model.Entries.Values.Where(e => types(e.Tags) && e.Category != "typedef"))
        {
            Include(e.Name);
        }

        Structs["chained struct"] = [("WGPUChainedStruct*", "next"), ("SType", "sType")];
        foreach (var e in model.Entries.Values.Where(e => e.Category == "object" && types(e.Tags)))
        {
            var self = ("nint", Names.LowerCamel(e.Name));
            if (functions(e.Tags))
            {
                foreach (var m in e.Methods.Where(m => functions(m.Tags)))
                {
                    Functions.Add(new RawFunction(Names.Function(e.Name, m.Name), ReturnType(m.Returns),
                        [self, .. m.Args.Select(a => (Type(a), Names.LowerCamel(a.Name)))]));
                }

                Functions.Add(new RawFunction(Names.Function(e.Name, "add ref"), "void", [self]));
                Functions.Add(new RawFunction(Names.Function(e.Name, "release"), "void", [self]));
            }
        }

        foreach (var e in model.Entries.Values.Where(e => e.Category == "function" && functions(e.Tags)))
        {
            Functions.Add(new RawFunction(Names.Function(null, e.Name), ReturnType(e.Returns), e.Args.Select(a => (Type(a), Names.LowerCamel(a.Name))).ToList()));
        }

        foreach (var e in model.Entries.Values.Where(e => e.Category == "structure" && Included.Contains(e.Name) && functions(e.Tags) && HasFreeMembers(e)))
        {
            Functions.Add(new RawFunction(Names.Function(e.Name, "free members"), "void", [(Names.RawStruct(e.Name), "value")]));
        }
    }

    /// <summary>Out structs whose members point at memory the implementation owns.</summary>
    public static bool HasFreeMembers(Entry e) =>
        (e.Out || e.Extensible == "out") && e.Members.Any(m => m.IsPointer || m.Type == "string view");

    private void Include(string name)
    {
        name = _model.Resolve(name);
        if (!_model.Entries.TryGetValue(name, out var e) || !Included.Add(name))
        {
            return;
        }

        switch (e.Category)
        {
            case "enum" or "bitmask":
                Enums.Add(e);
                break;
            case "structure":
                var fields = new List<(string, string)>();
                if (e.Extensible is not null)
                {
                    fields.Add(("WGPUChainedStruct*", "nextInChain"));
                }

                if (e.Chained is not null)
                {
                    fields.Add(("WGPUChainedStruct", "chain"));
                }

                fields.AddRange(e.Members.Select(m => (Type(m), Names.LowerCamel(m.Name))));
                Structs[name] = fields;
                break;
            case "callback info":
                Structs[name] = [("WGPUChainedStruct*", "nextInChain"), .. e.Members.Select(m => (Type(m), Names.LowerCamel(m.Name))), ("void*", "userdata1"), ("void*", "userdata2")];
                break;
            case "callback function":
                foreach (var a in e.Args)
                {
                    Type(a);
                }

                break;
        }
    }

    public string ReturnType(string? returns) => returns is null ? "void" : Type(new Field("return", returns, "value", null, null, false, null, []));

    /// <summary>The blittable C# type of a member or parameter.</summary>
    public string Type(Field f)
    {
        var name = _model.Resolve(f.Type);
        var stars = f.Annotation switch { "value" => "", "const*const*" => "**", _ => "*" };
        if (!_model.Entries.TryGetValue(name, out var e))
        {
            throw new InvalidOperationException($"unknown type '{f.Type}'");
        }

        if (!_types(e.Tags) && !Included.Contains(name))
        {
            _model.Warnings.Add($"'{name}' is not in this target but is referenced ({f.Name}); pulled in");
        }

        Include(name);
        var cs = e.Category switch
        {
            "object" => "nint",
            "callback function" or "function pointer" => "nint",
            "enum" or "bitmask" => Names.Camel(name),
            "structure" or "callback info" => Names.RawStruct(name),
            "native" => name switch
            {
                "bool" => "uint",
                "char" or "uint8_t" => "byte",
                "void" => "void",
                "void *" or "void const *" => "void*",
                "size_t" => "nuint",
                "uint32_t" => "uint",
                "int32_t" or "int" => "int",
                "uint64_t" => "ulong",
                "uint16_t" => "ushort",
                "float" => "float",
                "double" => "double",
                _ => throw new InvalidOperationException($"unmapped native type '{name}'"),
            },
            _ => throw new InvalidOperationException($"unexpected {e.Category} '{name}' as a type"),
        };
        return cs + stars;
    }

    public static long EnumValue(Entry e, EnumValue v) => e.Category == "bitmask" ? v.Value : v.Value + Availability.EnumBlock(v.Tags);

    public IEnumerable<EnumValue> Values(Entry e) => e.Values.Where(v => _types(v.Tags));

    public const string Banner = "// <auto-generated> by Olve.WebGPU's generator from Dawn's dawn.json. Do not edit. </auto-generated>";

    public const string CsBanner = Banner + "\n#nullable enable";

    public string EmitEnums()
    {
        var sb = new StringBuilder($"{Banner}\n\nnamespace Olve.WebGPU;\n\n");
        foreach (var e in Enums)
        {
            var flags = e.Category == "bitmask";
            if (flags)
            {
                sb.Append("[Flags]\n");
            }

            sb.Append($"public enum {Names.Camel(e.Name)} : {(flags ? "ulong" : "uint")}\n{{\n");
            foreach (var v in Values(e))
            {
                sb.Append($"    {Names.EnumMember(v.Name)} = 0x{(flags ? EnumValue(e, v).ToString("X16") : EnumValue(e, v).ToString("X8"))},\n");
            }

            sb.Append("}\n\n");
        }

        return sb.ToString();
    }

    public string EmitNative()
    {
        var sb = new StringBuilder($"{Banner}\n\n#pragma warning disable CS0649 // fields are written by native code or by object initialisers\nusing System.Runtime.InteropServices;\n\nnamespace Olve.WebGPU.Native;\n\n");
        foreach (var (name, fields) in Structs)
        {
            sb.Append($"internal unsafe struct {Names.RawStruct(name)}\n{{\n");
            foreach (var (type, field) in fields)
            {
                sb.Append($"    public {type} {Names.Camel(field)};\n");
            }

            sb.Append("}\n\n");
        }

        sb.Append("internal static unsafe partial class Wgpu\n{\n");
        foreach (var f in Functions)
        {
            sb.Append($"    [LibraryImport(Library)] public static partial {f.Return} {f.Name}({string.Join(", ", f.Params.Select(p => $"{p.Type} {Names.Identifier(p.Name)}"))});\n");
        }

        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>Every size and field offset of the structs both headers have, computed by C# and by the C compiler.</summary>
    public (string CSharp, string C, int Count) EmitLayoutCheck(Func<Entry, bool> inBothHeaders)
    {
        var cs = new StringBuilder($"{CsBanner}\n\nusing System.Runtime.CompilerServices;\nusing System.Runtime.InteropServices;\nusing Olve.WebGPU.Native;\n\nnamespace Olve.WebGPU.Tests;\n\n/// <summary>Every struct size, field offset and enum value as C# sees them, in layout_check.g.c's order.</summary>\ninternal static class LayoutCheck\n{{\n    public static (string Name, long Value)[] Values() =>\n    [\n");
        var c = new StringBuilder($"/* {Banner[3..]} */\n#include <stddef.h>\n#include <stdint.h>\n#include <webgpu/webgpu.h>\n\nint wgpu_layout_values(int64_t* out) {{\n    int i = 0;\n");
        var count = 0;
        foreach (var (name, fields) in Structs)
        {
            if (name != "chained struct" && !inBothHeaders(_model.Entries[name]))
            {
                continue;
            }

            var raw = Names.RawStruct(name);
            cs.Append($"        (\"{raw}\", Unsafe.SizeOf<{raw}>()),\n");
            c.Append($"    if (out) out[i] = sizeof({raw}); i++;\n");
            count++;
            foreach (var (_, field) in fields)
            {
                cs.Append($"        (\"{raw}.{field}\", Marshal.OffsetOf<{raw}>(\"{Names.Camel(field)}\").ToInt64()),\n");
                c.Append($"    if (out) out[i] = offsetof({raw}, {field}); i++;\n");
                count++;
            }
        }

        // Enum values too: the struct layout says nothing about a binding generated for another Dawn version than the library's
        foreach (var e in Enums.Where(e => Availability.Web(e.Tags) && Availability.Desktop(e.Tags)))
        {
            foreach (var v in Values(e).Where(v => Availability.Web(v.Tags) && Availability.Desktop(v.Tags)))
            {
                var member = $"{Names.Camel(e.Name)}.{Names.EnumMember(v.Name)}";
                cs.Append($"        (\"{member}\", (long){member}),\n");
                c.Append($"    if (out) out[i] = (int64_t)WGPU{Names.Camel(e.Name)}_{Names.Camel(v.Name)}; i++;\n");
                count++;
            }
        }

        cs.Append("    ];\n}\n");
        c.Append("    return i;\n}\n");
        return (cs.ToString(), c.ToString(), count);
    }
}
