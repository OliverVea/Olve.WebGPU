// Native side of the layout check: every struct size and field offset, and every enum value, as the C compiler sees them.
// Linked into the wasm by tests/Olve.WebGPU.Tests.Browser; built into webgpu_layout_check (.dll/.so/.dylib) by scripts/build-natives.sh.
#include "layout_check.g.c"

// webgpu.h's WGPU_*_INIT for the descriptors the binding marshals, for MarshalCheck.Defaults
#include "init_check.g.c"
