using System.Globalization;
using System.Text;

namespace Olve.WebGPU.Generator;

/// <summary>How a struct looks in C#.</summary>
public enum Rep
{
    /// <summary>A ref struct: arrays and strings are spans.</summary>
    Ref,

    /// <summary>A record struct that can sit in a span: arrays and strings are ReadOnlyMemory.</summary>
    Plain,

    /// <summary>A record struct the implementation fills: arrays are T[], strings are string.</summary>
    Out,
}

/// <summary>
/// The C#-friendly layer: handle record structs with methods, descriptor structs with defaults, async requests as tasks,
/// and the marshalling between them and the raw layer.
/// </summary>
public sealed class Api
{
    private static readonly HashSet<string> NotFriendly = ["string view", "future", "INTERNAL_HAVE_EMDAWNWEBGPU_HEADER"];

    // Callbacks whose message is part of the result rather than the reason for a failure
    private static readonly HashSet<string> KeepMessage = ["pop error scope callback"];

    // The 80/20 subset the churn manifest covers: these objects and their methods minus the niche ones
    private static readonly HashSet<string> ScopeObjects =
    [
        "instance", "adapter", "device", "queue", "buffer", "texture", "texture view", "sampler", "shader module", "render pipeline",
        "bind group", "bind group layout", "pipeline layout", "command encoder", "command buffer", "render pass encoder", "surface",
    ];

    private static readonly string[] ScopeExcludedWords = ["compute", "query", "bundle", "timestamp"];

    private readonly Model _m;
    private readonly Raw _raw;
    private readonly Dictionary<string, Rep> _reps = new();
    private readonly Dictionary<string, List<Entry>> _chains = new();
    private readonly HashSet<string> _skipped = [];
    private readonly HashSet<string> _taskCallbacks = [];
    private readonly HashSet<string> _delegateCallbacks = [];
    private readonly HashSet<string> _scope = [];
    private readonly List<(string Scope, string Line)> _manifest = [];
    private int _methods;

    public List<string> Manifest => _manifest.Where(l => _scope.Contains(l.Scope)).Select(l => l.Line).Distinct().Order(StringComparer.Ordinal).ToList();
    public List<string> FullManifest => _manifest.Select(l => l.Line).Distinct().Order(StringComparer.Ordinal).ToList();
    public string Summary => $"{_reps.Count} structs ({_reps.Values.Count(r => r == Rep.Ref)} ref, {_reps.Values.Count(r => r == Rep.Plain)} plain, {_reps.Values.Count(r => r == Rep.Out)} out), {_methods} methods, {_skipped.Count} skipped";

    public Api(Model model, Raw raw)
    {
        _m = model;
        _raw = raw;
        Classify();
        ComputeScope();
        foreach (var e in raw.Enums)
        {
            _manifest.Add((e.Name, $"enum {Pascal(e.Name)}"));
            foreach (var v in raw.Values(e))
            {
                _manifest.Add((e.Name, $"enum {Pascal(e.Name)}.{Names.EnumMember(v.Name)} = 0x{Raw.EnumValue(e, v):X}"));
            }
        }
    }

    private Entry E(string name) => _m[name];

    private IEnumerable<Entry> Objects => _raw.Included.Select(E).Where(e => e.Category == "object" && Availability.Web(e.Tags)).Distinct();

    private IEnumerable<Method> MethodsOf(Entry o) => o.Methods.Where(m => Availability.Web(m.Tags));

    private IEnumerable<Entry> Functions => _m.Entries.Values.Where(e => e.Category == "function" && Availability.Web(e.Tags));

    // ---------------------------------------------------------------- classification

    private void Classify()
    {
        var structs = _raw.Included.Select(E).Where(e => e.Category == "structure" && !NotFriendly.Contains(e.Name)).Distinct().ToList();
        foreach (var s in structs)
        {
            if (s.Chained == "out")
            {
                Skip(s.Name, "out chains are not mapped");
            }
            else if (s.Members.Any(m => m.FixedLength is not null))
            {
                Skip(s.Name, "fixed-length array members are not mapped");
            }
        }

        foreach (var s in structs.Where(s => s.Chained == "in" && !_skipped.Contains(s.Name)))
        {
            foreach (var root in s.ChainRoots.Select(_m.Resolve))
            {
                (_chains.TryGetValue(root, out var list) ? list : _chains[root] = []).Add(s);
            }
        }

        // Out: filled by the implementation, and everything they contain
        var outs = new HashSet<string>();
        var queue = new Queue<string>(structs.Where(s => s.Out || s.Extensible == "out").Select(s => s.Name));
        foreach (var cb in _m.Entries.Values.Where(e => e.Category == "callback function"))
        {
            foreach (var a in cb.Args.Where(a => IsStruct(a.Type)))
            {
                queue.Enqueue(_m.Resolve(a.Type));
            }
        }

        while (queue.TryDequeue(out var name))
        {
            if (outs.Add(name))
            {
                foreach (var m in E(name).Members.Where(m => IsStruct(m.Type)))
                {
                    queue.Enqueue(_m.Resolve(m.Type));
                }
            }
        }

        // Plain: anything that sits in an array, and everything those contain
        var plain = new HashSet<string>();
        var arrays = structs.SelectMany(s => s.Members)
            .Concat(Objects.SelectMany(o => MethodsOf(o).SelectMany(m => m.Args)))
            .Where(f => f.Length is not null && IsStruct(f.Type)).Select(f => _m.Resolve(f.Type));
        queue = new Queue<string>(arrays);
        while (queue.TryDequeue(out var name))
        {
            if (!outs.Contains(name) && plain.Add(name))
            {
                foreach (var child in Children(E(name)))
                {
                    queue.Enqueue(child);
                }
            }
        }

        foreach (var s in structs.Where(s => !_skipped.Contains(s.Name)))
        {
            RepOf(s.Name, outs, plain, []);
        }
    }

