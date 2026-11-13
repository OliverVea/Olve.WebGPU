# Third-party notices

Olve.WebGPU is MIT-licensed (see LICENSE). The Olve.WebGPU.Native package (`MIT AND BSD-3-Clause AND Apache-2.0`) also ships software by others, inside `webgpu_dawn` in `runtimes/*/native/`, which is statically linked from Dawn's release builds. The license texts are in the package's `licenses/`:

| Component | How it's in `webgpu_dawn` | License | Text |
|---|---|---|---|
| [Dawn](https://dawn.googlesource.com/dawn), including Tint (release in `dawn/version.env`) | compiled in | BSD-3-Clause | `licenses/dawn/` |
| [Abseil](https://github.com/abseil/abseil-cpp) | compiled in | Apache-2.0 | `licenses/abseil-cpp/` |
| [SPIRV-Tools](https://github.com/KhronosGroup/SPIRV-Tools) | compiled in | Apache-2.0 | `licenses/spirv-tools/` |
| [SPIRV-Headers](https://github.com/KhronosGroup/SPIRV-Headers) | header-only code | MIT-style (Khronos) | `licenses/spirv-headers/` |
| [Vulkan-Headers](https://github.com/KhronosGroup/Vulkan-Headers) | header-only code | Apache-2.0 or MIT | `licenses/vulkan-headers/` |
| [Vulkan-Utility-Libraries](https://github.com/KhronosGroup/Vulkan-Utility-Libraries) | header-only code | Apache-2.0 | `licenses/vulkan-utility-libraries/` |
| [DirectX-Headers](https://github.com/microsoft/DirectX-Headers) | header-only code (Windows) | MIT | `licenses/directx-headers/` |

The license texts are from the revisions in Dawn's `third_party/` at the pinned commit. None of these projects has a NOTICE file.

On Windows, Olve.WebGPU.Native depends on Microsoft's [Microsoft.Direct3D.DXC](https://www.nuget.org/packages/Microsoft.Direct3D.DXC) package. Its build files copy that package's `dxcompiler.dll` and `dxil.dll` into the app's output. This package doesn't distribute the two DLLs; Microsoft's package does, under the licenses in that package. An app that ships them distributes them under Microsoft's terms.

The repository also contains Dawn's `dawn.json` (in `dawn/`), the input to the generator, under Dawn's BSD-3-Clause license.
