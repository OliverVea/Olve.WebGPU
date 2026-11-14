# Shared by the scripts: the Dawn pin, this machine's runtime identifier and the artifacts layout.
# RID overrides the detected runtime identifier (e.g. RID=osx-x64 on an Intel Mac, RID=android-arm64 for Android).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# Plain KEY=value lines; eval rather than "source <(...)", which bash 3.2 (macOS) reads as empty
eval "$(tr -d '\r' < "$ROOT/dawn/version.env" | grep -E '^[A-Z_]+=')"
if [[ -z "${RID:-}" ]]; then
    case "$(uname -s)-$(uname -m)" in
        Linux-x86_64) RID=linux-x64 ;;
        Linux-aarch64) RID=linux-arm64 ;;
        Darwin-arm64) RID=osx-arm64 ;;
        Darwin-x86_64) RID=osx-x64 ;;
        MINGW*|MSYS*|CYGWIN*) RID=win-x64 ;;
        *) echo "unsupported platform $(uname -s)-$(uname -m)" >&2; exit 1 ;;
    esac
fi
case "$RID" in
    win-x64) DAWN_ASSET=windows-latest ;;
    linux-x64) DAWN_ASSET=ubuntu-latest ;;
    # Not in Dawn's releases: built from source by scripts/build-dawn.sh and published as a release of this repo (dawn.yml)
    linux-arm64) DAWN_ASSET=linux-arm64; DAWN_RELEASE_URL="https://github.com/OliverVea/Olve.WebGPU/releases/download/dawn-$DAWN_COMMIT" ;;
    osx-arm64) DAWN_ASSET=macos-latest ;;
    osx-x64) DAWN_ASSET=macos-15-intel ;;
    # Android: one archive with a static library per ABI, and the headers from the release's headers archive
    android-arm64) DAWN_ASSET=android; ANDROID_ABI=arm64-v8a; ANDROID_TRIPLE=aarch64-linux-android ;;
    android-x64) DAWN_ASSET=android; ANDROID_ABI=x86_64; ANDROID_TRIPLE=x86_64-linux-android ;;
    # iOS: static libraries from the release's Apple xcframework. iossimulator is one library for both simulator architectures.
    ios-arm64) DAWN_ASSET=apple; APPLE_SLICE=ios-arm64; APPLE_SDK=iphoneos; APPLE_TARGET=arm64-apple-ios14.0 ;;
    iossimulator) DAWN_ASSET=apple; APPLE_SLICE=ios-arm64_x86_64-simulator; APPLE_SDK=iphonesimulator; APPLE_TARGET="arm64-apple-ios14.0-simulator x86_64-apple-ios14.0-simulator" ;;
    *) echo "unsupported RID $RID" >&2; exit 1 ;;
esac
DAWN_RELEASE_URL="${DAWN_RELEASE_URL:-https://github.com/google/dawn/releases/download/$DAWN_TAG}"
# Dawn's own Android build targets API 26 (google/dawn .github/workflows/ci.yml, NDK r27d)
ANDROID_API=26
DAWN_DIR="$ROOT/artifacts/dawn/$RID"
NATIVE_DIR="$ROOT/artifacts/native/$RID"
LAYOUT_DIR="$ROOT/artifacts/layout-check/$RID"
PYTHON="${PYTHON:-python3}"