    private Rep RepOf(string name, HashSet<string> outs, HashSet<string> plain, HashSet<string> visiting)
    {
        if (_reps.TryGetValue(name, out var rep))
        {
            return rep;
        }

        if (outs.Contains(name))
        {
            return _reps[name] = Generator.Rep.Out;
        }

        if (plain.Contains(name) || !visiting.Add(name))
        {
            return _reps[name] = Generator.Rep.Plain;
        }

        var e = E(name);
        var needsSpans = e.Members.Any(m => m.Type == "string view" || (m.Length is not null))
            || Children(e).Any(c => !_skipped.Contains(c) && RepOf(c, outs, plain, visiting) == Generator.Rep.Ref);
        return _reps[name] = needsSpans ? Generator.Rep.Ref : Generator.Rep.Plain;
    }

    private IEnumerable<string> Children(Entry e) =>
        e.Members.Where(m => IsStruct(m.Type)).Select(m => _m.Resolve(m.Type))
            .Concat(_chains.GetValueOrDefault(e.Name, []).Select(c => c.Name));

    private bool IsStruct(string type) =>
        _m.Entries.TryGetValue(_m.Resolve(type), out var e) && e.Category == "structure" && !NotFriendly.Contains(e.Name);

    private void Skip(string name, string why)
    {
        if (_skipped.Add(name))
        {
            _m.Warnings.Add($"skipped {name}: {why}");
        }
    }

    private void ComputeScope()
    {
        var queue = new Queue<string>(["create instance"]);
        foreach (var o in Objects.Where(o => ScopeObjects.Contains(o.Name)))
        {
            queue.Enqueue(o.Name);
            foreach (var m in MethodsOf(o).Where(InScope))
            {
                queue.Enqueue($"{o.Name}.{m.Name}");
                foreach (var a in m.Args)
                {
                    queue.Enqueue(_m.Resolve(a.Type));
                }

                if (m.Returns is not null)
                {
                    queue.Enqueue(_m.Resolve(m.Returns));
                }
            }
        }

        foreach (var c in _m.Entries.Values.Where(e => e.Category == "constant"))
        {
            queue.Enqueue(c.Name);
        }

        while (queue.TryDequeue(out var name))
        {
            if (!_scope.Add(name) || !_m.Entries.TryGetValue(name, out var e) || e.Category == "object")
            {
                continue;
            }

            foreach (var f in e.Members.Concat(e.Args))
            {
                queue.Enqueue(_m.Resolve(f.Type));
            }

            foreach (var c in _chains.GetValueOrDefault(name, []))
            {
                queue.Enqueue(c.Name);
            }
        }
    }

    private static bool InScope(Method m) => !ScopeExcludedWords.Any(w => m.Name.Contains(w)) && !m.Args.Any(a => ScopeExcludedWords.Any(w => a.Type.Contains(w)));

    // ---------------------------------------------------------------- types and defaults

    private static string Pascal(string name) => Names.Camel(name);

    private static string Param(string name) => Names.Identifier(Names.LowerCamel(name));

    private string MemberName(Entry owner, Field f)
    {
        var name = Pascal(f.Name);
        return name == Pascal(owner.Name) ? name + "Value" : name;
    }

    private string? Scalar(string type) => _m.Resolve(type) switch
    {
        "uint32_t" => "uint",
        "int32_t" or "int" => "int",
        "uint64_t" => "ulong",
        "uint16_t" => "ushort",
        "uint8_t" => "byte",
        "size_t" => "nuint",
        "float" => "float",
        "double" => "double",
        var t when E(t).Category is "enum" or "bitmask" => Pascal(t),
        _ => null,
    };

    private string Category(string type) => E(type).Category;

    private string? DefaultExpression(Field f, bool constantOnly)
    {
        if (f.Default is null or "nullptr" or "strlen")
        {
            return null;
        }

        var type = _m.Resolve(f.Type);
        var e = E(type);
        if (e.Category is "enum" or "bitmask")
        {
            var value = e.Values.FirstOrDefault(v => v.Name == f.Default && _raw.Values(e).Contains(v));
            return value is null ? null : $"{Pascal(type)}.{Names.EnumMember(value.Name)}";
        }

        if (f.Default == "zero")
        {
            return null;
        }

        if (type == "bool")
        {
            return f.Default == "true" ? "true" : constantOnly ? "false" : null;
        }

        if (_m.Entries.TryGetValue(f.Default, out var constant) && constant.Category == "constant")
        {
            var (_, isConst) = Constant(constant);
            return constantOnly && !isConst ? null : $"Wgpu.{Pascal(constant.Name)}";
        }

        if (Scalar(type) is { } scalar && e.Category == "native")
        {
            var text = scalar is "float" or "double" ? f.Default.TrimEnd('f', 'F') : f.Default;
            if (scalar is "float" or "double")
            {
                var d = double.Parse(text, CultureInfo.InvariantCulture);
                return d == 0 ? (constantOnly ? "0" : null) : d.ToString("R", CultureInfo.InvariantCulture) + (scalar == "float" ? "f" : "d");
            }

            var n = text.StartsWith("0x") ? Convert.ToUInt64(text, 16) : ulong.Parse(text, CultureInfo.InvariantCulture);
            return n == 0 ? (constantOnly ? "0" : null) : (text.StartsWith("0x") ? text : n.ToString(CultureInfo.InvariantCulture));
        }

        _m.Warnings.Add($"default '{f.Default}' of {f.Name} not mapped");
        return null;
    }

