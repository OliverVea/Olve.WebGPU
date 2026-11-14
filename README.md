# Olve.WebGPU

A faithful C# mapping of WebGPU, generated from Dawn's `dawn.json` (the source of `webgpu.h`). The same assembly runs on desktop, through Dawn, and in the browser, through Emscripten's emdawnwebgpu.

- **Faithful:** the objects, methods, descriptors and enums of `webgpu.h`, with C# names. Descriptors are structs with webgpu.h's defaults, arrays and strings are spans, and requests (`RequestAdapter`, `MapAsync`, ...) return a task of their callback's arguments: `RequestAdapterResult` has the `Status`, the `Adapter` and the `Message`, with `IsSuccess` and `TryGetValue(out adapter)`.
- **No `unsafe` for callers:** the marshalling is generated, callers pass spans and structs.
- **Nothing on top:** there's no safety layer and no ownership tracking. Handles are `IDisposable` record structs whose `Dispose` is webgpu.h's `Release`.
- **80/20 coverage:** what the browser has, plus the native surface sources and Dawn's `DawnInstanceDescriptor`. Other Dawn-only extensions aren't mapped; see the issues.
- **Out chains:** a method that fills a struct with out chains has an overload per chained struct, such as `adapter.GetLimits(out var limits, out CompatibilityModeLimits compatibility)`. The binding links an empty struct in, as C does, and reads both back.
- **Futures:** each request has an overload that also gives webgpu.h's future (`RequestAdapterAsync(options, out var future)`), for `Instance.WaitAny`. `WaitAny` with a timeout needs the instance feature `InstanceFeatureName.TimedWaitAny`; without it, only a zero timeout works. In the browser, use a zero timeout.

Descriptors get webgpu.h's defaults from `new()` and object initialisers: the same values as webgpu.h's `*_INIT` macros, which a test compares field by field. Like those macros, an enum member that has an `Undefined` value defaults to it, which means "use the default": `new BlendComponent().Operation` is `BlendOperation.Undefined`, which the implementation treats as `Add`. `default(T)` and `new T[n]` skip them, like `= {0}` in C: the elements of `new ColorTargetState[n]` have `WriteMask = None` and draw nothing. Build arrays from `new()` values, for example with collection expressions.

Descriptors that hold spans or strings are `ref struct`s, so `"label"u8`, `stackalloc` and collection expressions need no copies. That also means they can't be stored in a field, captured by a lambda, used as a type argument (`Task<T>`, `List<T>`) or passed to an async method. Build them where you make the call. To prepare one elsewhere, keep your own type with arrays and convert it at the call:

```csharp
record MeshPipeline(byte[] Label, VertexBufferLayout[] Buffers);   // storable: yours

device.CreateRenderPipeline(new RenderPipelineDescriptor
{
    Label = mesh.Label,
    Vertex = new VertexState { Module = module, EntryPoint = "vs"u8, Buffers = mesh.Buffers },
});
```

An optional struct member is `T?`, except when `T` is itself a `ref struct`, which C# can't make nullable. Then the member is a plain `T` with a `HasX` flag that setting it in an initializer sets: `new RenderPipelineDescriptor { Fragment = new FragmentState { ... } }` has `HasFragment == true`.

## Packages

| Package | What it is |
|---|---|
| `Olve.WebGPU` | The binding (net10.0). No implementation: a library can reference it alone, but an app can't. |
| `Olve.WebGPU.Native` | Desktop, Android and iOS: Dawn's `webgpu_dawn` shared library for win-x64, linux-x64, linux-arm64, osx-arm64, osx-x64, android-arm64 and android-x64, and its static library for iOS devices and the simulator, which an iOS app's build links in. On Windows, it depends on Microsoft's `Microsoft.Direct3D.DXC` and copies `dxcompiler.dll` and `dxil.dll` next to `webgpu_dawn.dll` in the build and publish output. |
| `Olve.WebGPU.Browser` | WebAssembly: build files that link emdawnwebgpu, from the same Dawn release, into `dotnet.native.wasm`. Needs .NET 11's `wasm-tools` workload. |

