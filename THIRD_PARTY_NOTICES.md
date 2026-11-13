# Third-party notices

Olve.WebGPU is MIT-licensed (see LICENSE). The Olve.WebGPU.Native package also ships software by others:

| Component | Where | License |
|---|---|---|
| [Dawn](https://dawn.googlesource.com/dawn) (google/dawn, release in `dawn/version.env`) | `webgpu_dawn` in `runtimes/*/native/`, statically linked from Dawn's release builds | BSD-3-Clause, `licenses/dawn/LICENSE` |
| Dawn's statically linked dependencies (Tint, Abseil, SPIRV-Tools, SPIRV-Headers, Vulkan headers and utility libraries, DirectX headers, ...) | inside `webgpu_dawn` | their own licenses, listed in Dawn's `third_party/` at the pinned commit |
| [DirectXShaderCompiler](https://github.com/microsoft/DirectXShaderCompiler) (`DXC_TAG` in `dawn/version.env`) | `dxcompiler.dll`, `dxil.dll` in `runtimes/win-x64/native/` | `licenses/directx-shader-compiler/`: LLVM Release License and MIT for dxcompiler, the Microsoft license in `LICENSE-MS.txt` for dxil.dll |

The repository also contains Dawn's `dawn.json` (in `dawn/`), the input to the generator, under Dawn's BSD-3-Clause license.
