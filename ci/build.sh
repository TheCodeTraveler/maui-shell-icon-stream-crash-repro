#!/usr/bin/env bash
set -uo pipefail

artifacts="artifacts"
mkdir -p "$artifacts"

extra_arguments=()
if [ -n "${ANDROID_HOME:-}" ]; then
    extra_arguments+=("-p:AndroidSdkDirectory=$ANDROID_HOME")
fi

dotnet build src/Repro -c Release -p:RuntimeIdentifier=android-x64 ${extra_arguments[@]+"${extra_arguments[@]}"} \
    -bl:"$artifacts/build.binlog" | tee "$artifacts/build.txt"
if [ "${PIPESTATUS[0]}" -ne 0 ]; then
    echo "::error::Build failed."
    exit 1
fi

apk=$(ls src/Repro/bin/Release/*/android-x64/*-Signed.apk | head -1)
echo "APK: $apk"
dotnet --version > "$artifacts/versions.txt"
