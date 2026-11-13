#!/usr/bin/env bash
# Downloads the pinned Dawn release for this platform into artifacts/dawn/<rid>/.
source "$(dirname "$0")/common.sh"
if [[ -f "$DAWN_DIR/.complete" ]]; then
    echo "Dawn $DAWN_TAG ($RID) already in $DAWN_DIR"
elif [[ "$DAWN_ASSET" == android ]]; then
    mkdir -p "$DAWN_DIR/lib"
    echo "downloading dawn-headers and dawn-android ($DAWN_TAG, $ANDROID_ABI)"
    curl -sSfL "https://github.com/google/dawn/releases/download/$DAWN_TAG/dawn-headers-$DAWN_COMMIT.tar.gz" -o "$DAWN_DIR/headers.tar.gz"
    curl -sSfL "https://github.com/google/dawn/releases/download/$DAWN_TAG/dawn-android-$DAWN_COMMIT.tar.gz" -o "$DAWN_DIR/android.tar.gz"
    (cd "$DAWN_DIR" && tar -xzf headers.tar.gz --strip-components=1 dawn-headers/include && rm headers.tar.gz)
    (cd "$DAWN_DIR" && tar -xzf android.tar.gz --strip-components=2 -C lib "dawn-android-$DAWN_COMMIT/$ANDROID_ABI/libwebgpu_dawn.a" && rm android.tar.gz)
    touch "$DAWN_DIR/.complete"
else
    name="Dawn-$DAWN_COMMIT-$DAWN_ASSET-Release"
    mkdir -p "$DAWN_DIR"
    echo "downloading $name.tar.gz ($DAWN_TAG)"
    curl -sSfL "https://github.com/google/dawn/releases/download/$DAWN_TAG/$name.tar.gz" -o "$DAWN_DIR/dawn.tar.gz"
    # Relative paths: Git Bash's GNU tar reads "C:/..." as a remote host
    (cd "$DAWN_DIR" && tar -xzf dawn.tar.gz --strip-components=1 && rm dawn.tar.gz)
    touch "$DAWN_DIR/.complete"
fi