    private static (string Expression, bool IsConst) Constant(Entry c) => (c.ConstantType, c.ConstantValue) switch
    {
        (_, "UINT32_MAX") => ("uint.MaxValue", true),
        (_, "UINT64_MAX") => ("ulong.MaxValue", true),
        (_, "SIZE_MAX") => ("nuint.MaxValue", false),
        ("float", "NAN") => ("float.NaN", true),
        (var t, var v) => throw new InvalidOperationException($"constant {c.Name} = {t} {v}"),
    };

    private string ConstantType(Entry c) => Scalar(c.ConstantType!) ?? throw new InvalidOperationException(c.ConstantType);

    // ---------------------------------------------------------------- struct members

    private enum Kind
    {
        Hidden, Handle, Value, Bool, Pointer, String, Array, Struct, StructPointer, OptionalStruct, CallbackInfo,
    }

    private sealed record Member(Field Field, string Name, Kind Kind, string Type, string? Element, string? Default);

    /// <summary>Maps a struct's members, or returns null with a reason when one can't be mapped.</summary>
    private List<Member>? Members(Entry s, Rep rep, out string? problem)
    {
        problem = null;
        var counts = s.Members.Where(m => m.Length is not null).Select(m => m.Length).ToHashSet();
        var members = new List<Member>();
        foreach (var f in s.Members)
        {
            var name = MemberName(s, f);
            var type = _m.Resolve(f.Type);
            var category = Category(type);
            if (counts.Contains(f.Name))
            {
                members.Add(new Member(f, name, Kind.Hidden, "", null, null));
                continue;
            }

            Member? member = (category, f.Annotation, f.Length) switch
            {
                ("object", "value", null) => new Member(f, name, Kind.Handle, Pascal(type), null, null),
                ("native", "value", null) when type == "bool" => new Member(f, name, Kind.Bool, "bool", null, DefaultExpression(f, false)),
                ("native", "*", null) when type == "void" => new Member(f, name, Kind.Pointer, "nint", null, null),
                (_, "value", null) when Scalar(type) is { } scalar => new Member(f, name, Kind.Value, scalar, null, DefaultExpression(f, false)),
                ("structure", "value", null) when type == "string view" => new Member(f, name, Kind.String,
                    rep switch { Generator.Rep.Ref => "ReadOnlySpan<byte>", Generator.Rep.Plain => "ReadOnlyMemory<byte>", _ => "string" }, null,
                    rep == Generator.Rep.Out ? "\"\"" : null),
                ("structure", "value", null) when Friendly(type) => new Member(f, name, Kind.Struct, Pascal(type), null, f.Default == "zero" ? null : "new()"),
                ("structure", "const*", null) when Friendly(type) => f.Optional
                    ? new Member(f, name, Kind.OptionalStruct, Pascal(type), null, null)
                    : new Member(f, name, Kind.StructPointer, Pascal(type), null, "new()"),
                ("callback info", "value", null) when _reps.ContainsKey(type) || DelegateInfo(type) => new Member(f, name, Kind.CallbackInfo, Pascal(type), null, null),
                (_, "const*", not null) when ArrayElement(type) is { } element => new Member(f, name, Kind.Array,
                    rep switch { Generator.Rep.Ref => $"ReadOnlySpan<{element}>", Generator.Rep.Plain => $"ReadOnlyMemory<{element}>", _ => $"{element}[]" }, element,
                    rep == Generator.Rep.Out ? "[]" : null),
                _ => null,
            };

            if (member is null)
            {
                problem = $"member {f.Name} ({f.Type} {f.Annotation}{(f.Length is null ? "" : " length " + f.Length)}) not mapped";
                return null;
            }

            members.Add(member);
        }

        return members;
    }

    private bool Friendly(string type) => _reps.ContainsKey(type) && !_skipped.Contains(type);

    private string? ArrayElement(string type) => Category(type) switch
    {
        "object" => Pascal(type),
        "structure" when Friendly(type) => Pascal(type),
        "native" when type == "void" => null,
        _ => Scalar(type),
    };

    private bool DelegateInfo(string callbackInfo)
    {
        var callback = E(callbackInfo).Members.First(m => m.Name == "callback").Type;
        _delegateCallbacks.Add(callback);
        return true;
    }

    // ---------------------------------------------------------------- emit

    public IEnumerable<(string File, string Text)> Emit()
    {
        var structs = new StringBuilder();
        var marshal = new StringBuilder();
        foreach (var name in _reps.Keys.ToList())
        {
            EmitStruct(name, structs, marshal);
        }

        var objects = new StringBuilder();
        foreach (var o in Objects)
        {
            EmitObject(o, objects);
        }

        EmitFunctions(objects);

        foreach (var cb in _delegateCallbacks.Where(cb => _raw.Included.Contains(cb)))
        {
            EmitCallbackInfo(cb, structs, marshal);
        }

        var callbacks = new StringBuilder();
        foreach (var cb in _taskCallbacks)
        {
            EmitTaskCallback(cb, callbacks);
        }

        foreach (var cb in _delegateCallbacks.Where(cb => _raw.Included.Contains(cb)))
        {
            EmitDelegateCallback(cb, callbacks);
        }

        const string usings = "using System.Runtime.InteropServices;\nusing Olve.Results;\nusing Olve.WebGPU.Native;\nusing static Olve.WebGPU.Native.Interop;\n";
        yield return ("Structs.g.cs", $"{Raw.CsBanner}\n\n{usings}\nnamespace Olve.WebGPU;\n\n{structs}");
        yield return ("Objects.g.cs", $"{Raw.CsBanner}\n\n{usings}\nnamespace Olve.WebGPU;\n\n{objects}");
        yield return ("Marshalling.g.cs", $"{Raw.CsBanner}\n\n{usings}\nnamespace Olve.WebGPU.Native;\n\ninternal static unsafe partial class Marshalling\n{{\n{marshal}}}\n");
        yield return ("Callbacks.g.cs", $"{Raw.CsBanner}\n\n{usings}\nnamespace Olve.WebGPU.Native;\n\ninternal static unsafe partial class Callbacks\n{{\n{callbacks}}}\n");
    }

