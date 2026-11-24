# VoiceWeave Desktop (Windows) - Design

Status: draft for approval. Date: 2026-11-24. Author's calls are marked **[call]**; everything else comes from the user or from measurements.

## Intent and why

The user wants a personal Windows dictation app that does what Wispr Flow does, as fast and accurately as possible, and then more. Hold a key, speak into any text field in any app, release, and clean text appears: punctuated, with fillers removed and spoken corrections applied. It is for the user alone (no distribution, no accounts, no auto-update server).

It extends the existing Android VoiceWeave (offline sherpa-onnx packs for Telugu, Hindi, Tamil; dictionary; history; app exclusions) to the desktop.

## Goals

1. Hold-to-talk dictation into any Windows app (editors, browsers, chat, terminals) with the text inserted at the cursor.
2. English accuracy and speed good enough to replace Wispr daily on the user's laptop (i7-8650U, 4 cores, 7.9 GB RAM, no GPU).
3. Telugu, Hindi, Tamil dictation, and English mixed with each, using the packs already proven in the Android app.
4. Clean output: punctuation, capitalization, filler removal, self-correction ("John, no wait, Maria" gives "Maria"), numbers/emails/URLs, per-app tone.
5. Personal vocabulary that actually fixes names and terms (Srikanth, Quarkus, Aurora PostgreSQL, Miyapur).
6. Everything Wispr's desktop client has (feature table below), plus offline operation, Indic languages, and a built-in accuracy check.
7. A measured accuracy/speed harness, so "works accurately and fast" is a number.

## Non-goals and anti-goals

- No cloud service, account system, sync, team features, or analytics. **[call]** Offline-only for v1; the user chose "best small models" rather than a cloud path. An optional cloud engine can be added later behind the same engine interface.
- No macOS/Linux/Android in this project. No store packaging or auto-update.
- Anti-goals: text inserted into the wrong window; the clipboard left clobbered; the first word of an utterance lost; cleanup that changes a name or answers a dictated question; password fields ever receiving dictation.

## Evidence from the Wispr Flow teardown

Wispr Flow 1.6.1609 (unpacked statically; nothing run). Electron app plus a NativeAOT C# helper. The helper uses a low-level keyboard hook and raw input, UI Automation (focus-change events, TextPattern, LegacyIAccessible), clipboard save / verified write / `SendInput` paste / restore (marked as sensitive content), and WASAPI audio sessions. The Electron side shows an always-on-top click-through bar, a drag overlay, tray, login item, single-instance lock, Opus recording, SQLite history and dictionary, and fastText language ID. Transcription and formatting are server-side. REA was not run (needs Node 24.22.1+). We copy the capability list, not code, assets, or branding.

## Measured on the user's laptop (sherpa-onnx int8, 4 threads, real voice)

| Stage | Choice | Result |
|---|---|---|
| English ASR | Parakeet TDT 0.6B v2 | WER 21.8% on 11 phrases (errors mostly names/terms), avg decode 0.57 s; Moonshine base was 42.7% |
| Telugu | IndicConformer pack | CER 2.2%, RTF 0.04 |
| Hindi | IndicConformer pack | CER 17.6%, RTF 0.33 (slowest) |
| Tamil | IndicConformer pack | CER 26.8%, RTF 0.05 |
| Cleanup (English) | Spokenly Cleanup 1 (Qwen3-0.6B, Q8, Apache-2.0) | 0.5-1.7 s per phrase, avg 0.8 s; fixed corrections/fillers/numbers/emails; altered one misheard name |

Estimated end-to-end after key release for English: about 1.3-2.3 s unoptimised. Caveats: small test sets, some synthetic audio early on, first word lost in one recording. Hindi and cleanup speed need work.

## Revision 2026-11-24 (supersedes the Tauri choice and the "personal only" scope below)

The user will share the app with family and publish it from a public GitHub repository. Decisions, all confirmed by the user in chat except where marked **[call]**:

