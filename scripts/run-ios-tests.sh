#!/usr/bin/env bash
# Boots an iPhone simulator, installs the iOS test runner, launches it, and checks its output for PASS.
# usage: run-ios-tests.sh <path to the .app>    (a Mac with Xcode)
set -euo pipefail
app="$1"
bundle=dev.olve.webgpu.tests

udid="$(xcrun simctl list devices available -j | python3 -c '
import json, sys
devices = json.load(sys.stdin)["devices"]
print(next(d["udid"] for runtime, ds in sorted(devices.items(), reverse=True) if "iOS" in runtime for d in ds if d["name"].startswith("iPhone")))')"
echo "simulator $udid"
xcrun simctl boot "$udid" 2>/dev/null || true
xcrun simctl bootstatus "$udid" -b
xcrun simctl install "$udid" "$app"

out="$(mktemp)"
# The app exits when its checks are done; the console stream ends with it
xcrun simctl launch --console-pty --terminate-running-process "$udid" "$bundle" | tee "$out" || true
grep -q '^PASS' "$out"