    private void EmitStruct(string name, StringBuilder sb, StringBuilder marshal)
    {
        var s = E(name);
        var rep = _reps[name];
        var members = Members(s, rep, out var problem);
        if (members is null)
        {
            Skip(name, problem!);
            _reps.Remove(name);
            return;
        }

        var chains = _chains.GetValueOrDefault(name, []).Where(c => Friendly(c.Name) && rep != Generator.Rep.Out).ToList();
        var cs = Pascal(name);
        var keyword = rep == Generator.Rep.Ref ? "ref struct" : "record struct";
        _manifest.Add((name, $"struct {cs} : {rep}"));
        sb.Append($"public {(rep == Generator.Rep.Ref ? "ref partial struct" : "partial record struct")} {cs}()\n{{\n");
        foreach (var m in members.Where(m => m.Kind != Kind.Hidden))
        {
            AppendMember(sb, name, m.Name, m.Type, m.Default, m.Kind == Kind.OptionalStruct ? _m.Resolve(m.Field.Type) : null);
        }

        foreach (var c in chains)
        {
            AppendMember(sb, name, Pascal(c.Name), Pascal(c.Name), null, c.Name);
        }

        sb.Append("}\n\n");
        _ = keyword;

        if (rep == Generator.Rep.Out)
        {
            EmitFromNative(s, members, marshal);
            if (members.All(m => m.Kind is Kind.Hidden or Kind.Value or Kind.Bool or Kind.Handle or Kind.Pointer))
            {
                EmitToNative(s, members, [], rep, marshal);
            }
        }
        else
        {
            EmitToNative(s, members, chains, rep, marshal);
        }
    }

    /// <summary>Optional structs are nullable, or a Has flag next to the member when they are ref structs.</summary>
    private void AppendMember(StringBuilder sb, string owner, string name, string type, string? initializer, string? optionalStruct)
    {
        var optional = optionalStruct is not null;
        if (optional && _reps.GetValueOrDefault(optionalStruct!) == Generator.Rep.Ref)
        {
            sb.Append($"    public bool Has{name} {{ get; private set; }}\n");
            sb.Append($"    public {type} {name} {{ get; init {{ field = value; Has{name} = true; }} }}\n");
            _manifest.Add((owner, $"struct {Pascal(owner)}.{name} : optional {type}"));
            return;
        }

        var memberType = optional ? type + "?" : type;
        sb.Append($"    public {memberType} {name} {{ get; init; }}{(initializer is null ? "" : $" = {initializer};")}\n");
        _manifest.Add((owner, $"struct {Pascal(owner)}.{name} : {memberType}{(initializer is null ? "" : " = " + initializer)}"));
    }

    private readonly Dictionary<string, string> _unpascal = new();

    private string Unpascal(string cs)
    {
        if (_unpascal.Count == 0)
        {
            foreach (var n in _m.Entries.Keys)
            {
                _unpascal.TryAdd(Pascal(n), n);
            }
        }

        return _unpascal.GetValueOrDefault(cs, cs);
    }

    private void EmitToNative(Entry s, List<Member> members, List<Entry> chains, Rep rep, StringBuilder sb)
    {
        var raw = Names.RawStruct(s.Name);
        sb.Append($"    public static {raw} ToNative(in {Pascal(s.Name)} v, Arena arena)\n    {{\n");
        var inits = new List<string>();
        if (s.Chained is not null)
        {
            inits.Add($"Chain = new WGPUChainedStruct {{ SType = SType.{Pascal(s.Name)} }}");
        }

        foreach (var m in members)
        {
            var field = Names.Camel(m.Field.Name);
            var value = $"v.{m.Name}";
            var span = rep == Generator.Rep.Plain ? ".Span" : "";
            switch (m.Kind)
            {
                case Kind.Hidden:
                    var array = members.First(a => a.Field.Length == m.Field.Name);
                    inits.Add($"{field} = ({_raw.Type(m.Field)})v.{array.Name}.Length");
                    break;
                case Kind.Handle:
                    inits.Add($"{field} = {value}.Handle");
                    break;
                case Kind.Value:
                    inits.Add($"{field} = {value}");
                    break;
                case Kind.Bool:
                    inits.Add($"{field} = {value} ? 1u : 0u");
                    break;
                case Kind.Pointer:
                    inits.Add($"{field} = (void*){value}");
                    break;
                case Kind.String:
                    inits.Add($"{field} = arena.String({value}{span})");
                    break;
                case Kind.Struct:
                    inits.Add($"{field} = ToNative({value}, arena)");
                    break;
                case Kind.CallbackInfo:
                    inits.Add($"{field} = ToNative({value})");
                    break;
                case Kind.StructPointer:
                    inits.Add($"{field} = arena.Alloc(ToNative({value}, arena))");
                    break;
                case Kind.OptionalStruct:
                    inits.Add(_reps[_m.Resolve(m.Field.Type)] == Generator.Rep.Ref
                        ? $"{field} = v.Has{m.Name} ? arena.Alloc(ToNative({value}, arena)) : null"
                        : $"{field} = {value}.HasValue ? arena.Alloc(ToNative({value}.Value, arena)) : null");
                    break;
                case Kind.Array:
                    var element = _m.Resolve(m.Field.Type);
                    var local = Names.LowerCamel(m.Field.Name);
                    if (Category(element) == "structure")
                    {
                        sb.Append($"        var {local} = arena.Alloc<{Names.RawStruct(element)}>({value}.Length);\n");
                        sb.Append($"        for (var i = 0; i < {value}.Length; i++)\n        {{\n            {local}[i] = ToNative({value}{span}[i], arena);\n        }}\n\n");
                        inits.Add($"{field} = {local}");
                    }
                    else if (Category(element) == "object")
                    {
                        inits.Add($"{field} = arena.Copy(MemoryMarshal.Cast<{m.Element}, nint>({value}{span}))");
                    }
                    else
                    {
                        inits.Add($"{field} = arena.Copy({value}{span})");
                    }

                    break;
            }
        }

        if (chains.Count > 0)
        {
            sb.Append("        WGPUChainedStruct* chain = null;\n");
            foreach (var c in chains)
            {
                var name = Pascal(c.Name);
                var test = _reps[c.Name] == Generator.Rep.Ref ? $"v.Has{name}" : $"v.{name}.HasValue";
                var source = _reps[c.Name] == Generator.Rep.Ref ? $"v.{name}" : $"v.{name}.Value";
                sb.Append($"        if ({test})\n        {{\n            var link = arena.Alloc(ToNative({source}, arena));\n            link->Chain.Next = chain;\n            chain = &link->Chain;\n        }}\n\n");
            }

            inits.Insert(0, "NextInChain = chain");
        }

        sb.Append($"        return new {raw}\n        {{\n{string.Concat(inits.Select(i => $"            {i},\n"))}        }};\n    }}\n\n");
    }

