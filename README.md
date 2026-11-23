# maui-shell-icon-stream-crash-repro

On Android, a `Shell.FlyoutIcon` or `BackButtonBehavior.IconOverride` whose `StreamImageSource` load throws crashes the whole app. `ShellToolbarTracker.UpdateLeftBarButtonItem` is `async void` and awaits `GetPlatformImageAsync` with no `try`/`catch`. MAUI also cancels its own load, so even a well-behaved source can crash.

## Repro

```bash
dotnet build src/Repro -c Release -p:RuntimeIdentifier=android-x64
adb install -r src/Repro/bin/Release/net11.0-android/android-x64/com.example.shellicon-Signed.apk
adb shell am start -n com.example.shellicon/com.example.shellicon.MainActivity --es case flyouticon-throws
adb logcat -d | grep -E "ShellIconRepro|UpdateLeftBarButtonItem"
```

Cases: `control`, `control-iconoverride`, `flyouticon-throws`, `iconoverride-throws`, `flyouticon-canceled-by-maui`, and each failing case with `-safe` appended.

## Observed vs expected

- Observed: the process dies with a fatal exception thrown from `ShellToolbarTracker.UpdateLeftBarButtonItem` (`IOException: simulated offline`, or `TaskCanceledException`).
- Expected: the icon is skipped (as the bottom-tab twin does since dotnet/maui#36328) and the app keeps running.

## Versions

- `Microsoft.Maui.Controls` 11.0.10 (`MauiVersion` pin), `net11.0-android`, .NET SDK 11.0.100, CoreCLR, Release.
- Emulators: Android API 35 and 36, x86_64.
- Source: `ShellToolbarTracker.cs` lines 426 and 477 are identical in 11.0.10 and `main` (checked 2026-11-23). 10.0.130 has the same code.

## Legs (all run in `.github/workflows/repro.yml`)

| Case | Setup | Expected |
|---|---|---|
| `control` | `FlyoutIcon` is a stream that loads fine | app stays alive |
| `control-iconoverride` | same, via `BackButtonBehavior.IconOverride` on a pushed page | app stays alive |
| `flyouticon-throws` | stream func throws `IOException("simulated offline")` | **crash** in `UpdateLeftBarButtonItem` |
| `iconoverride-throws` | same, via `IconOverride` | **crash** |
| `flyouticon-canceled-by-maui` | slow stream; app sets `Shell.ForegroundColor` after 500 ms, so MAUI loads the icon a second time and cancels the first via the shared per-source token. App code never cancels. | **crash** with `TaskCanceledException` |
| `*-safe` | same three, but the source is a custom `IStreamImageSource` (`SafeStreamImageSource.cs`) that catches and returns a 1x1 PNG, and uses no shared token | app stays alive |

CI is green only when every crash case crashes with the expected exception text plus an `UpdateLeftBarButtonItem` frame, and every control and `-safe` case logs `ALIVE` and `DONE`. Each case must also log `START` and the expected number of `STREAM_REQUESTED` lines, so a case that did not run fails. The step summary lists expected vs observed per case; logs and `results.json` are uploaded as artifacts.

Fix leg: a subclass of `ShellToolbarTracker` cannot catch the exception, because it is raised after `async void` returns to the caller. The workaround therefore lives in the image source.

CI: (run link added after the first run)

Upstream issue: (to be filed)

## Root cause and recommended fix

- `UpdateLeftBarButtonItem` awaits `image.GetPlatformImageAsync(MauiContext)` with no `try`/`catch`. `StreamImageSourceService.GetDrawableAsync` logs and rethrows. An exception in `async void` is posted to the Android `SynchronizationContext` and kills the process. Uri, file and font sources go through Glide and return `null`, so only stream sources throw.
- `StreamImageSource.GetStreamAsync` uses a per-source token that a second load cancels. Related and still open: dotnet/maui#28684.
- Fix: wrap the await in `try`/`catch`, log, and fall back to no custom icon, as dotnet/maui#36328 did for `BottomNavigationViewUtils.SetupMenu`.
