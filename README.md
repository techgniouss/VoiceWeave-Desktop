# VoiceWeave Desktop

Windows dictation app: hold **Ctrl+Win**, speak into any app, release, and clean text appears at your cursor. Speech recognition and cleanup run on your own computer, so your voice never leaves it.

Companion to the Android app, [VoiceWeave](https://github.com/techgniouss/VoiceWeave).

## Install

1. Download **VoiceWeaveSetup.exe** from the [latest release](https://github.com/techgniouss/VoiceWeave-Desktop/releases/latest).
2. Run it. Windows may say "unknown publisher" because the installer is not code-signed: choose **More info**, then **Run anyway**. You can compare the download with the `.sha256` file on the release page.
3. On first launch the app downloads its speech models (about 1.3 GB, checked against a fixed hash). The floating bar shows progress and then "Ready".

It installs for your user only (no administrator prompt) and does **not** start with Windows unless you turn that on in the tray menu. If it isn't running, open **VoiceWeave** from the Start menu and it is ready straight away.

## Use

- **Hold Ctrl+Win**, speak, release. A quick double-tap keeps recording hands-free; tap again to finish. **Esc** cancels.
- Or use the **floating bar**: press and hold it to dictate, or click once to start and again to finish. Drag it to move it.
- It does not type into password fields or windows running as administrator, and tells you so.
- **Tray icon**: show or hide the bar, Start with Windows, edit your dictionary, open the models folder, quit.
- **Languages**: tray icon, **Language**: English, Telugu, Hindi or Tamil, or **English + Telugu / Hindi / Tamil (auto)**, which detects the language of each sentence so you can switch between them with a pause (about 160 MB extra for language detection, downloaded the first time). The Telugu, Hindi and Tamil models (about 140-190 MB each) download the first time you pick them. By default those languages are typed in **English letters** ("nenu repu udayam Hyderabad veltunnanu"); untick *Write Indian languages in English letters* in the tray menu to get Telugu, Devanagari or Tamil script instead.
- **Long dictation**: sentences are recognised while you speak (at each pause), so the wait after you stop stays short however long you talked.
- **Dictionary**: add words it mishears, one per line, as `what it hears => what you want` (for example `quark us => Quarkus`).

## Requirements

Windows 10 or 11 (64-bit), a CPU with AVX2, about 2 GB of free RAM while dictating and 1.5 GB of disk space for the models.

## Build from source

Install the .NET 10 SDK, then:

```
dotnet test desktop/VoiceWeave.slnx
dotnet run --project desktop/VoiceWeave.App
```

Set `VOICEWEAVE_MODELS` to a folder that already holds the models to skip the download. The design is in `docs/superpowers/specs/` and the build plan in `docs/superpowers/plans/`. Pushing a tag like `v0.1.0` builds the installer in GitHub Actions and publishes it as a release.

Third-party software and model licences: `desktop/licenses/THIRD-PARTY.txt`.

## Licence

MIT, see `LICENSE`.
