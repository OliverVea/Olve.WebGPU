using System.Text.Json.Nodes;
using Olve.WebGPU.Generator;

// usage: [<dawn.json> [<repo root>]] [--header-check <dir>] [--manifest <api.txt>]
//   default:        reads dawn/dawn.json and writes src/Olve.WebGPU/Generated/*.g.cs, tests/Shared/LayoutCheck.g.cs
//                   and native/layout_check.g.c under the repo root (the current directory)
//   --header-check: writes the raw layer as webgpu.h's regex generator saw it (desktop types, functions in both headers) to <dir>
//   --manifest:     also writes the public API, one line per member, for diffing between Dawn versions
var positional = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    if (args[i].StartsWith("--"))
    {
        i++;
        continue;
    }

    positional.Add(args[i]);
}

var dawnJson = positional.ElementAtOrDefault(0) ?? Path.Combine("dawn", "dawn.json");
var root = positional.ElementAtOrDefault(1) ?? ".";
var json = JsonNode.Parse(File.ReadAllText(dawnJson))!.AsObject();
var model = new Model(json);

if (Option("--header-check") is { } headerCheckDir)
{
    var check = new Raw(model, Availability.Desktop, tags => Availability.Web(tags) && Availability.Desktop(tags));
    Directory.CreateDirectory(headerCheckDir);
    Write(Path.Combine(headerCheckDir, "Enums.g.cs"), check.EmitEnums());
    Write(Path.Combine(headerCheckDir, "Native.g.cs"), check.EmitNative());
    Report(check);
    return;
}

var raw = new Raw(model, Availability.BindingType, Availability.Web);
var api = new Api(model, raw);
var generated = Path.Combine(root, "src", "Olve.WebGPU", "Generated");
Directory.CreateDirectory(generated);
Write(Path.Combine(generated, "Enums.g.cs"), raw.EmitEnums());
Write(Path.Combine(generated, "Native.g.cs"), raw.EmitNative());
var (layoutCs, layoutC, layoutCount) = raw.EmitLayoutCheck(e => Availability.Web(e.Tags) && Availability.Desktop(e.Tags));
Directory.CreateDirectory(Path.Combine(root, "tests", "Shared"));
Write(Path.Combine(root, "tests", "Shared", "LayoutCheck.g.cs"), layoutCs);
Write(Path.Combine(root, "native", "layout_check.g.c"), layoutC);
foreach (var (file, text) in api.Emit())
{
    Write(Path.Combine(generated, file), text);
}

if (Option("--manifest") is { } manifest)
{
    File.WriteAllText(manifest, string.Join('\n', api.Manifest) + "\n");
}

Console.WriteLine($"{layoutCount} layout values; api: {api.Summary}");
Report(raw);
return;

string? Option(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;

// LF everywhere, so regenerating on any OS gives the committed bytes
static void Write(string path, string text) => File.WriteAllText(path, text.ReplaceLineEndings("\n"));

void Report(Raw r)
{
    Console.WriteLine($"{r.Enums.Count} enums, {r.Structs.Count} structs, {r.Functions.Count} functions");
    foreach (var warning in model.Warnings.Distinct())
    {
        Console.WriteLine($"warning: {warning}");
    }
}