**An app must reference `Olve.WebGPU.Native` (desktop, Android, iOS) or `Olve.WebGPU.Browser` (`browser-wasm`).** Both bring in `Olve.WebGPU`. An app that references only `Olve.WebGPU` builds, but its first WebGPU call throws `DllNotFoundException` for `webgpu_dawn`. Reference the same version of all three packages; they always ship together. The release notes name the Dawn release they wrap, which is pinned in [`dawn/version.env`](https://github.com/OliverVea/Olve.WebGPU/blob/main/dawn/version.env).

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

The triangle samples ([`samples/`](https://github.com/OliverVea/Olve.WebGPU/tree/main/samples)) show the whole path to a frame on a Silk.NET window, on a canvas, and as a viewport inside an Avalonia app (`Triangle.Avalonia`: a `NativeControlHost` whose native child window becomes the surface, next to ordinary Avalonia controls).

## Platforms and known gaps

| Platform | Status |
|---|---|
| Windows x64 | D3D12, compiling shaders with DXC from `Microsoft.Direct3D.DXC`. Needs the VC++ runtime. Dawn's own search doesn't find `vulkan-1.dll` or `d3dcompiler_47.dll` in System32; for the Vulkan backend, pass System32 in `DawnInstanceDescriptor.AdditionalRuntimeSearchPaths` (below). |
| Linux (x64, arm64) | Vulkan on X11 and Wayland. Needs glibc 2.28 or newer (built and linked in manylinux_2_28, like Dawn's own Linux build, with libstdc++ linked in). Dawn's releases have no arm64 Linux build, so this repo builds that one from source (`scripts/build-dawn.sh`). CI runs the desktop sample on Wayland under cage, headless, on both. |
| macOS (arm64, x64) | Metal. A window needs a `CAMetalLayer` for `SurfaceSourceMetalLayer`; the desktop sample makes one through the Objective-C runtime (`samples/Triangle.Desktop/MetalLayer.cs`), and CI runs it in a window on both macOS runners. |
| Android (arm64, x64) | Vulkan, from Android 8 (API 26), Dawn's minimum. 16 KB pages, as Google Play requires. The surface comes from `SurfaceSourceAndroidNativeWindow` (an `ANativeWindow`). CI runs the checks on an Android 14 emulator. |
| iOS (devices, simulator) | Metal, from iOS 14. Dawn's static library is linked into the app (force-loaded) and the binding finds its functions in the app's executable. Dawn's xcframework compiles webgpu.h's functions with hidden visibility, so the package ships its libraries with exactly those 279 functions made visible (`scripts/unhide-symbols.py`). CI runs the checks in an iPhone simulator; devices are linked but not run. |
| Browser | emdawnwebgpu from the Dawn release in `dawn/version.env`. The package ships that release's remote port file; on the first build, Emscripten downloads the port from Dawn's GitHub release and checks its SHA-512. The browser tests check every struct layout and enum value against it. |

On desktop, `Wgpu.CreateInstance(descriptor, dawnDescriptor)` chains Dawn's C++-only `DawnInstanceDescriptor`: extra folders for backend libraries, backend validation, and a logging callback for the instance's log. Dawn prepends each search path to a library's file name, so end folders with a separator. Dawn's instance log carries messages such as a backend that fails to load; Dawn writes its other warnings to stderr itself.

```csharp
using var instance = Wgpu.CreateInstance(new InstanceDescriptor(), new DawnInstanceDescriptor
{
    AdditionalRuntimeSearchPaths = [Environment.SystemDirectory + Path.DirectorySeparatorChar],   // Windows: vulkan-1.dll, d3dcompiler_47.dll
    LoggingCallback = (type, message) => logger.Log(type, message),
});
```

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

On Windows, the scripts need Git Bash, Python, and Visual Studio's C++ tools. On Linux, they need g++; CI links in a manylinux_2_28 container instead (`MANYLINUX_TAG` in `dawn/version.env`), so the library needs only glibc 2.28. On macOS, they need the Xcode command line tools.

For linux-arm64, `download-dawn.sh` takes Dawn from this repo's prerelease `dawn-<commit>`: Dawn's releases have no arm64 Linux build, so `.github/workflows/dawn.yml` builds it with `scripts/build-dawn.sh` (about an hour) when the pin changes, and publishes it there.

The browser tests and the web sample need `dotnet workload install wasm-tools`. `scripts/verify-web.py` runs them in headless Chromium.

For Android, the scripts link with the Android NDK r27d that Dawn's own Android build uses (`ANDROID_NDK_HOME`). The test runner needs `dotnet workload install android`, and `scripts/run-android-tests.sh` runs it on a device or emulator and reads the result from logcat:

```bash
for rid in android-arm64 android-x64; do RID=$rid scripts/download-dawn.sh && RID=$rid scripts/build-natives.sh; done
dotnet build tests/Olve.WebGPU.Tests.Android -c Release
scripts/run-android-tests.sh tests/Olve.WebGPU.Tests.Android/bin/Release/net11.0-android/dev.olve.webgpu.tests-Signed.apk
```

For iOS (on a Mac with Xcode and `dotnet workload install ios`), `RID=ios-arm64` and `RID=iossimulator` take Dawn's static libraries from the release's xcframework and build the layout checker; `scripts/run-ios-tests.sh` runs the test runner in an iPhone simulator:

```bash
for rid in ios-arm64 iossimulator; do RID=$rid scripts/download-dawn.sh && RID=$rid scripts/build-natives.sh; done
dotnet build tests/Olve.WebGPU.Tests.iOS -c Release
scripts/run-ios-tests.sh "$(find tests/Olve.WebGPU.Tests.iOS/bin/Release -name '*.app' -maxdepth 3 | head -1)"
```

## Performance

The binding adds nothing measurable to a call: it costs what a plain `DllImport` of the same Dawn function costs, and a frame allocates no managed memory (a test checks that). Descriptors are marshalled into native memory that each thread reuses, so a call with a descriptor allocates nothing either.

`benchmarks/Olve.WebGPU.Benchmarks` measures it (BenchmarkDotNet; `dotnet run -c Release --project benchmarks/Olve.WebGPU.Benchmarks -- --filter '*'`). On Windows 11, an AMD Radeon RX 9070 XT (D3D12) and .NET 10.0.15:

| Call | Binding | `DllImport` | Allocated |
|---|---|---|---|
| `RenderPassEncoder.Draw` | 29.4 ns | 29.3 ns | 0 |
| `SetVertexBuffer` | 31.7 ns | 27.1 ns | 0 |
| `SetBindGroup` with a dynamic offset | 56.9 ns | 55.7 ns | 0 |
| `CreateCommandEncoder` with a label | 148 ns | | 0 |
| `CreateBindGroup` | 208 ns | | 0 |
| A render pass, begun and ended | 1.3 µs | | 0 |
| A frame: 100 draws, a uniform write, a submit | 23 µs | | 0 |
| A request round trip (`OnSubmittedWorkDone`) | 546 ns | | 104 B |

The binding and `DllImport` differ by a few nanoseconds either way between runs. In the browser, publish with AOT (above): a `Draw` took about 125 ns interpreted and about 60 ns with AOT.

## Regenerating and upgrading Dawn

The generator reads `dawn/dawn.json` and writes `src/Olve.WebGPU/Generated/`, `tests/Shared/LayoutCheck.g.cs` and `native/layout_check.g.c`. CI fails if the committed output differs from a fresh run. That output includes `Generated/PublicApi.g.txt`, the whole generated public API with one line per member, so every API change shows up in review.

```bash
dotnet run --project generator                           # regenerate
dotnet run --project generator -- --manifest api.txt     # also write the 80/20 subset of the public API
```

`.github/workflows/dawn-bump.yml` moves to each new Dawn release by itself (daily). When the public API is unchanged, it releases the next patch through `publish.yml`, after the whole CI run passes, and fast-forwards main to the bump. When the API changes, it opens an issue instead, since that's not a patch. By hand:

1. Update `DAWN_TAG` and `DAWN_COMMIT` in `dawn/version.env`.
2. Fetch `src/dawn/dawn.json` at that commit into `dawn/`, and regenerate.
3. Review the diff of `src/Olve.WebGPU/Generated/PublicApi.g.txt`.
4. Replace `src/Olve.WebGPU.Browser/port/emdawnwebgpu.remoteport.py` with the release's `emdawnwebgpu-<tag>.remoteport.py`. CI checks it against `DAWN_TAG`.
5. Run the tests on each platform. The layout check compares 983 sizes, offsets and enum values with the C compiler's.

## License

MIT. `Olve.WebGPU.Native` is `MIT AND BSD-3-Clause AND Apache-2.0`, because it also ships Dawn and code that Dawn links in. DXC comes from Microsoft's package, under Microsoft's licenses. See [THIRD_PARTY_NOTICES.md](https://github.com/OliverVea/Olve.WebGPU/blob/main/THIRD_PARTY_NOTICES.md).