- **Stack: .NET (WPF), not Tauri.** Speed and accuracy come from the native models, so they are identical across stacks. .NET avoids the multi-GB Visual Studio C++/Rust toolchain: sherpa-onnx (NuGet `org.k2fsa.sherpa.onnx`) and llama.cpp (**[call]** LLamaSharp CPU backend) ship prebuilt native libraries. First build task verifies the llama.cpp backend runs on the user's CPU (an earlier prebuilt build crashed with an illegal-instruction error).
- **Distribution:** public repository `techgniouss/VoiceWeave` (currently the Android app) or a `desktop/` folder in it **[call]**. GitHub Actions on `windows-latest` builds the app and installer and attaches it to a GitHub Release. Family downloads only the installer (about 40-60 MB); models download on first launch (English about 1.35 GB; Indic packs optional, 138-186 MB each).
- **Startup behaviour:** the app must NOT force itself to start at login. It offers an optional "Start with Windows" switch that uses the standard Windows startup-app mechanism, so it appears in Windows Settings > Startup apps and the user can disable it there. If it is not running at login, launching it from the Start menu or a shortcut must bring it fully up with no extra clicks: tray icon, hotkey active, floating bar ready. Single-instance: a second launch just activates the first.
- **Sharing implications:** unsigned installer triggers a SmartScreen warning (code-signing is a paid option, deferred); a first-launch hardware check (AVX2, RAM, free disk) selects a lighter model set or warns; licences (Parakeet CC-BY-4.0 credit, Cleanup 1 Apache-2.0, IndicConformer MIT) shipped in an About/licences file; no Wispr assets or branding; models, recordings and the `wispr-analysis` folder are never committed; secret scan before every commit; updates are manual reinstall from Releases for v1, with an optional new-version check later **[call]**.
- Non-goal "no distribution/auto-update server" is relaxed to: no update server, but GitHub Releases distribution is in scope.

Read the Tauri/Rust references in the sections below as .NET equivalents: Rust crates become .NET projects, the Tauri shell becomes a WPF tray app, WebView settings become WPF windows.

## Approaches considered

