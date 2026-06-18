# Release Packaging

Keywise ships through GitHub Releases. The primary download is an Inno Setup installer named `Keywise-Setup-<version>.exe`.

## Local Release Package

From the repository root, create the portable build:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o artifacts\Keywise-win-x64
Compress-Archive -Path artifacts\Keywise-win-x64\* -DestinationPath artifacts\Keywise-win-x64.zip -Force
```

If Inno Setup 6 is installed locally, create the installer:

```powershell
iscc installer\Keywise.iss /DAppVersion=0.1.0
```

## GitHub Release

1. Update the version in `DesktopUsageAnalytics.csproj`.
2. Commit and push all changes.
3. Create a tag:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

The release workflow builds:

- `Keywise-Setup-<version>.exe`
- `Keywise-win-x64.zip`

It also creates the GitHub Release and attaches both assets.

## Upgrade Behavior

The installer uses a stable Inno Setup `AppId`, so running a newer installer over an older install upgrades the app in place. It installs to:

```text
%LOCALAPPDATA%\Programs\Keywise\
```

Local usage data is stored separately and is preserved across upgrades and uninstall:

```text
%LOCALAPPDATA%\Keywise\
```

The app includes an opt-in update check button. It contacts GitHub only when the user clicks it, then opens the GitHub release page if a newer version is available.

## Before Public Releases

- Add code signing.
- Document the privacy/security review.
- Do not claim formal pentesting until it has actually been completed.