    private void EmitFromNative(Entry s, List<Member> members, StringBuilder sb)
    {
        var inits = new List<string>();
        foreach (var m in members)
        {
            var field = "n." + Names.Camel(m.Field.Name);
            switch (m.Kind)
            {
                case Kind.Handle:
                    inits.Add($"{m.Name} = new {m.Type}({field})");
                    break;
                case Kind.Value:
                    inits.Add($"{m.Name} = {field}");
                    break;
                case Kind.Bool:
                    inits.Add($"{m.Name} = {field} != 0");
                    break;
                case Kind.Pointer:
                    inits.Add($"{m.Name} = (nint){field}");
                    break;
                case Kind.String:
                    inits.Add($"{m.Name} = ToManaged({field})");
                    break;
                case Kind.Struct:
                    inits.Add($"{m.Name} = FromNative({field})");
                    break;
                case Kind.StructPointer:
                case Kind.OptionalStruct:
                    inits.Add($"{m.Name} = {field} == null ? default : FromNative(*{field})");
                    break;
                case Kind.Array:
                    var count = "n." + Names.Camel(m.Field.Length!);
                    var element = _m.Resolve(m.Field.Type);
                    var copy = Category(element) switch
                    {
                        "structure" => $"Array(new ReadOnlySpan<{Names.RawStruct(element)}>({field}, (int){count}), static x => FromNative(x))",
                        "object" => $"MemoryMarshal.Cast<nint, {m.Element}>(new ReadOnlySpan<nint>({field}, (int){count})).ToArray()",
                        _ => $"new ReadOnlySpan<{m.Element}>({field}, (int){count}).ToArray()",
                    };
                    inits.Add($"{m.Name} = {field} == null ? [] : {copy}");
                    break;
            }
        }

        sb.Append($"    public static {Pascal(s.Name)} FromNative(in {Names.RawStruct(s.Name)} n) => new()\n    {{\n{string.Concat(inits.Select(i => $"        {i},\n"))}    }};\n\n");
    }

    // ---------------------------------------------------------------- callbacks

    private sealed record CallbackArg(Field Field, string Type, string Convert);

    /// <summary>A native callback argument as the managed value it hands over, or null if it can't be mapped.</summary>
    private CallbackArg? Callback(Field a)
    {
        var type = _m.Resolve(a.Type);
        var name = Names.Identifier(Names.LowerCamel(a.Name));
        return (Category(type), a.Annotation) switch
        {
            ("object", "value") => new CallbackArg(a, Pascal(type), $"new {Pascal(type)}({name})"),
            ("object", "const*") when a.FixedLength == 1 => new CallbackArg(a, Pascal(type), $"{name} == null ? default : new {Pascal(type)}(*{name})"),
            ("structure", "value") when type == "string view" => new CallbackArg(a, "string", $"ToManaged({name})"),
            ("structure", "const*") when _reps.GetValueOrDefault(type) == Generator.Rep.Out => new CallbackArg(a, Pascal(type), $"{name} == null ? default : Marshalling.FromNative(*{name})"),
            ("native", "value") when type == "bool" => new CallbackArg(a, "bool", $"{name} != 0"),
            (_, "value") when Scalar(type) is { } scalar => new CallbackArg(a, scalar, name),
            _ => null,
        };
    }

    private string RawCallbackSignature(Entry cb) =>
        string.Join(", ", cb.Args.Select(a => _raw.Type(a)).Append("void*").Append("void*"));

    private string RawCallbackParams(Entry cb) =>
        string.Join(", ", cb.Args.Select(a => $"{_raw.Type(a)} {Names.Identifier(Names.LowerCamel(a.Name))}").Append("void* userdata1").Append("void* userdata2"));

    /// <summary>The status argument and the result an async request completes with.</summary>
    private (Field Status, Field? Message, List<CallbackArg> Payload, string Result)? TaskShape(string callback)
    {
        var cb = E(callback);
        var status = cb.Args.FirstOrDefault(a => a.Name == "status" && E(a.Type).Values.Any(v => v.Name == "success"));
        if (status is null)
        {
            return null;
        }

        var message = cb.Args.FirstOrDefault(a => a.Name == "message" && a.Type == "string view");
        var payload = new List<CallbackArg>();
        foreach (var a in cb.Args.Where(a => a != status && (a != message || KeepMessage.Contains(callback))))
        {
            if (Callback(a) is not { } arg)
            {
                return null;
            }

            payload.Add(arg);
        }

        var result = payload.Count switch
        {
            0 => "Result",
            1 => $"Result<{payload[0].Type}>",
            _ => $"Result<({string.Join(", ", payload.Select(p => $"{p.Type} {Pascal(p.Field.Name)}"))})>",
        };
        return (status, message, payload, result);
    }

