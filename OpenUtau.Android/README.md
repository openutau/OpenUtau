# OpenUtau Android starter

This is an isolated Avalonia Android entry point and a small touch-friendly landing shell. The existing desktop app and desktop launch path are unchanged.

## Requirements

- .NET 10 SDK with the Android workload installed (`dotnet workload install android`)
- Android SDK and JDK as required by the .NET Android workload
- An Android 12+ device or emulator for the initial target

Avalonia 12 Android startup uses an `AvaloniaMainActivity` and an `[Application]` class derived from `AvaloniaAndroidApplication<TApp>`. The starter follows that pattern and assigns an `IActivityApplicationLifetime.MainViewFactory`.

## Build

From the repository root:

```sh
dotnet workload install android
dotnet build OpenUtau.Android/OpenUtau.Android.csproj -c Debug
```

To install and launch on a connected device/emulator:

```sh
dotnet build OpenUtau.Android/OpenUtau.Android.csproj -c Debug -t:Install
```

The Android project currently provides only a UI shell. It does not yet load or save `.ustx` projects, include the desktop editor controls, render vocals, or provide Android audio playback. Those require separate porting and compatibility work.

## Next engineering steps

1. Build on an Android-workload-enabled machine and fix compile or packaging issues.
2. Evaluate the core engine's native dependencies and create Android-specific audio output.
3. Design a mobile piano roll and touch editing workflow, then incrementally connect supported shared core features.
