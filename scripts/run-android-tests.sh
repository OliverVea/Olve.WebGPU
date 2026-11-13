#!/usr/bin/env bash
# Installs the Android test runner on the connected device or emulator, starts it, and waits for PASS or FAIL in logcat.
# usage: run-android-tests.sh <apk>    (adb on PATH, or ANDROID_HOME set)
set -euo pipefail
apk="$1"
adb="${ANDROID_HOME:+$ANDROID_HOME/platform-tools/}adb"
package=dev.olve.webgpu.tests

"$adb" install -r "$apk"
"$adb" logcat -c
"$adb" shell am start -n "$package/$("$adb" shell cmd package resolve-activity --brief "$package" | tail -1 | tr -d '\r' | cut -d/ -f2)"

# The checks log to the tag OlveWebGPU; the last line is PASS or FAIL (n)
for _ in $(seq 1 150); do
    if "$adb" logcat -d -s 'OlveWebGPU:*' | grep -qE ' (PASS|FAIL)'; then
        break
    fi
    sleep 2
done

"$adb" logcat -d -s 'OlveWebGPU:*' 'AndroidRuntime:E' 'DOTNET:E' | grep -v '^-----'
"$adb" logcat -d -s 'OlveWebGPU:*' | grep -q ' PASS$'
