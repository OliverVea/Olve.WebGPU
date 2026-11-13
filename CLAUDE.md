# Olve.WebGPU for agents

A faithful C# mapping of WebGPU, generated from Dawn's `dawn.json`. The README describes the packages, platforms and known gaps.

## Layout

- `generator/`: reads `dawn/dawn.json`. `Model.cs` parses it, `Raw.cs` writes the C ABI (structs, enums, P/Invokes), and `Api.cs` writes the C# layer (handles, descriptors, marshalling, callbacks).
- `src/Olve.WebGPU/`: the binding. `Generated/` is generator output, so never edit it by hand; `Generated/PublicApi.g.txt` is the generated public API, so an API change shows as its diff. `Instance.cs`, `Native/Interop.cs` and `DawnInstanceDescriptor.cs` (Dawn's C++-only struct, desktop only) are hand-written.
- `src/Olve.WebGPU.Native/`, `src/Olve.WebGPU.Browser/`: packaging only. The Native package packs `artifacts/native/<rid>/`. The Browser package ships `buildTransitive` files, the empty `webgpu_dawn.c` module, and the pinned release's emdawnwebgpu remote port (`port/`).
- `native/`: the layout checker: struct layouts and enum values (`layout_check.g.c`), webgpu.h's `*_INIT` defaults (`init_check.g.c`), and the C++ layout of `DawnInstanceDescriptor` (`dawn_layout_check.cpp`, desktop only). `tests/Shared/`: code shared by the desktop tests and the browser tests, including the generated `MarshalCheck.g.cs` (generator/Check.cs), which checks every field ToNative writes and FromNative reads.
- `benchmarks/`: BenchmarkDotNet, run off-cadence (locally or on a GPU machine).
- `dawn/version.env`: the Dawn pin and the exact `Microsoft.Direct3D.DXC` version. Scripts, CI and package metadata read it.
- `src/Olve.WebGPU.Native/buildTransitive/`: copies DXC next to `webgpu_dawn.dll` for package consumers. `build/ProjectNatives.targets` does the same for this repo's tests and samples, and also copies `artifacts/native`.

## Commands

```bash
scripts/download-dawn.sh && scripts/build-natives.sh       # natives into artifacts/ (Windows: Git Bash; set PYTHON if python3 isn't a real Python)
dotnet run --project generator                             # regenerate; commit the output
dotnet run --project tests/Olve.WebGPU.Tests               # layout, marshalling and defaults checks, flows, offscreen readback
dotnet publish tests/Olve.WebGPU.Tests.Browser -c Release -o artifacts/web/tests      # needs the wasm-tools workload
python scripts/verify-web.py artifacts/web/tests/wwwroot tests                        # (Windows: add --channel chrome)
dotnet run -c Release --project benchmarks/Olve.WebGPU.Benchmarks -- --filter '*'   # benchmarks
```

`-p:NativeSource=package` builds the tests and the web sample against `artifacts/packages` (pack with `-p:Version=<v>` and pass the same version), instead of the projects and `artifacts/native`.

## Rules

- The binding stays faithful to `webgpu.h`: no safety layer, ownership tracking or helper abstractions. Change what the generator emits, not the generated files.
- Callers need no `unsafe`. Tests and samples build with `AllowUnsafeBlocks=false`; only the web sample enables it, for the JSExport generator's output.
- Generated files use LF. CI regenerates on Linux and fails on any diff.
- A change to the C side of the layout checker must build on all three platforms: Windows exports its functions explicitly (`scripts/build-natives-windows.cmd`).
- `publish.yml` pushes to nuget.org. It only runs on a published GitHub release or a manual dispatch, so don't create releases or tags to test it.
