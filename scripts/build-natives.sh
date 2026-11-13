#!/usr/bin/env bash
# Links Dawn's static library into the shared library the binding loads, and builds the layout checker the tests use.
#   artifacts/native/<rid>/        webgpu_dawn (+ dxcompiler, dxil on Windows): what Olve.WebGPU.Native packs
#   artifacts/layout-check/<rid>/  webgpu_layout_check: test-only, compares struct layouts and enum values with the C compiler
# Run download-dawn.sh first.
source "$(dirname "$0")/common.sh"
mkdir -p "$NATIVE_DIR" "$LAYOUT_DIR"
header="$DAWN_DIR/include/dawn/webgpu.h"
check_src="$ROOT/native/webgpu_layout_check.c"

case "$RID" in
    linux-x64)
        "$PYTHON" "$ROOT/scripts/mkdef.py" "$header" "$LAYOUT_DIR/webgpu_dawn.map"
        g++ -shared -o "$NATIVE_DIR/libwebgpu_dawn.so" -Wl,--version-script="$LAYOUT_DIR/webgpu_dawn.map" \
            -Wl,--gc-sections -Wl,--no-undefined -Wl,-soname,libwebgpu_dawn.so \
            -Wl,--whole-archive "$DAWN_DIR/lib64/libwebgpu_dawn.a" -Wl,--no-whole-archive -ldl -lrt -lpthread -lm
        strip --strip-unneeded "$NATIVE_DIR/libwebgpu_dawn.so"
        gcc -shared -fPIC -O1 -I"$DAWN_DIR/include" "$check_src" -o "$LAYOUT_DIR/libwebgpu_layout_check.so"
        ;;
    osx-*)
        "$PYTHON" "$ROOT/scripts/mkdef.py" "$header" "$LAYOUT_DIR/webgpu_dawn.exp"
        clang++ -dynamiclib -o "$NATIVE_DIR/libwebgpu_dawn.dylib" -install_name @rpath/libwebgpu_dawn.dylib \
            -Wl,-exported_symbols_list,"$LAYOUT_DIR/webgpu_dawn.exp" -Wl,-dead_strip \
            -Wl,-force_load,"$DAWN_DIR/lib/libwebgpu_dawn.a" \
            -framework CoreFoundation -framework Foundation -framework IOSurface -framework QuartzCore \
            -framework Cocoa -framework IOKit -framework Metal
        strip -x "$NATIVE_DIR/libwebgpu_dawn.dylib"
        clang -dynamiclib -O1 -I"$DAWN_DIR/include" "$check_src" -o "$LAYOUT_DIR/libwebgpu_layout_check.dylib"
        ;;
    win-x64)
        "$PYTHON" "$ROOT/scripts/mkdef.py" "$header" "$LAYOUT_DIR/webgpu_dawn.def"
        cmd //c "$(cygpath -w "$ROOT/scripts/build-natives-windows.cmd")" "$(cygpath -w "$DAWN_DIR")" "$(cygpath -w "$NATIVE_DIR")" "$(cygpath -w "$LAYOUT_DIR")"
        cp "$ROOT/artifacts/dxc/bin/x64/dxcompiler.dll" "$ROOT/artifacts/dxc/bin/x64/dxil.dll" "$NATIVE_DIR/"
        ;;
esac
ls -l "$NATIVE_DIR" "$LAYOUT_DIR"
