# Olve.WebGPU for agents

A faithful C# mapping of WebGPU, generated from Dawn's `dawn.json`. The README describes the packages, platforms and known gaps.

## Layout

- `generator/`: reads `dawn/dawn.json`. `Model.cs` parses it, `Raw.cs` writes the C ABI (structs, enums, P/Invokes), and `Api.cs` writes the C# layer (handles, descriptors, marshalling, callbacks).
- `src/Olve.WebGPU/`: the binding. `Generated/` is generator output, so never edit it by hand. `Instance.cs` and `Native/Interop.cs` are hand-written.
- `src/Olve.WebGPU.Native/`, `src/Olve.WebGPU.Browser/`: packaging only. The Native package packs `artifacts/native/<rid>/`. The Browser package ships `buildTransitive` files, the empty `webgpu_dawn.c` module, and the pinned release's emdawnwebgpu remote port (`port/`).
- `native/`: the layout checker's C side. `tests/Shared/`: code shared by the desktop tests and the browser tests.
- `dawn/version.env`: the Dawn pin and the exact `Microsoft.Direct3D.DXC` version. Scripts, CI and package metadata read it.
- `src/Olve.WebGPU.Native/buildTransitive/`: copies DXC next to `webgpu_dawn.dll` for package consumers. `build/ProjectNatives.targets` does the same for this repo's tests and samples, and also copies `artifacts/native`.

## Commands

```bash
scripts/download-dawn.sh && scripts/build-natives.sh       # natives into artifacts/ (Windows: Git Bash; set PYTHON if python3 isn't a real Python)
dotnet run --project generator                             # regenerate; commit the output
dotnet run --project tests/Olve.WebGPU.Tests               # layout check + offscreen readback
dotnet publish tests/Olve.WebGPU.Tests.Browser -c Release -o artifacts/web/tests      # needs the wasm-tools workload
python scripts/verify-web.py artifacts/web/tests/wwwroot tests                        # (Windows: add --channel chrome)
```

`-p:NativeSource=package` builds the tests and the web sample against `artifacts/packages` (pack with `-p:Version=<v>` and pass the same version), instead of the projects and `artifacts/native`.

## Rules

- The binding stays faithful to `webgpu.h`: no safety layer, ownership tracking or helper abstractions. Change what the generator emits, not the generated files.
- Callers need no `unsafe`. Tests and samples build with `AllowUnsafeBlocks=false`; only the web sample enables it, for the JSExport generator's output.
- Generated files use LF. CI regenerates on Linux and fails on any diff.
- `publish.yml` pushes to nuget.org. It only runs on a published GitHub release or a manual dispatch, so don't create releases or tags to test it.
