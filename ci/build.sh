#!/usr/bin/env bash
set -uo pipefail

artifacts="artifacts"
mkdir -p "$artifacts"

extra_arguments=()
if [ -n "${ANDROID_HOME:-}" ]; then
    extra_arguments+=("-p:AndroidSdkDirectory=$ANDROID_HOME")
fi

dotnet build src/Repro -c Release ${extra_arguments[@]+"${extra_arguments[@]}"} \
    -bl:"$artifacts/build.binlog" | tee "$artifacts/build.txt"
if [ "${PIPESTATUS[0]}" -ne 0 ]; then
    echo "::error::Build failed."
    exit 1
fi

apk=$(ls src/Repro/bin/Release/net11.0-android/*-Signed.apk 2>/dev/null | head -1)
if [ -z "$apk" ]; then
    echo "::error::No signed APK found."
    exit 1
fi
echo "APK: $apk"
{
    echo "dotnet: $(dotnet --version)"
    echo "maui-controls: $(python3 -I -c "import json; print(next((k.split('/')[1] for k in json.load(open('src/Repro/obj/project.assets.json'))['libraries'] if k.lower().startswith('microsoft.maui.controls/')), '?'))")"
} > "$artifacts/versions.txt"
