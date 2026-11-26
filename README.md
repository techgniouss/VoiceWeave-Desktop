# VoiceWeave Desktop

Windows dictation app: hold **Ctrl+Win**, speak into any app, release, and clean text appears at your cursor. Speech recognition and cleanup run on your own computer, so your voice never leaves it.

Companion to the Android app, [VoiceWeave](https://github.com/techgniouss/VoiceWeave).

## Install

1. Download **VoiceWeaveSetup.exe** from the [latest release](https://github.com/techgniouss/VoiceWeave-Desktop/releases/latest).
2. Run it. Windows may say "unknown publisher" because the installer is not code-signed: choose **More info**, then **Run anyway**. You can compare the download with the `.sha256` file on the release page.
3. On first launch the app downloads its speech models (about 1.3 GB, checked against a fixed hash). The floating bar shows progress and then "Ready".

It installs for your user only (no administrator prompt) and starts with Windows by default (untick that box in the installer, or turn it off any time in Windows Settings > Apps > Startup, Task Manager > Startup apps, or the app's Settings). If it isn't running, open **VoiceWeave** from the Start menu and it is ready straight away.

## Use

Opening VoiceWeave shows its window: **Home** (welcome, language, a box to try dictating in), **History** (what you dictated, searchable, copy or delete, kept as text only), **Dictionary** and **Settings**. Closing the window leaves dictation running in the tray.

- **Hold Ctrl+Win**, speak, release. A quick double-tap keeps recording hands-free; tap again to finish. **Esc** cancels.
- Or use the **floating bar**: press and hold it to dictate, or click once to start and again to finish. Drag it to move it.
- It does not type into password fields or windows running as administrator, and tells you so.
- **Tray icon**: show or hide the bar, Start with Windows, edit your dictionary, open the models folder, quit.
- **Languages**: tray icon, **Language**: English, Telugu, Hindi or Tamil, or **English + Telugu / Hindi / Tamil (auto)**, which detects the language of each sentence so you can switch between them with a pause (about 160 MB extra for language detection, downloaded the first time). The Telugu, Hindi and Tamil models (about 140-190 MB each) download the first time you pick them. By default those languages are typed in **English letters** ("nenu repu udayam Hyderabad veltunnanu"); untick *Write Indian languages in English letters* in the tray menu to get Telugu, Devanagari or Tamil script instead.
- **Telugu / Hindi / Tamil missed in auto mode?** Hold **Shift** as well (Ctrl+Win+Shift) and that dictation goes straight to the regional language. Language detection cannot always tell a short phrase like "Em chesthunnav" from English.
- **Long dictation**: sentences are recognised while you speak (at each pause), so the wait after you stop stays short however long you talked.
- **Learns from your corrections**: if you fix a misheard word in the text box after dictating (for example `sri kanth` to `Srikanth`) and stop typing for a couple of seconds, the fix is added to your dictionary, so it comes out right next time. A toast tells you what it learned; **Undo last learned correction** in the tray menu removes it, and **Learn from my corrections** turns it off. It only looks at the stretch of text you just dictated, only in fields that expose their text to Windows (not password boxes or terminals), and it keeps just the old and new words, never the surrounding text. Fixes that only make sense in their own phrase (swapping one ordinary word for another) are learned once you have made the same fix twice. If you correct what a dictionary rule wrote, that rule is dropped (a toast says so, and Undo brings it back). If the automatic watch missed a fix (you edited after the 90 seconds, or rewrote the text around it), select the corrected word and press **Ctrl+Win+L**: it is matched to the closest words of your last dictation and learned at once. The field must still expose its selection to Windows.
- **Cleanup options** (both off by default, in Settings): *Match my writing style to the app* picks a relaxed style for chat apps such as Slack or Teams and a formal one for mail and documents, from the name of the program in front (nothing else is read or stored; no delay; it only changes sentences the cleanup model works on). *Improve accuracy with my vocabulary* shows the cleanup model your 20 newest dictionary terms so it spells them your way; it makes cleanup roughly 0.2 to 0.7 seconds slower and, in testing, fixed most term spellings but once dropped a "no wait" correction, so it is optional.
- **Wake word** (off by default): in Settings, turn on *Start dictating when I say a phrase* and type your own phrase (default "hey voice"; one to six plain English words). The first time it downloads a small model (about 17 MB, checked against a pinned hash, 5 MB kept). Saying the phrase starts a hands-free dictation that finishes by itself after you go quiet; **Esc** cancels and **Ctrl+Win** finishes. Listening happens on this computer only, on one background thread, and only while there is sound. If it starts by mistake, raise `WakeWordThreshold` in `settings.json`; if it misses you, lower it.
- **Dictionary**: add words it mishears, one per line, as `what it hears => what you want` (for example `quark us => Quarkus`). A term you add also catches words that sound like it: after `srikanth => Srikanth`, "Sreekant", "sri kant" and "Sri Kanth" are all written Srikanth. Terms under five letters need an exact rule, and real English words are never rewritten. It is a guess, so **Match words that sound like my dictionary terms** in Settings turns it off.

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
