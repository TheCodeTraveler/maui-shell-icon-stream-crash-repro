#!/usr/bin/env bash
# One launch per case. A crash in one case must not stop the others, so there is no `set -e`.
set -u -o pipefail

package=com.example.shellicon
artifacts="artifacts/cases"
mkdir -p "$artifacts"

adb logcat -G 16M
adb shell settings put global hide_error_dialogs 1

apk=$(ls src/Repro/bin/Release/net11.0-android/*-Signed.apk | head -1)
adb install -r "$apk" > artifacts/install.txt 2>&1 || { sleep 10; adb install -r "$apk" > artifacts/install.txt 2>&1; } \
    || { echo "Install failed"; cat artifacts/install.txt; exit 1; }

wait_for_end() {
    local waited=0
    while [ "$waited" -lt 150 ]; do
        adb logcat -d -s ShellIconRepro:I | grep -q ' DONE ' && return 0
        if [ "$waited" -gt 15 ] && [ -z "$(adb shell pidof "$package" | tr -d '\r')" ]; then
            echo "Process gone without DONE: it crashed."
            return 1
        fi
        sleep 2
        waited=$((waited + 2))
    done
    echo "Timed out."
    return 1
}

# The first launch after emulator boot can restart the activity, which makes Glide re-read a closed stream (dotnet/maui#37834).
# One unasserted launch absorbs that.
warmup="artifacts/warmup"
mkdir -p "$warmup"
adb logcat -c
adb shell am start -W -n "$package/com.example.shellicon.MainActivity" --es case control > "$warmup/am-start.txt" 2>&1
wait_for_end > "$warmup/wait.txt" 2>&1
adb logcat -d > "$warmup/logcat.txt"
adb shell am force-stop "$package"
sleep 3

for case_name in control control-iconoverride control-image-throws flyouticon-slow \
    flyouticon-throws iconoverride-throws flyouticon-canceled-by-maui \
    flyouticon-throws-safe iconoverride-throws-safe flyouticon-canceled-by-maui-safe; do
    folder="$artifacts/$case_name"
    mkdir -p "$folder"

    adb logcat -c
    adb logcat -b crash -c
    adb shell am start -W -n "$package/com.example.shellicon.MainActivity" --es case "$case_name" > "$folder/am-start.txt" 2>&1

    wait_for_end > "$folder/wait.txt" 2>&1
    if [ -z "$(adb shell pidof "$package" | tr -d '\r')" ]; then echo gone > "$folder/process.txt"; else echo alive > "$folder/process.txt"; fi
    echo "$case_name: $(cat "$folder/wait.txt")"

    adb logcat -d > "$folder/logcat.txt"
    adb logcat -d -s ShellIconRepro:I > "$folder/repro.txt"
    adb logcat -d -b crash > "$folder/crash.txt"
    adb shell am force-stop "$package"
done

echo "emulator API level $(adb shell getprop ro.build.version.sdk | tr -d '\r')" >> artifacts/versions.txt
