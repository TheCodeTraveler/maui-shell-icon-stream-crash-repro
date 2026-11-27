# maui-shell-icon-stream-crash-repro

On Android, a `Shell.FlyoutIcon` or `BackButtonBehavior.IconOverride` whose `StreamImageSource` load throws crashes the whole app. `ShellToolbarTracker.UpdateLeftBarButtonItem` is `async void` and awaits `GetPlatformImageAsync` with no `try`/`catch`. MAUI also cancels its own load, so even a well-behaved source can crash.

## Repro

```bash
bash ci/build.sh && bash ci/run-cases.sh && python3 -I ci/assert-results.py --expected ci/expected.json --results artifacts
```

Needs .NET SDK 11.0.100 or newer, `dotnet workload install maui-android`, and a booted Android emulator (x86_64 or arm64). A single case by hand:

```bash
dotnet build src/Repro -c Release
adb install -r src/Repro/bin/Release/net11.0-android/com.example.shellicon-Signed.apk
adb shell am start -W -n com.example.shellicon/com.example.shellicon.MainActivity --es case flyouticon-throws
sleep 5
adb logcat -d | grep -E "ShellIconRepro|UpdateLeftBarButtonItem"
```

Cases: `control`, `control-iconoverride`, `control-image-throws`, `flyouticon-slow`, `flyouticon-throws`, `iconoverride-throws`, `flyouticon-canceled-by-maui`, and each failing case with `-safe` appended.

## Observed vs expected

- Observed: the process dies with a fatal exception thrown from `ShellToolbarTracker.UpdateLeftBarButtonItem` (`IOException: simulated offline`, or `TaskCanceledException`).
- Not a misuse of the API: `control-image-throws` shows `Image` and `ToolbarItem` survive the same throwing stream func (each has its own source), because `ImageHandler.MapSource` uses `FireAndForget` and `ImageSourcePartExtensions.UpdateSourceAsync` catches the exception for the `Image`, and `ImageSourceExtensions.LoadImage` uses `FireAndForget` for the `ToolbarItem`.
- Expected: the icon is skipped (as the bottom-tab twin does since dotnet/maui#36328) and the app keeps running.

## Versions

- `Microsoft.Maui.Controls` 11.0.10 (`MauiVersion` pin), `net11.0-android`, .NET SDK 11.0.100, CoreCLR, Release.
- Emulators: Android API 35 and 36, x86_64. The run logs the exact restored `Microsoft.Maui.Controls` version (`artifacts/versions.txt`) and the assembly informational version (`START maui=`).
- Source: `ShellToolbarTracker.cs` lines [426](https://github.com/dotnet/maui/blob/11.0.10/src/Controls/src/Core/Compatibility/Handlers/Shell/Android/ShellToolbarTracker.cs#L426) and [477](https://github.com/dotnet/maui/blob/11.0.10/src/Controls/src/Core/Compatibility/Handlers/Shell/Android/ShellToolbarTracker.cs#L477) are identical in 11.0.10 and `main` (checked 2026-11-23). 10.0.130 has the same code.

## Legs (all run in `.github/workflows/repro.yml`)

| Case | Setup | Expected |
|---|---|---|
| `control` | `FlyoutIcon` is a stream that loads fine | app stays alive |
| `control-iconoverride` | same, via `BackButtonBehavior.IconOverride` on a pushed page | app stays alive |
| `control-image-throws` | the same throwing stream func on an `Image.Source` and a `ToolbarItem.IconImageSource` of the home page (separate sources); no `FlyoutIcon`, no `IconOverride` | app stays alive, at least one `STREAM_REQUESTED source=image` and one `source=toolbaritem`, no `UNHANDLED` (MAUI's other image paths survive a throwing source; only `ShellToolbarTracker` does not) |
| `flyouticon-slow` | slow stream as `FlyoutIcon`, no color change | app stays alive, `STREAM_SERVED n=1` (the load finished without a cancel; isolates the trigger) |
| `flyouticon-throws` | stream func throws `IOException("simulated offline")` | **crash** in `UpdateLeftBarButtonItem` |
| `iconoverride-throws` | same, via `IconOverride` | **crash** |
| `flyouticon-canceled-by-maui` | slow stream; once the first load has started (`STREAM_REQUESTED n=1`) the app logs `SECOND_UPDATE` and sets `Shell.ForegroundColor`, so MAUI loads the icon a second time and cancels the first via the shared per-source token. App code never cancels. | **crash** with `TaskCanceledException`; the assert needs `STREAM_REQUESTED n=2` after `SECOND_UPDATE` and no `STREAM_SERVED n=1` |
| `*-safe` | same three, but the source is a custom `IStreamImageSource` (`SafeStreamImageSource.cs`) that catches and returns a 1x1 PNG, and uses no shared token | app stays alive; the throwing `-safe` cases need at least one `FALLBACK`; `flyouticon-canceled-by-maui-safe` needs `STREAM_REQUESTED n=2` after `SECOND_UPDATE`, `STREAM_SERVED` n=1 and n=2, and no `FALLBACK` |

CI is green only when every crash case kills the process (`process.txt` says `gone`) with the expected exception text plus an `UpdateLeftBarButtonItem` frame, and every control and `-safe` case logs `ALIVE` and `DONE` with the process still alive. Each case must also log `START` and the expected number of `STREAM_REQUESTED` lines, so a case that did not run fails. The step summary lists expected vs observed per case; logs and `results.json` are uploaded as artifacts.

Workaround leg: a subclass of `ShellToolbarTracker` cannot catch the exception, because it is raised after `async void` returns to the caller. The workaround therefore lives in the image source.

CI: https://github.com/TheCodeTraveler/maui-shell-icon-stream-crash-repro/actions/runs/45937466908 (green = every cell matched its expected result)

Known unrelated flake: on the first launch after emulator boot, Glide can re-read a closed stream (dotnet/maui#37834, fixed by dotnet/maui#38111 for the next service release). The app does not crash, a warm-up launch absorbs it, and the assert reports it as `glide-restart-37834`.

Upstream issue: (to be filed)

## Root cause and recommended fix

- `UpdateLeftBarButtonItem` awaits `image.GetPlatformImageAsync(MauiContext)` with no `try`/`catch`. `StreamImageSourceService.GetDrawableAsync` logs and rethrows. An exception in `async void` is posted to the Android `SynchronizationContext` and kills the process. Uri, file and font sources go through Glide and return `null`, so only stream sources throw.
- `StreamImageSource.GetStreamAsync` uses a per-source token that a second load cancels. Related and still open: dotnet/maui#28684.
- Fix: wrap the await in `try`/`catch`, log, and fall back to no custom icon, as dotnet/maui#36328 did for `BottomNavigationViewUtils.SetupMenu`.
