# Shared by the scripts: the Dawn pin, this machine's runtime identifier and the artifacts layout.
# RID overrides the detected runtime identifier (e.g. RID=osx-x64 on an Intel Mac).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=/dev/null
source <(tr -d '\r' < "$ROOT/dawn/version.env")
if [[ -z "${RID:-}" ]]; then
    case "$(uname -s)-$(uname -m)" in
        Linux-x86_64) RID=linux-x64 ;;
        Darwin-arm64) RID=osx-arm64 ;;
        Darwin-x86_64) RID=osx-x64 ;;
        MINGW*|MSYS*|CYGWIN*) RID=win-x64 ;;
        *) echo "unsupported platform $(uname -s)-$(uname -m)" >&2; exit 1 ;;
    esac
fi
case "$RID" in
    win-x64) DAWN_ASSET=windows-latest ;;
    linux-x64) DAWN_ASSET=ubuntu-latest ;;
    osx-arm64) DAWN_ASSET=macos-latest ;;
    osx-x64) DAWN_ASSET=macos-15-intel ;;
esac
DAWN_DIR="$ROOT/artifacts/dawn/$RID"
NATIVE_DIR="$ROOT/artifacts/native/$RID"
LAYOUT_DIR="$ROOT/artifacts/layout-check/$RID"
PYTHON="${PYTHON:-python3}"
