#!/usr/bin/env bash
# Builds Dawn's static library from source at DAWN_COMMIT, for RIDs Dawn's releases don't cover (linux-arm64).
# Runs in a manylinux_2_28 container of the target architecture, like Dawn's own Linux build (glibc 2.28):
#   docker run --rm -v "$PWD:/w" -w /w -e RID=linux-arm64 quay.io/pypa/manylinux_2_28_aarch64 scripts/build-dawn.sh
# Writes artifacts/dawn-build/Dawn-<commit>-<rid>-Release.tar.gz, laid out like Dawn's release archives (include/, lib64/),
# with the headers from the release's headers archive. .github/workflows/dawn.yml publishes it as a release asset of this repo.
source "$(dirname "$0")/common.sh"
src="$ROOT/artifacts/dawn-src"
build="$ROOT/artifacts/dawn-build/$RID"
name="Dawn-$DAWN_COMMIT-$RID-Release"
out="$ROOT/artifacts/dawn-build/$name"

# The libraries Dawn's Linux backends and surfaces build against (X11, Wayland, Vulkan headers come with Dawn)
dnf install -y -q libxcb-devel libX11-devel libX11-xcb libXcursor-devel libXrandr-devel libXinerama-devel libXi-devel libXext-devel \
    libxkbcommon-devel libxkbcommon-x11-devel mesa-libGL-devel wayland-devel wayland-protocols-devel
# The image's own CPython (the system python3 is 3.6, without pip) for Ninja and Dawn's dependency fetcher
export PATH="$(ls -d /opt/python/cp312-cp312/bin):$PATH"
python3 -m pip install -q ninja

if [[ ! -d "$src/.git" ]]; then
    git init -q "$src"
    git -C "$src" remote add origin https://dawn.googlesource.com/dawn
fi
git -C "$src" fetch -q --depth 1 origin "$DAWN_COMMIT"
git -C "$src" checkout -q --force FETCH_HEAD

# Dawn's CI options (.github/workflows/dawn-ci.cmake: fetch dependencies, install), the monolithic static library,
# and nothing but the library: no samples, tests or Tint tools
cmake -S "$src" -B "$build" -G Ninja -C "$src/.github/workflows/dawn-ci.cmake" \
    -DCMAKE_BUILD_TYPE=Release -DCMAKE_C_COMPILER=gcc -DCMAKE_CXX_COMPILER=g++ \
    -DDAWN_BUILD_MONOLITHIC_LIBRARY=STATIC -DDAWN_BUILD_SAMPLES=OFF -DDAWN_BUILD_TESTS=OFF -DDAWN_BUILD_BENCHMARKS=OFF \
    -DTINT_BUILD_TESTS=OFF -DTINT_BUILD_CMD_TOOLS=OFF -DTINT_BUILD_BENCHMARKS=OFF -DTINT_BUILD_IR_BINARY=OFF \
    -DDAWN_USE_WAYLAND=ON -DDAWN_USE_X11=ON
cmake --build "$build" --target webgpu_dawn

rm -rf "$out" && mkdir -p "$out/lib64"
cp "$build/src/dawn/native/libwebgpu_dawn.a" "$out/lib64/"
curl -sSfL "https://github.com/google/dawn/releases/download/$DAWN_TAG/dawn-headers-$DAWN_COMMIT.tar.gz" | tar -xz -C "$out" --strip-components=1 dawn-headers/include
tar -czf "$out.tar.gz" -C "$(dirname "$out")" "$name"
ls -l "$out.tar.gz"
