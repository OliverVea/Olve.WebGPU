// Empty on purpose: the wasm P/Invoke table only binds module names it gets from a NativeFileReference,
// so this file gives the binding's "webgpu_dawn" imports a module. The wgpu* symbols come from the emdawnwebgpu port.
#include <webgpu/webgpu.h>
