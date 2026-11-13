# Olve.WebGPU

A faithful C# mapping of WebGPU, generated from Dawn's `dawn.json` (the source of `webgpu.h`). The same assembly runs on desktop, through Dawn, and in the browser, through Emscripten's emdawnwebgpu.

- **Faithful:** the objects, methods, descriptors and enums of `webgpu.h`, with C# names. Descriptors are structs with webgpu.h's defaults, arrays and strings are spans, and requests (`RequestAdapter`, `MapAsync`, ...) return a task of their callback's arguments: `RequestAdapterResult` has the `Status`, the `Adapter` and the `Message`, with `IsSuccess` and `TryGetValue(out adapter)`.
- **No `unsafe` for callers:** the marshalling is generated, callers pass spans and structs.
- **Nothing on top:** there's no safety layer and no ownership tracking. Handles are `IDisposable` record structs whose `Dispose` is webgpu.h's `Release`.
- **80/20 coverage:** what the browser has, plus the native surface sources. Dawn-only extensions, futures (`WaitAny`) and out-chains aren't mapped yet; see the issues.

Descriptors get webgpu.h's defaults from `new()` and object initialisers, like webgpu.h's `*_INIT` macros. `default(T)` and `new T[n]` skip them, like `= {0}` in C: the elements of `new ColorTargetState[n]` have `WriteMask = None` and draw nothing. Build arrays from `new()` values, for example with collection expressions.

## Packages

| Package | What it is |
|---|---|
| `Olve.WebGPU` | The binding (net10.0). No implementation: a library can reference it alone, but an app can't. |
| `Olve.WebGPU.Native` | Desktop: Dawn's `webgpu_dawn` shared library for win-x64, linux-x64, osx-arm64 and osx-x64. On Windows, it depends on Microsoft's `Microsoft.Direct3D.DXC` and copies `dxcompiler.dll` and `dxil.dll` next to `webgpu_dawn.dll` in the build and publish output. |
| `Olve.WebGPU.Browser` | WebAssembly: build files that link emdawnwebgpu, from the same Dawn release, into `dotnet.native.wasm`. Needs .NET 11's `wasm-tools` workload. |

**An app must reference `Olve.WebGPU.Native` (desktop) or `Olve.WebGPU.Browser` (`browser-wasm`).** Both bring in `Olve.WebGPU`. An app that references only `Olve.WebGPU` builds, but its first WebGPU call throws `DllNotFoundException` for `webgpu_dawn`. Reference the same version of all three packages; they always ship together. The release notes name the Dawn release they wrap, which is pinned in [`dawn/version.env`](https://github.com/OliverVea/Olve.WebGPU/blob/main/dawn/version.env).

```csharp
var instance = Wgpu.CreateInstance();
// Requests complete inside ProcessEvents, as promises do in the browser's event loop
var result = await instance.ProcessEventsUntilAsync(instance.RequestAdapterAsync());
if (!result.TryGetValue(out var adapter)) { /* result.Status is Unavailable or Error; result.Message says why */ }

// Desktop only: pump on this thread, without async; false if the request hasn't completed within the timeout
if (instance.TryProcessEventsUntil(adapter.RequestDeviceAsync(), TimeSpan.FromSeconds(5), out var deviceResult)
    && deviceResult.TryGetValue(out var device)) { /* ... */ }
```

`Wgpu` holds webgpu.h's free functions and constants (`wgpuCreateInstance` is `Wgpu.CreateInstance`). Handles keep webgpu.h's names, so in a file that imports both `System` and `Olve.WebGPU`, `Buffer` is ambiguous with `System.Buffer`. Add `using Buffer = Olve.WebGPU.Buffer;` to resolve it.

The triangle samples ([`samples/`](https://github.com/OliverVea/Olve.WebGPU/tree/main/samples)) show the whole path to a frame on a Silk.NET window and on a canvas.

## Platforms and known gaps

| Platform | Status |
|---|---|
| Windows x64 | D3D12, compiling shaders with DXC from `Microsoft.Direct3D.DXC`. Needs the VC++ runtime. Dawn doesn't find `vulkan-1.dll` or `d3dcompiler_47.dll` in System32, so there's no Vulkan backend. |
| Linux x64 | Vulkan on X11. Dawn's prebuilt library has no Wayland surfaces. Built on Ubuntu 24.04, so it needs glibc 2.39 or newer. |
| macOS (arm64, x64) | Metal. The binding maps `SurfaceSourceMetalLayer`, but there's no sample host; you create the `CAMetalLayer`. |
| Browser | emdawnwebgpu from the Dawn release in `dawn/version.env`. The package ships that release's remote port file; on the first build, Emscripten downloads the port from Dawn's GitHub release and checks its SHA-512. The browser tests check every struct layout and enum value against it. |

In the browser, emdawnwebgpu aborts on `Surface.Present()`: the browser presents when the frame callback returns.

For a browser app, publish with `<RunAOTCompilation>true</RunAOTCompilation>`. Without it, .NET interprets the binding, and each call into WebGPU costs about twice as much. A `Draw` took about 125 ns interpreted and about 60 ns with AOT, measured in headless Chromium on SwiftShader. AOT makes the build slower, so the package leaves the choice to the app.

The build files set `WasmCachePath` to a writable folder, because the emdawnwebgpu port must build into Emscripten's cache, and the SDK's cache is read-only. On Windows, that folder must be on the same drive as the .NET SDK.

## Building

```bash
scripts/download-dawn.sh              # Dawn for this platform into artifacts/
scripts/build-natives.sh              # links artifacts/native/<rid>/webgpu_dawn and the test-only layout checker
dotnet run --project tests/Olve.WebGPU.Tests             # layout check + offscreen triangle readback
dotnet run --project samples/Triangle.Desktop            # the triangle in a window
```

On Windows, the scripts need Git Bash, Python, and Visual Studio's C++ tools. On Linux, they need g++. On macOS, they need the Xcode command line tools.

The browser tests and the web sample need `dotnet workload install wasm-tools`. `scripts/verify-web.py` runs them in headless Chromium.

## Regenerating and upgrading Dawn

The generator reads `dawn/dawn.json` and writes `src/Olve.WebGPU/Generated/`, `tests/Shared/LayoutCheck.g.cs` and `native/layout_check.g.c`. CI fails if the committed output differs from a fresh run.

```bash
dotnet run --project generator                           # regenerate
dotnet run --project generator -- --manifest api.txt     # also write the public API, one line per member
```

To move to a new Dawn release:

1. Update `DAWN_TAG` and `DAWN_COMMIT` in `dawn/version.env`.
2. Fetch `src/dawn/dawn.json` at that commit into `dawn/`, and regenerate.
3. Diff the API manifests before and after the upgrade.
4. Replace `src/Olve.WebGPU.Browser/port/emdawnwebgpu.remoteport.py` with the release's `emdawnwebgpu-<tag>.remoteport.py`. CI checks it against `DAWN_TAG`.
5. Run the tests on each platform. The layout check compares 983 sizes, offsets and enum values with the C compiler's.

## License

MIT. `Olve.WebGPU.Native` is `MIT AND BSD-3-Clause AND Apache-2.0`, because it also ships Dawn and code that Dawn links in. DXC comes from Microsoft's package, under Microsoft's licenses. See [THIRD_PARTY_NOTICES.md](https://github.com/OliverVea/Olve.WebGPU/blob/main/THIRD_PARTY_NOTICES.md).
