# Release Packaging

Keywise currently ships as a portable Windows build. A full installer/updater will come later.

## Local Release Package

From the repository root:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o artifacts\Keywise-win-x64
Compress-Archive -Path artifacts\Keywise-win-x64\* -DestinationPath artifacts\Keywise-win-x64.zip -Force
```

The zip contains `Keywise.exe` and can be attached to a GitHub Release.

## GitHub Release

1. Update version/release notes.
2. Commit and push all changes.
3. Create a tag:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

The release workflow builds the portable zip and uploads it as a GitHub Actions artifact. Until the app is signed and the installer exists, treat releases as testing builds.

## Before Public Releases

- Add a real installer/uninstaller.
- Add explicit startup opt-in.
- Add code signing.
- Document the privacy/security review.
- Do not claim formal pentesting until it has actually been completed.