    private void EmitTaskCallback(string callback, StringBuilder sb)
    {
        var cb = E(callback);
        var (status, message, payload, result) = TaskShape(callback)!.Value;
        var name = Pascal(callback);
        var value = payload.Count switch
        {
            0 => "Result.Success()",
            1 => payload[0].Convert,
            _ => $"({string.Join(", ", payload.Select(p => p.Convert))})",
        };
        var problem = message is null
            ? $"new ResultProblem(\"{callback} failed ({{0}})\", status)"
            : $"new ResultProblem(\"{callback} failed ({{0}}): {{1}}\", status, ToManaged(message))";
        sb.Append($"    public static nint {name}Task => (nint)(delegate* unmanaged<{RawCallbackSignature(cb)}, void>)&On{name}Task;\n\n");
        sb.Append($"    [UnmanagedCallersOnly]\n    private static void On{name}Task({RawCallbackParams(cb)})\n    {{\n");
        sb.Append($"        var completion = Take<TaskCompletionSource<{result}>>(userdata1);\n");
        sb.Append($"        completion.SetResult(status == {Pascal(status.Type)}.Success ? {value} : {problem});\n    }}\n\n");
    }

    private void EmitDelegateCallback(string callback, StringBuilder sb)
    {
        var cb = E(callback);
        var args = cb.Args.Select(Callback).ToList();
        var name = Pascal(callback);
        sb.Append($"    public static nint {name}Delegate => (nint)(delegate* unmanaged<{RawCallbackSignature(cb)}, void>)&On{name}Delegate;\n\n");
        sb.Append($"    [UnmanagedCallersOnly]\n    private static void On{name}Delegate({RawCallbackParams(cb)}) =>\n");
        sb.Append($"        Get<{name}>(userdata1)({string.Join(", ", args.Select(a => a!.Convert))});\n\n");
    }

    private void EmitCallbackInfo(string callback, StringBuilder structs, StringBuilder marshal)
    {
        var cb = E(callback);
        var args = cb.Args.Select(Callback).ToList();
        if (args.Any(a => a is null))
        {
            Skip(callback, "an argument can't be mapped");
            return;
        }

        var name = Pascal(callback);
        var parameters = string.Join(", ", args.Select(a => $"{a!.Type} {Param(a.Field.Name)}"));
        structs.Append($"public delegate void {name}({parameters});\n\n");
        _manifest.Add((callback, $"delegate {name}({parameters})"));

        foreach (var info in _m.Entries.Values.Where(e => e.Category == "callback info" && e.Members.Any(m => m.Name == "callback" && m.Type == callback)))
        {
            var infoName = Pascal(info.Name);
            var hasMode = info.Members.Any(m => m.Name == "mode");
            structs.Append($"public record struct {infoName}()\n{{\n");
            if (hasMode)
            {
                structs.Append("    public CallbackMode Mode { get; init; }\n");
                _manifest.Add((info.Name, $"struct {infoName}.Mode : CallbackMode"));
            }

            structs.Append($"    public {name}? Callback {{ get; init; }}\n}}\n\n");
            _manifest.Add((info.Name, $"struct {infoName}.Callback : {name}?"));
            _reps.TryAdd(info.Name, Generator.Rep.Plain);

            // The delegate stays alive as long as the process: a callback info has no end of life the binding could see
            marshal.Append($"    public static WGPU{infoName} ToNative(in {infoName} v) => v.Callback is null ? default : new WGPU{infoName}\n    {{\n");
            if (hasMode)
            {
                marshal.Append("        Mode = v.Mode,\n");
            }

            marshal.Append($"        Callback = Callbacks.{name}Delegate,\n        Userdata1 = Callbacks.Wrap(v.Callback),\n    }};\n\n");
        }
    }

    // ---------------------------------------------------------------- methods

    private void EmitObject(Entry o, StringBuilder sb)
    {
        var name = Pascal(o.Name);
        _manifest.Add((o.Name, $"object {name}"));
        sb.Append($"public readonly unsafe partial record struct {name}(nint Handle) : IDisposable\n{{\n");
        sb.Append($"    public void AddRef() => {Names.Function(o.Name, "add ref")}(Handle);\n\n");
        sb.Append($"    public void Release() => {Names.Function(o.Name, "release")}(Handle);\n\n");
        sb.Append("    /// <summary>Releases the reference this value holds; copies share it.</summary>\n    public void Dispose() => Release();\n\n");
        foreach (var m in MethodsOf(o))
        {
            EmitMethod(o, m.Name, m.Args, m.Returns, sb, $"{o.Name}.{m.Name}");
        }

        sb.Append("}\n\n");
    }

    private void EmitFunctions(StringBuilder sb)
    {
        sb.Append("/// <summary>webgpu.h's free functions and constants.</summary>\npublic static unsafe partial class Wgpu\n{\n");
        foreach (var c in _m.Entries.Values.Where(e => e.Category == "constant" && Availability.BindingType(e.Tags)))
        {
            var (expression, isConst) = Constant(c);
            var type = ConstantType(c);
            sb.Append($"    public {(isConst ? "const" : "static readonly")} {type} {Pascal(c.Name)} = {expression};\n");
            _manifest.Add((c.Name, $"const Wgpu.{Pascal(c.Name)} : {type} = {expression}"));
        }

        sb.Append('\n');
        foreach (var f in Functions)
        {
            EmitMethod(null, f.Name, f.Args, f.Returns, sb, f.Name);
        }

        sb.Append("}\n\n");
    }

    private sealed record Arg(Field Field, string Kind, string Type, string Native, string? Fixed = null, string? Default = null);

