# Third-party notices

Olve.WebGPU is MIT-licensed (see LICENSE). The Olve.WebGPU.Native package also ships software by others:

| Component | Where | License |
|---|---|---|
| [Dawn](https://dawn.googlesource.com/dawn) (google/dawn, release in `dawn/version.env`) | `webgpu_dawn` in `runtimes/*/native/`, statically linked from Dawn's release builds | BSD-3-Clause, `licenses/dawn/LICENSE` |
| Tint (Dawn's shader compiler) | inside `webgpu_dawn` | Dawn's BSD-3-Clause |
| [Abseil](https://github.com/abseil/abseil-cpp), [SPIRV-Tools](https://github.com/KhronosGroup/SPIRV-Tools) | compiled into `webgpu_dawn` | Apache-2.0 |
| Header-only code from SPIRV-Headers, Vulkan-Headers, Vulkan-Utility-Libraries and DirectX-Headers | inside `webgpu_dawn` | MIT (SPIRV-Headers, DirectX-Headers), Apache-2.0 (Vulkan-Utility-Libraries), Apache-2.0 or MIT (Vulkan-Headers) |
| [DirectXShaderCompiler](https://github.com/microsoft/DirectXShaderCompiler) (`DXC_TAG` in `dawn/version.env`) | `dxcompiler.dll`, `dxil.dll` in `runtimes/win-x64/native/` | `licenses/directx-shader-compiler/`: LLVM Release License and MIT for dxcompiler, the Microsoft license in `LICENSE-MS.txt` for dxil.dll |

The dependencies' license texts don't ship yet ([#11](https://github.com/OliverVea/Olve.WebGPU/issues/11)). The versions are the ones in Dawn's `third_party/` at the pinned commit.

The repository also contains Dawn's `dawn.json` (in `dawn/`), the input to the generator, under Dawn's BSD-3-Clause license.
