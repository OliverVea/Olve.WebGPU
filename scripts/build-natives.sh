#!/usr/bin/env bash
# Links Dawn's static library into the shared library the binding loads, and builds the layout checker the tests use.
#   artifacts/native/<rid>/        webgpu_dawn: what Olve.WebGPU.Native packs
#   artifacts/layout-check/<rid>/  webgpu_layout_check: test-only, compares struct layouts and enum values with the C compiler,
#                                  and the C++-only DawnInstanceDescriptor with the C++ compiler
# Run download-dawn.sh first.
source "$(dirname "$0")/common.sh"
mkdir -p "$NATIVE_DIR" "$LAYOUT_DIR"
header="$DAWN_DIR/include/dawn/webgpu.h"
check_src="$ROOT/native/webgpu_layout_check.c"
dawn_check_src="$ROOT/native/dawn_layout_check.cpp"

case "$RID" in
    linux-*)
        # Link where Dawn was built (a manylinux_2_28 container, glibc 2.28: see ci.yml), with libstdc++ and libgcc inside,
        # so the library runs wherever glibc is 2.28 or newer, whatever libstdc++ the system has
        "$PYTHON" "$ROOT/scripts/mkdef.py" "$header" "$LAYOUT_DIR/webgpu_dawn.map"
        g++ -shared -o "$NATIVE_DIR/libwebgpu_dawn.so" -Wl,--version-script="$LAYOUT_DIR/webgpu_dawn.map" \
            -Wl,--gc-sections -Wl,--no-undefined -Wl,-soname,libwebgpu_dawn.so -static-libstdc++ -static-libgcc \
            -Wl,--whole-archive "$DAWN_DIR/lib64/libwebgpu_dawn.a" -Wl,--no-whole-archive -ldl -lrt -lpthread -lm
        strip --strip-unneeded "$NATIVE_DIR/libwebgpu_dawn.so"
        gcc -c -fPIC -O1 -I"$DAWN_DIR/include" "$check_src" -o "$LAYOUT_DIR/webgpu_layout_check.o"
        g++ -c -fPIC -O1 -std=c++20 -Wno-invalid-offsetof -I"$DAWN_DIR/include" "$dawn_check_src" -o "$LAYOUT_DIR/dawn_layout_check.o"
        g++ -shared "$LAYOUT_DIR/webgpu_layout_check.o" "$LAYOUT_DIR/dawn_layout_check.o" -o "$LAYOUT_DIR/libwebgpu_layout_check.so"
        ;;
    osx-*)
        "$PYTHON" "$ROOT/scripts/mkdef.py" "$header" "$LAYOUT_DIR/webgpu_dawn.exp"
        clang++ -dynamiclib -o "$NATIVE_DIR/libwebgpu_dawn.dylib" -install_name @rpath/libwebgpu_dawn.dylib \
            -Wl,-exported_symbols_list,"$LAYOUT_DIR/webgpu_dawn.exp" -Wl,-dead_strip \
            -Wl,-force_load,"$DAWN_DIR/lib/libwebgpu_dawn.a" \
            -framework CoreFoundation -framework Foundation -framework IOSurface -framework QuartzCore \
            -framework Cocoa -framework IOKit -framework Metal
        strip -x "$NATIVE_DIR/libwebgpu_dawn.dylib"
        clang -c -O1 -I"$DAWN_DIR/include" "$check_src" -o "$LAYOUT_DIR/webgpu_layout_check.o"
        clang++ -c -O1 -std=c++20 -Wno-invalid-offsetof -I"$DAWN_DIR/include" "$dawn_check_src" -o "$LAYOUT_DIR/dawn_layout_check.o"
        clang++ -dynamiclib "$LAYOUT_DIR/webgpu_layout_check.o" "$LAYOUT_DIR/dawn_layout_check.o" -o "$LAYOUT_DIR/libwebgpu_layout_check.dylib"
        ;;
    android-*)
        # The NDK Dawn's Android build uses (r27d); ANDROID_NDK_HOME points at it
        ndk_bin="${ANDROID_NDK_HOME:?set ANDROID_NDK_HOME to Android NDK r27d}/toolchains/llvm/prebuilt/$(uname -s | tr '[:upper:]' '[:lower:]')-x86_64/bin"
        cc="$ndk_bin/$ANDROID_TRIPLE$ANDROID_API-clang"
        "$PYTHON" "$ROOT/scripts/mkdef.py" "$header" "$LAYOUT_DIR/webgpu_dawn.map"
        # 16 KB pages: Google Play requires them for apps targeting Android 15
        "$cc++" -shared -o "$NATIVE_DIR/libwebgpu_dawn.so" -Wl,--version-script="$LAYOUT_DIR/webgpu_dawn.map" \
            -Wl,--gc-sections -Wl,--no-undefined -Wl,-soname,libwebgpu_dawn.so -Wl,-z,max-page-size=16384 \
            -Wl,--whole-archive "$DAWN_DIR/lib/libwebgpu_dawn.a" -Wl,--no-whole-archive -static-libstdc++ -llog -landroid -lm -ldl
        "$ndk_bin/llvm-strip" --strip-unneeded "$NATIVE_DIR/libwebgpu_dawn.so"
        "$cc" -c -fPIC -O1 -I"$DAWN_DIR/include" "$check_src" -o "$LAYOUT_DIR/webgpu_layout_check.o"
        "$cc++" -c -fPIC -O1 -std=c++20 -Wno-invalid-offsetof -I"$DAWN_DIR/include" "$dawn_check_src" -o "$LAYOUT_DIR/dawn_layout_check.o"
        "$cc++" -shared -static-libstdc++ -Wl,-z,max-page-size=16384 "$LAYOUT_DIR/webgpu_layout_check.o" "$LAYOUT_DIR/dawn_layout_check.o" \
            -o "$LAYOUT_DIR/libwebgpu_layout_check.so"
        ;;
    ios-arm64 | iossimulator)
        # iOS apps link static libraries only: Dawn's goes into the package, and the app's build force-loads it. Dawn's xcframework
        # hides webgpu.h's functions (private external), and the binding looks them up by name at run time, so they're made visible
        cp "$DAWN_DIR/lib/libwebgpu_dawn.a" "$NATIVE_DIR/"
        "$PYTHON" "$ROOT/scripts/mkdef.py" "$header" "$LAYOUT_DIR/webgpu_dawn.exp"
        "$PYTHON" "$ROOT/scripts/unhide-symbols.py" "$NATIVE_DIR/libwebgpu_dawn.a" "$LAYOUT_DIR/webgpu_dawn.exp"
        # The layout checker needs Xcode; only the iOS test runner uses it
        if [[ "$(uname -s)" == Darwin ]]; then
            objects=()
            for target in $APPLE_TARGET; do
                arch="${target%%-*}"
                xcrun -sdk "$APPLE_SDK" clang -target "$target" -c -O1 -I"$DAWN_DIR/include" "$check_src" -o "$LAYOUT_DIR/webgpu_layout_check-$arch.o"
                xcrun -sdk "$APPLE_SDK" clang++ -target "$target" -c -O1 -std=c++20 -Wno-invalid-offsetof -I"$DAWN_DIR/include" "$dawn_check_src" -o "$LAYOUT_DIR/dawn_layout_check-$arch.o"
                xcrun libtool -static -o "$LAYOUT_DIR/libwebgpu_layout_check-$arch.a" "$LAYOUT_DIR/webgpu_layout_check-$arch.o" "$LAYOUT_DIR/dawn_layout_check-$arch.o"
                objects+=("$LAYOUT_DIR/libwebgpu_layout_check-$arch.a")
            done
            xcrun lipo -create "${objects[@]}" -output "$LAYOUT_DIR/libwebgpu_layout_check.a"
        fi
        ;;
    win-x64)
        "$PYTHON" "$ROOT/scripts/mkdef.py" "$header" "$LAYOUT_DIR/webgpu_dawn.def"
        cmd //c "$(cygpath -w "$ROOT/scripts/build-natives-windows.cmd")" "$(cygpath -w "$DAWN_DIR")" "$(cygpath -w "$NATIVE_DIR")" "$(cygpath -w "$LAYOUT_DIR")"
        ;;
esac
ls -l "$NATIVE_DIR" "$LAYOUT_DIR"
