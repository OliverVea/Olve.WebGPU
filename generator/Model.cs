using System.Text.Json.Nodes;

namespace Olve.WebGPU.Generator;

/// <summary>A struct member, method argument or callback argument.</summary>
public sealed record Field(string Name, string Type, string Annotation, string? Length, int? FixedLength, bool Optional, string? Default, IReadOnlyList<string> Tags)
{
    public bool IsPointer => Annotation != "value";
}

public sealed record EnumValue(string Name, long Value, IReadOnlyList<string> Tags);

public sealed record Method(string Name, string? Returns, IReadOnlyList<Field> Args, IReadOnlyList<string> Tags);

/// <summary>One top-level entry of dawn.json.</summary>
public sealed class Entry
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<Field> Members { get; init; } = [];
    public IReadOnlyList<Field> Args { get; init; } = [];
    public IReadOnlyList<EnumValue> Values { get; init; } = [];
    public IReadOnlyList<Method> Methods { get; init; } = [];
    public string? Extensible { get; init; }
    public string? Chained { get; init; }
    public IReadOnlyList<string> ChainRoots { get; init; } = [];
    public bool Out { get; init; }
    public string? Returns { get; init; }
    public string? ConstantType { get; init; }
    public string? ConstantValue { get; init; }
    public string? TypedefTarget { get; init; }
}

/// <summary>dawn.json, read into entries. Unknown keys and categories are collected as warnings.</summary>
public sealed class Model
{
    private static readonly HashSet<string> KnownCategories =
        ["structure", "enum", "bitmask", "object", "native", "constant", "callback function", "callback info", "function", "function pointer", "typedef"];

    private static readonly HashSet<string> KnownEntryKeys =
    [
        "category", "members", "tags", "chained", "chain roots", "extensible", "values", "methods", "args", "_comment", "type", "value",
        "returns", "out", "emscripten_no_enum_table", "wasm type", "no autolock", "emscripten_string_to_int", "is nullable pointer",
        "cpp_value", "wire transparent",
    ];

    private static readonly HashSet<string> KnownFieldKeys =
        ["name", "type", "annotation", "length", "optional", "default", "tags", "no_default", "array_element_optional", "_comment", "wire_is_data_only", "skip_serialize"];

    public Dictionary<string, Entry> Entries { get; } = new();
    public List<string> Warnings { get; } = [];

    public Model(JsonObject json)
    {
        foreach (var (name, node) in json)
        {
            if (name.StartsWith('_'))
            {
                continue;
            }

            var o = node!.AsObject();
            foreach (var (key, _) in o)
            {
                if (!KnownEntryKeys.Contains(key))
                {
                    Warnings.Add($"unknown key '{key}' on '{name}'");
                }
            }

            var category = (string)o["category"]!;
            if (!KnownCategories.Contains(category))
            {
                Warnings.Add($"unknown category '{category}' of '{name}', skipped");
                continue;
            }

            Entries[name] = new Entry
            {
                Name = name,
                Category = category,
                Tags = Strings(o["tags"]),
                Members = Fields(o["members"], name),
                Args = Fields(o["args"], name),
                Values = (o["values"]?.AsArray() ?? []).Select(v => new EnumValue((string)v!["name"]!, Number(v["value"]!), Strings(v["tags"]))).ToList(),
                Methods = (o["methods"]?.AsArray() ?? []).Select(m => new Method(
                    (string)m!["name"]!, ReturnType(m["returns"]), Fields(m["args"], $"{name}.{m["name"]}"), Strings(m["tags"]))).ToList(),
                Extensible = o["extensible"] switch { null => null, JsonValue v when v.TryGetValue<bool>(out var b) => b ? "in" : null, var v => (string)v! },
                Chained = (string?)o["chained"],
                ChainRoots = Strings(o["chain roots"]),
                Out = o["out"] is JsonValue ov && ov.TryGetValue<bool>(out var isOut) && isOut,
                Returns = ReturnType(o["returns"]),
                ConstantType = category == "constant" ? (string?)o["type"] : null,
                ConstantValue = category == "constant" ? o["value"]!.ToString() : null,
                TypedefTarget = category == "typedef" ? (string?)o["type"] : null,
            };
        }
    }

    public Entry this[string name] => Entries[Resolve(name)];

    /// <summary>Follows typedefs (older dawn.json aliases) to the entry they name.</summary>
    public string Resolve(string name)
    {
        while (Entries.TryGetValue(name, out var e) && e.Category == "typedef")
        {
            name = e.TypedefTarget!;
        }

        return name;
    }

