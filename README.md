# Desktop Usage Analytics

Privacy-first Windows desktop usage analytics prototype.

## Current Status

This is an AI-generated prototype and has not yet completed a formal penetration test.

The app currently includes:

- WPF dashboard
- Tray icon
- Enable/pause tracking toggle
- Local JSON aggregate storage
- Aggregate key and mouse counter model
- Active tracking time and session duration
- Privacy and settings screens
- Simulator buttons for validating the counter model before global hooks are added

## Privacy Model

The app is designed to store aggregate counters only. It must not store typed text, ordered key sequences, per-key timestamps, window titles, app names, URLs, clipboard data, screenshots, mouse coordinates, or raw input logs.

## Build

```powershell
dotnet build
```

## Run Prototype

```powershell
dotnet run
```

## Planned Install Layout

```text
%LOCALAPPDATA%\Programs\DesktopUsageAnalytics\
%LOCALAPPDATA%\DesktopUsageAnalytics\
```

Startup should be disabled by default and enabled only by explicit user choice.