1. **Tauri (Rust) single process - chosen.** Hook, UI Automation, paste, audio and inference in one native process; roughly 30-60 MB idle. Cost: Rust for the native glue; Tauri's global-shortcut plugin cannot do hold-to-talk, so the keyboard hook is custom.
2. Electron plus native helper (Wispr's own shape). Heavy (hundreds of MB idle), two processes and IPC, no accuracy benefit.
3. .NET WPF. Equally capable and the closest to Wispr's helper; rejected only because the user asked Tauri vs Electron and Tauri fits. **[call]** Revisit if Rust FFI to sherpa-onnx or llama.cpp proves painful.

The user said they don't mind which; the stack is therefore my call.

## Design

### Pipeline
hotkey down, start capture (with ~500 ms pre-roll ring buffer), live waveform; hotkey up, stop; VAD trim; language route; ASR; dictionary/fuzzy correction; cleanup; insert; save history.

- **Audio:** WASAPI capture at 16 kHz mono, always-armed ring buffer only while the hotkey could fire, so the first word is never lost. Optional ducking of other audio via WASAPI sessions.
- **Engines (sherpa-onnx C API from Rust):** English Parakeet v2 (offline, decode on release, chunked for long audio). Telugu/Hindi/Tamil via their packs (reuse the Android pack URLs and SHA-256 pins; Hindi metadata patch). Multilingual and mixed speech: reuse the Whisper-small pack and the Android routing logic as a later milestone. Model files download once with SHA verification into `%LOCALAPPDATA%`.
- **Dictionary:** exact and fuzzy (phonetic/edit-distance) replacement before cleanup, plus text hints to the cleanup model. Same `heard => preferred` file format as Android.
- **Cleanup:** llama.cpp (built with AVX2, bundled) running Cleanup 1 for English. Bypassed for short, already-clean text (speed). For Indic text: rules only (punctuation, number normalization); optional romanization toggle. Never given instructions as input (prompt-injection safe by model design, verified in tests).
- **Insertion:** primary = clipboard save, set text, verify, `SendInput` Ctrl+V (Ctrl+Shift+V in terminals), restore clipboard, marking the copy as sensitive. UI Automation to read the focused element, detect password fields (never dictate there), and get text before/after the cursor for tone and spacing. Fallback = typed `SendInput` for apps that block paste.

### Hotkeys and modes
Hold-to-talk (default Right Ctrl, any key incl. modifier-only), toggle, and hands-free (double-tap to latch), Esc to cancel. Custom low-level keyboard hook; must not swallow keys it doesn't own.

### Features (Wispr parity, then more)
Parity: floating status bar and tray with mic/language selection; auto-punctuation/capitalization; filler removal; self-correction; personal dictionary; snippets (voice shortcut expands to text); per-app tone/style; history with re-copy and retry; command mode (select text, speak an edit); whisper/quiet mode via input gain; microphone picker; start with Windows; single instance; app exclusion list; usage stats.
Beyond Wispr: fully offline; Telugu/Hindi/Tamil and mixed English; transcript correction learning (port Android `CorrectionLearning`); adjustable romanization of Indic output; the built-in benchmark (below).

### UI
Settings window (WebView): general, hotkeys, languages/models, dictionary, snippets, per-app styles, history, exclusions, diagnostics. Overlay: small transparent, click-through, no-focus-steal window near the bottom of the active screen.

### Quality harness
A `bench` command and test suite: reuse `dictation-sample.csv` and `score-dictation.py`; record-and-score script (already prototyped in `wispr-analysis`); report WER/CER, term accuracy, decode time, and total key-release-to-text latency. Release gate: targets below must pass on the user's laptop.

### Targets (proposed, to be confirmed by measurement) **[call]**
Self-review fix: the first draft said 1.2 s for English with cleanup, which the measurements (about 0.6 s ASR + 0.8 s cleanup) cannot meet. Targets are now split by path, for phrases under 6 s on the user's laptop, p50 from key release to text:
- English, cleanup skipped (short or already clean): at most 1.0 s.
- English with cleanup: at most 2.0 s at first build, then optimised toward 1.5 s (skip rules, running cleanup on streamed partial text, smaller quantisation).
- Telugu and Tamil: at most 1.0 s. Hindi: at most 2.0 s.
- Accuracy: dictionary terms 95% or better once added; zero lost first words in the recorder test; no clipboard loss across 100 consecutive inserts.
If a target cannot be met on this CPU, the engine interface allows swapping in a faster model or an optional cloud engine without redesign.

### Security and privacy
Audio never leaves the machine and is not stored by default; history stores text only, with a retention setting and a clear button. Password/sensitive-field detection and an app exclusion list (banking apps, password managers). No telemetry.

## Constraints and risks

- CPU-only laptop with 8 GB RAM: Parakeet (about 650 MB) plus the cleanup model (about 640 MB) plus Indic packs must not be resident together; load on demand and unload after idle.
- Windows blocks injection into elevated windows unless the app runs elevated; documented limitation.
- Antivirus and anti-cheat software may flag global hooks.
- llama.cpp builds: the newest `llama-cpp-python` crashed with an illegal-instruction error on this CPU, so bundle a self-built AVX2 llama.cpp.
- Hindi pack speed and Tamil accuracy are weaker; may need tuning or alternative models.
- Cleanup can alter misheard names; mitigate with dictionary-first and a "protected terms" list passed to the model and verified after.
- Model licences: Cleanup 1 is Apache-2.0. S1-mini has a custom licence and is not adopted. IndicConformer packs are MIT.

## Build prerequisites (found in self-review)

This machine has Node but no Rust, no MSVC C++ Build Tools, no CMake, and an unknown WebView2 state. Tauri on Windows needs Rust (MSVC target), Visual Studio Build Tools with the C++ workload (several GB), CMake (to build llama.cpp with AVX2) and WebView2 (preinstalled on Windows 11 and recent Windows 10). The user must approve these installs before any code is built. sherpa-onnx's prebuilt Windows libraries are MSVC builds, so the MinGW toolchain already on the machine is not a substitute.

## Left to the builder

Project layout, Rust crates (`windows`, sherpa-onnx bindings or direct C FFI), model download/unload policy, VAD choice, exact hotkey defaults, overlay look, per-app style presets, how Hindi speed is fixed, optional streaming-overlap optimisations, test fixtures. Build order proposal: (1) hook, audio, overlay, insertion with a fake transcript; (2) Parakeet English end to end; (3) dictionary and cleanup; (4) Indic engines and routing; (5) history/snippets/command mode/per-app tone; (6) benchmark harness and polish.

## Open items (user's call before or during build)

None blocking. Confirm: offline-only v1; Windows 10/11 only; Right Ctrl as the default hold key.