    private static string? ReturnType(JsonNode? node) => node switch
    {
        null => null,
        JsonObject o => (string)o["type"]!,
        _ => (string)node! is "void" ? null : (string)node!,
    };

    private static long Number(JsonNode node) =>
        node is JsonValue v && v.TryGetValue<long>(out var l) ? l : Convert.ToInt64((string)node!, ((string)node!).StartsWith("0x") ? 16 : 10);

    private static List<string> Strings(JsonNode? node) => node?.AsArray().Select(t => (string)t!).ToList() ?? [];

    private List<Field> Fields(JsonNode? node, string owner)
    {
        var fields = new List<Field>();
        foreach (var f in node?.AsArray() ?? [])
        {
            var o = f!.AsObject();
            foreach (var (key, _) in o)
            {
                if (!KnownFieldKeys.Contains(key))
                {
                    Warnings.Add($"unknown field key '{key}' on '{owner}.{o["name"]}'");
                }
            }

            var type = (string)o["type"]!;
            var annotation = (string?)o["annotation"] ?? "value";

            // Older dawn.json spells a few pointer types inline, e.g. "void const *"
            if (type.EndsWith(" const *") || type.EndsWith(" *"))
            {
                annotation = type.EndsWith(" const *") ? "const*" : "*";
                type = type.EndsWith(" const *") ? type[..^8] : type[..^2];
            }

            string? length = null;
            int? fixedLength = null;
            switch (o["length"])
            {
                case JsonValue v when v.TryGetValue<int>(out var n):
                    fixedLength = n;
                    break;
                case JsonValue v when ((string)v!).All(char.IsDigit):
                    fixedLength = int.Parse((string)v!);
                    break;
                case JsonValue v:
                    length = (string)v!;
                    break;
            }

            fields.Add(new Field((string)o["name"]!, type, annotation, length, fixedLength,
                o["optional"] is JsonValue ov && ov.GetValue<bool>(), o["default"]?.ToString(), Strings(o["tags"])));
        }

        return fields;
    }
}

/// <summary>Which entries a target sees, by dawn.json tags.</summary>
public static class Availability
{
    // emdawnwebgpu's header is generated with these tags enabled, Dawn native's with the second set
    private static readonly HashSet<string> WebTags = ["emscripten", "compat", "upstream"];
    private static readonly HashSet<string> DesktopTags = ["dawn", "native", "emscripten", "compat", "upstream", "deprecated"];

    public static bool Web(IReadOnlyList<string> tags) => tags.Count == 0 || tags.Any(WebTags.Contains);

    public static bool Desktop(IReadOnlyList<string> tags) => tags.Count == 0 || tags.Any(DesktopTags.Contains);

    /// <summary>
    /// The binding: everything the browser has, plus the standard desktop-only types (native surface sources, ...)
    /// but no Dawn extensions. Functions must also be <see cref="Web"/>: wasm fails to link a P/Invoke that emdawnwebgpu lacks.
    /// </summary>
    public static bool BindingType(IReadOnlyList<string> tags) => Web(tags) || (tags.Contains("native") && !tags.Contains("dawn"));

    /// <summary>webgpu.h moves extension enum values into their own blocks.</summary>
    public static long EnumBlock(IReadOnlyList<string> tags) =>
        tags.Contains("dawn") ? 0x0005_0000 : tags.Contains("emscripten") ? 0x0004_0000 : 0;
}

public static class Names
{
    public static string Camel(string name) =>
        string.Concat(name.Split(' ').Select(chunk => char.ToUpperInvariant(chunk[0]) + chunk[1..]));

    public static string LowerCamel(string name)
    {
        var chunks = name.Split(' ');
        return chunks[0] + string.Concat(chunks.Skip(1).Select(c => char.ToUpperInvariant(c[0]) + c[1..]));
    }

    private static readonly HashSet<string> Keywords =
        ["object", "string", "base", "params", "event", "fixed", "lock", "operator", "checked", "out", "ref", "in", "default", "internal", "public", "static", "class", "struct", "this"];

    public static string Identifier(string name) => Keywords.Contains(name) ? "@" + name : name;

    public static string EnumMember(string name)
    {
        var camel = Camel(name);
        return char.IsDigit(camel[0]) ? "_" + camel : camel;
    }

    public static string RawStruct(string name) => "WGPU" + Camel(name);

    public static string Function(string? obj, string method) => "wgpu" + (obj is null ? "" : Camel(obj)) + Camel(method);
}