    private void EmitMethod(Entry? owner, string methodName, IReadOnlyList<Field> fields, string? returns, StringBuilder sb, string scope)
    {
        var counts = fields.Where(a => a.Length is not null).Select(a => a.Length).ToHashSet();
        var args = new List<Arg>();
        string? callbackInfo = null;
        var generic = false;
        foreach (var a in fields)
        {
            var type = _m.Resolve(a.Type);
            var name = Param(a.Name);
            var category = Category(type);
            if (counts.Contains(a.Name))
            {
                var array = fields.First(x => x.Length == a.Name);
                var bytes = Category(_m.Resolve(array.Type)) == "native" && _m.Resolve(array.Type) == "void";
                args.Add(new Arg(a, "hidden", "", bytes ? $"({_raw.Type(a)})p_{Names.LowerCamel(array.Name)}Length" : $"({_raw.Type(a)}){Param(array.Name)}.Length"));
                continue;
            }

            Arg? arg = (category, a.Annotation, a.Length is not null) switch
            {
                ("callback info", "value", false) => new Arg(a, "callback", "", "callbackInfo"),
                ("object", "value", false) => new Arg(a, "value", Pascal(type), $"{name}.Handle"),
                ("native", "value", false) when type == "bool" => new Arg(a, "value", "bool", $"{name} ? 1u : 0u"),
                (_, "value", false) when Scalar(type) is { } scalar => new Arg(a, "value", scalar, name, Default: DefaultExpression(a, true)),
                ("structure", "value", false) when type == "string view" => new Arg(a, "value", "ReadOnlySpan<byte>", $"arena.String({name})"),
                ("native", "const*" or "*", true) when type == "void" => new Arg(a, "bytes", a.Annotation == "*" ? "Span<T>" : "ReadOnlySpan<T>", $"p_{Names.LowerCamel(a.Name)}",
                    $"byte* p_{Names.LowerCamel(a.Name)} = MemoryMarshal.AsBytes({name})"),
                ("object", "const*", true) => new Arg(a, "value", $"ReadOnlySpan<{Pascal(type)}>", $"p_{Names.LowerCamel(a.Name)}",
                    $"nint* p_{Names.LowerCamel(a.Name)} = MemoryMarshal.Cast<{Pascal(type)}, nint>({name})", a.Default == "nullptr" ? "default" : null),
                (_, "const*", true) when Scalar(type) is { } scalar => new Arg(a, "value", $"ReadOnlySpan<{scalar}>", $"p_{Names.LowerCamel(a.Name)}",
                    $"{scalar}* p_{Names.LowerCamel(a.Name)} = {name}", a.Default == "nullptr" ? "default" : null),
                ("structure", "const*", true) when Friendly(type) => new Arg(a, "structs", $"ReadOnlySpan<{Pascal(type)}>", $"p_{Names.LowerCamel(a.Name)}"),
                ("structure", "const*", false) when Friendly(type) && _reps[type] != Generator.Rep.Out =>
                    new Arg(a, a.Optional ? "optional" : "value", $"in {Pascal(type)}", $"arena.Alloc(Marshalling.ToNative({name}, arena))"),
                ("structure", "*", false) when Friendly(type) && _reps[type] == Generator.Rep.Out => new Arg(a, "out", Pascal(type), $"&n_{Names.LowerCamel(a.Name)}"),
                _ => null,
            };

            if (arg is null)
            {
                _m.Warnings.Add($"skipped method {owner?.Name}.{methodName}: argument {a.Name} ({a.Type} {a.Annotation}) not mapped");
                return;
            }

            generic |= arg.Kind == "bytes";
            if (arg.Kind == "callback")
            {
                callbackInfo = type;
            }

            args.Add(arg);
        }

        // Returns
        var returnType = returns is null ? null : _m.Resolve(returns);
        string csReturn;
        Func<string, string> wrap;
        var outs = args.Where(a => a.Kind == "out").ToList();
        if (callbackInfo is not null)
        {
            var callback = E(callbackInfo).Members.First(m => m.Name == "callback").Type;
            if (TaskShape(callback) is not { } shape)
            {
                _m.Warnings.Add($"skipped method {owner?.Name}.{methodName}: callback {callback} has no success status");
                return;
            }

            _taskCallbacks.Add(callback);
            csReturn = $"Task<{shape.Result}>";
            wrap = call => $"_ = {call};";
        }
        else if (returnType is null && outs.Count == 1)
        {
            csReturn = outs[0].Type;
            wrap = call => $"{call};";
        }
        else if (returnType is null)
        {
            csReturn = "void";
            wrap = call => $"{call};";
        }
        else if (Category(returnType) == "object")
        {
            csReturn = Pascal(returnType);
            wrap = call => $"var result = new {csReturn}({call});";
        }
        else if (returnType == "bool")
        {
            csReturn = "bool";
            wrap = call => $"var result = {call} != 0;";
        }
        else if (returnType is "void *" or "void const *" && fields.Any(f => f.Name == "size"))
        {
            csReturn = returnType == "void *" ? "Span<byte>" : "ReadOnlySpan<byte>";
            wrap = call => $"var result = new {csReturn}({call}, checked((int)size));";
        }
        else if (Scalar(returnType) is { } scalar)
        {
            csReturn = scalar;
            wrap = call => $"var result = {call};";
        }
        else
        {
            _m.Warnings.Add($"skipped method {owner?.Name}.{methodName}: return {returns} not mapped");
            return;
        }

        var isVoidWithOut = returnType is null && outs.Count == 1 && callbackInfo is null;
        var visible = args.Where(a => a.Kind is not ("hidden" or "callback") && !(isVoidWithOut && a.Kind == "out")).ToList();

        // Trailing parameters with constant defaults become optional
        var defaults = new HashSet<Arg>();
        for (var i = visible.Count - 1; i >= 0 && visible[i].Default is not null && visible[i].Kind == "value"; i--)
        {
            defaults.Add(visible[i]);
        }

        var methodCs = Pascal(methodName) + (callbackInfo is null || methodName.EndsWith(" async") ? "" : "Async");
        var optional = args.Where(a => a.Kind == "optional").ToList();
        var function = Names.Function(owner?.Name, methodName);
        EmitOverload(owner, function, methodCs, args, visible, defaults, [], callbackInfo, csReturn, wrap, generic, isVoidWithOut, returns, sb, scope);
        if (optional.Count > 0)
        {
            EmitOverload(owner, function, methodCs, args, visible.Except(optional).ToList(), defaults, optional.ToHashSet(), callbackInfo, csReturn, wrap, generic, isVoidWithOut, returns, sb, scope);
        }

        _methods++;
    }

