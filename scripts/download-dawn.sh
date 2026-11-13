#!/usr/bin/env bash
# Downloads the pinned Dawn release for this platform into artifacts/dawn/<rid>/ (and DirectXShaderCompiler on Windows into artifacts/dxc/).
source "$(dirname "$0")/common.sh"
if [[ -f "$DAWN_DIR/.complete" ]]; then
    echo "Dawn $DAWN_TAG ($RID) already in $DAWN_DIR"
else
    name="Dawn-$DAWN_COMMIT-$DAWN_ASSET-Release"
    mkdir -p "$DAWN_DIR"
    echo "downloading $name.tar.gz ($DAWN_TAG)"
    curl -sSfL "https://github.com/google/dawn/releases/download/$DAWN_TAG/$name.tar.gz" -o "$DAWN_DIR/dawn.tar.gz"
    # Relative paths: Git Bash's GNU tar reads "C:/..." as a remote host
    (cd "$DAWN_DIR" && tar -xzf dawn.tar.gz --strip-components=1 && rm dawn.tar.gz)
    touch "$DAWN_DIR/.complete"
fi

if [[ "$RID" == win-x64 && ! -f "$ROOT/artifacts/dxc/.complete" ]]; then
    mkdir -p "$ROOT/artifacts/dxc"
    echo "downloading $DXC_ASSET ($DXC_TAG)"
    curl -sSfL "https://github.com/microsoft/DirectXShaderCompiler/releases/download/$DXC_TAG/$DXC_ASSET" -o "$ROOT/artifacts/dxc/dxc.zip"
    # The zip's paths use backslashes: unzip warns (exit code 1) but extracts. Its licenses are in licenses/directx-shader-compiler/.
    (cd "$ROOT/artifacts/dxc" && { unzip -q -o dxc.zip 'bin?x64?dxcompiler.dll' 'bin?x64?dxil.dll' || [[ $? -eq 1 ]]; } && rm dxc.zip)
    touch "$ROOT/artifacts/dxc/.complete"
fi