    private void EmitOverload(Entry? owner, string function, string methodCs, List<Arg> args, List<Arg> visible, HashSet<Arg> defaults, HashSet<Arg> omitted,
        string? callbackInfo, string csReturn, Func<string, string> wrap, bool generic, bool isVoidWithOut, string? returns, StringBuilder sb, string scope)
    {
        var parameters = string.Join(", ", visible.Select(a =>
            (a.Kind == "out" ? "out " : "") + $"{a.Type} {Param(a.Field.Name)}" + (defaults.Contains(a) ? $" = {a.Default}" : "")));
        var typeParams = generic ? "<T>" : "";
        var constraint = generic ? " where T : unmanaged" : "";
        var isStatic = owner is null ? "static " : "";

        // QuerySet.GetType() is webgpu.h's, not object's
        var hides = owner is not null && methodCs == "GetType" && parameters.Length == 0 ? "new " : "";
        sb.Append($"    public {hides}{isStatic}{csReturn} {methodCs}{typeParams}({parameters}){constraint}\n    {{\n");
        _manifest.Add((owner is null ? scope : scope, $"method {(owner is null ? "Wgpu" : Pascal(owner.Name))}.{methodCs}{typeParams}({parameters}) : {csReturn}"));

        var needsArena = args.Any(a => !omitted.Contains(a) && (a.Native.Contains("arena") || a.Kind == "structs"));
        if (needsArena)
        {
            sb.Append("        using var arena = Arena.Rent();\n");
        }

        foreach (var a in args.Where(a => a.Kind == "structs"))
        {
            var element = _m.Resolve(a.Field.Type);
            var local = $"p_{Names.LowerCamel(a.Field.Name)}";
            var name = Param(a.Field.Name);
            sb.Append($"        var {local} = arena.Alloc<{Names.RawStruct(element)}>({name}.Length);\n");
            sb.Append($"        for (var i = 0; i < {name}.Length; i++)\n        {{\n            {local}[i] = Marshalling.ToNative({name}[i], arena);\n        }}\n\n");
        }

        foreach (var a in args.Where(a => a.Kind == "out"))
        {
            sb.Append($"        var n_{Names.LowerCamel(a.Field.Name)} = new {Names.RawStruct(_m.Resolve(a.Field.Type))}();\n");
        }

        if (callbackInfo is not null)
        {
            var callback = E(callbackInfo).Members.First(m => m.Name == "callback").Type;
            var shape = TaskShape(callback)!.Value;
            sb.Append($"        var completion = new TaskCompletionSource<{shape.Result}>(TaskCreationOptions.RunContinuationsAsynchronously);\n");
            sb.Append($"        var callbackInfo = new {Names.RawStruct(callbackInfo)}\n        {{\n");
            if (E(callbackInfo).Members.Any(m => m.Name == "mode"))
            {
                sb.Append("            Mode = CallbackMode.AllowProcessEvents,\n");
            }

            sb.Append($"            Callback = Callbacks.{Pascal(callback)}Task,\n            Userdata1 = Callbacks.Wrap(completion),\n        }};\n");
        }

        var indent = "        ";
        var fixeds = args.Where(a => a.Fixed is not null).ToList();
        foreach (var a in fixeds)
        {
            sb.Append($"{indent}fixed ({a.Fixed})\n");
        }

        if (fixeds.Count > 0)
        {
            sb.Append($"{indent}{{\n");
            indent += "    ";
        }

        foreach (var a in args.Where(a => a.Kind == "bytes"))
        {
            sb.Append($"{indent}var p_{Names.LowerCamel(a.Field.Name)}Length = MemoryMarshal.AsBytes({Param(a.Field.Name)}).Length;\n");
        }

        var nativeArgs = (owner is null ? [] : new[] { "Handle" }).Concat(args.Select(a => omitted.Contains(a) ? "null" : a.Native));
        sb.Append($"{indent}{wrap($"{function}({string.Join(", ", nativeArgs)})")}\n");

        foreach (var a in args.Where(a => a.Kind == "out"))
        {
            var local = $"n_{Names.LowerCamel(a.Field.Name)}";
            var type = _m.Resolve(a.Field.Type);
            var target = isVoidWithOut ? "var value" : Param(a.Field.Name);
            sb.Append($"{indent}{target} = Marshalling.FromNative({local});\n");
            if (Raw.HasFreeMembers(E(type)))
            {
                sb.Append($"{indent}{Names.Function(type, "free members")}({local});\n");
            }
        }

        if (callbackInfo is not null)
        {
            sb.Append($"{indent}return completion.Task;\n");
        }
        else if (isVoidWithOut)
        {
            sb.Append($"{indent}return value;\n");
        }
        else if (csReturn != "void")
        {
            sb.Append($"{indent}return result;\n");
        }

        if (fixeds.Count > 0)
        {
            sb.Append("        }\n");
        }

        sb.Append("    }\n\n");
        _ = returns;
    }
}
