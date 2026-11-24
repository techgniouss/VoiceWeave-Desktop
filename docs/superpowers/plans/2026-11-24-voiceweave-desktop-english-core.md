# VoiceWeave Desktop (.NET) - English Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows tray app where holding Right Ctrl dictates English into any app, with dictionary correction and cleanup, a benchmark that measures accuracy and latency, and a GitHub Actions release that produces an installer.

**Architecture:** A .NET solution under `desktop/`. `VoiceWeave.Core` is platform-free logic (dictionary, cleanup gate, scoring, hotkey state machine, pre-roll), fully unit-tested. `VoiceWeave.Win` wraps Windows APIs behind interfaces (audio, keyboard hook, clipboard/paste, UI Automation). `VoiceWeave.Engine` wraps sherpa-onnx (Parakeet) and llama.cpp (cleanup) plus the model downloader. `VoiceWeave.App` is the WPF tray app (overlay, tray, settings, pipeline). `VoiceWeave.Bench` is the benchmark CLI.

**Tech Stack:** .NET 10 (C#), WPF, xUnit, NuGet `org.k2fsa.sherpa.onnx`, LLamaSharp with the CPU backend, `Microsoft.Data.Sqlite`, GitHub Actions on `windows-latest`.

**Spec:** `docs/superpowers/specs/2026-11-24-voiceweave-desktop-design.md` (see its 2026-11-24 revision).

**Scope:** English end to end plus release. Indic engines/routing, snippets, command mode, per-app tone, correction learning get their own later plans on these interfaces.

## Global Constraints

- Windows 10/11 x64. The user's laptop: i7-8650U, 4 cores, 7.9 GB RAM, no GPU. llama.cpp must run there (an earlier prebuilt build crashed with an illegal-instruction error); task 2 proves this before anything depends on it.
- Offline except one-time model downloads, SHA-256 verified. Models live in `%LOCALAPPDATA%\VoiceWeave\models`; never committed.
- English engine: Parakeet TDT 0.6B v2 int8 (sherpa-onnx), 16 kHz mono f32. Cleanup: Spokenly Cleanup 1 Q8 GGUF (Apache-2.0), prompt `tone: <t>\ntext: <text>\nout:\n`.
- Never lose the first word (500 ms pre-roll). Never dictate into password fields. No clipboard loss. Audio is not stored; history stores text only.
- Latency targets for phrases under 6 s, p50 from key release to text: English without cleanup at most 1.0 s; with cleanup at most 2.0 s.
- Default hold key Right Ctrl; Esc cancels. Do NOT auto-register for Windows startup by default. An optional "Start with Windows" switch uses the standard per-user startup mechanism so it shows in Windows Settings > Startup apps and can be disabled there. Launching the app manually must bring it fully up (tray icon, hotkey active, bar ready) with no extra clicks. Second launch activates the running instance.
- Public repo hygiene: no secrets, models, recordings or `wispr-analysis` content committed; scan staged diff before every commit. No Wispr assets or branding. Licences for Parakeet (CC-BY-4.0), Cleanup 1 (Apache-2.0), sherpa-onnx, LLamaSharp shipped in a `licenses` file.

## Review Focus

- Clipboard holds an image or files when dictation fires: restore every format exactly, or skip and say so.
- Target window is elevated (admin): insertion fails visibly, never silently or into the wrong window.
- Silence or a sub-200 ms key tap: no model call, no text, no stray paste.
- Recording over 60 s: chunked decoding, no out-of-memory, no truncated tail.
- Hotkey released while a model is still loading: the utterance is queued and inserted, not dropped.

## File Structure

- `desktop/VoiceWeave.sln`
- `desktop/VoiceWeave.Core/{Dictionary,CleanupGate,Scoring,HotkeyMachine,PreRoll}.cs`
- `desktop/VoiceWeave.Win/{WasapiMic,KeyboardHook,Insertion,FocusInfo,Startup}.cs`
- `desktop/VoiceWeave.Engine/{ModelManager,Parakeet,LlamaCleaner}.cs`
- `desktop/VoiceWeave.App/` (WPF: `App.xaml`, `Pipeline.cs`, `Overlay.xaml`, `Tray.cs`, `SettingsWindow.xaml`)
- `desktop/VoiceWeave.Bench/Program.cs`
- `desktop/tests/VoiceWeave.*.Tests/`
- `.github/workflows/desktop-release.yml`

---

### Task 1: Solution scaffold

**Prerequisite:** .NET 10 SDK installed (winget `Microsoft.DotNet.SDK.10`, user-approved). Verify with `dotnet --version` printing 10.x.

**Files:** Create the solution, the five projects, three test projects, `desktop/.gitignore` (`bin/`, `obj/`, `*.onnx`, `*.gguf`, `*.wav`).

**Interfaces:** Produces project names above.

- [ ] **Step 1:** Create projects; add a test `Workspace_Builds` asserting true in `VoiceWeave.Core.Tests`.
- [ ] **Step 2:** Run `dotnet test desktop/VoiceWeave.sln`. Expected: 1 passed.
- [ ] **Step 3:** Commit `chore: scaffold desktop solution`.

### Task 2: Prove llama.cpp runs on this CPU

**Files:** Create `desktop/VoiceWeave.Bench/LlamaProbe.cs`.

**Interfaces:** Produces `bench llama-probe <gguf>` that loads the Cleanup 1 GGUF with LLamaSharp's CPU backend (AVX2 build), runs the prompt `tone: casual\ntext: um so i think we should meet on friday at three pm\nout:\n`, and prints output and elapsed time.

- [ ] **Step 1:** Implement the probe; reference the CPU backend package.
- [ ] **Step 2:** Run `dotnet run --project desktop/VoiceWeave.Bench -- llama-probe <path to cleanup-1-q8_0.gguf>` (the file is already in `wispr-analysis/models/cleanup`). Expected: a cleaned sentence containing "3 pm" in under 2 s, no crash.
- [ ] **Step 3:** If it crashes with an illegal-instruction error, stop and pick a fallback (self-compiled llama.cpp AVX2 DLL) before continuing; record the finding in the spec. Commit `feat(bench): llama.cpp probe`.

### Task 3: Dictionary with fuzzy matching

**Files:** Create `VoiceWeave.Core/Dictionary.cs`; test `Dictionary.Tests.cs`.

**Interfaces:** Produces `sealed class Dictionary { static Dictionary Parse(string text); string Apply(string text); IReadOnlyList<string> Terms { get; } }`. File lines `heard => preferred`, `#` comments ignored.

- [ ] **Step 1:** Tests: `ExactPhrase_ReplacedCaseInsensitively` ("quark us deployed" with `quark us => Quarkus`); `Fuzzy_SingleWord_WithinDistanceTwo` ("Srikunth" to "Srikanth"); `Fuzzy_RequiresSameFirstLetter_AndLengthWithinOne` (no change for "Sri", "Srikanthan"); `NoReplacementInsideLongerWords`; `MultiWordPhrase` (`sri kantur => Srikanth`).
- [ ] **Step 2:** Run `dotnet test --filter Dictionary`. Expected: FAIL.
- [ ] **Step 3:** Implement: exact whole-word/phrase pass (longest phrase first), then fuzzy pass (Damerau-Levenshtein, words of 4+ letters, guards above).
- [ ] **Step 4:** Run; expected PASS. Commit `feat(core): personal dictionary`.

### Task 4: Cleanup gate and protected terms

**Files:** Create `VoiceWeave.Core/CleanupGate.cs`.

**Interfaces:** Produces `static bool NeedsCleanup(string text)`, `static string StripNoise(string text)` (standalone "Hmm", "Mm-hmm", "uh", "um"), `static bool ProtectedTermsIntact(string before, string after, IReadOnlyList<string> terms)`.

- [ ] **Step 1:** Tests: `CleanShortSentence_SkipsCleanup` ("Please review the pull request after standup." false); `Filler_Triggers` ("So I think uh we should go"); `NumberWordsAndEmailWords_Trigger` ("thirty days", "emily dot carter at example dot com"); `StripNoise_RemovesHmm` ("Hmm. So I think" to "So I think"); `ChangedProtectedTerm_Rejected` (before "Myapur", after "Maupuri", terms ["Myapur"] false).
- [ ] **Step 2-4:** Run FAIL, implement, run PASS. Commit `feat(core): cleanup gate`.

### Task 5: Scoring

**Files:** Create `VoiceWeave.Core/Scoring.cs`.

**Interfaces:** Produces `static (int errors, int refWords) Wer(string reference, string hypothesis)`, `Cer(...)`, `TermAccuracy(IReadOnlyList<string> terms, string hypothesis)` (case-sensitive). Normalisation matches `score-dictation.py`: lowercase, strip punctuation, hyphens as spaces.

- [ ] **Step 1:** Tests: identical gives (0, n); one substitution in "send the report" gives (1, 3); case and punctuation ignored; "quarkus" fails term "Quarkus".
- [ ] **Step 2-4:** FAIL, implement, PASS. Commit `feat(core): scoring`.

### Task 6: Hotkey state machine

**Files:** Create `VoiceWeave.Core/HotkeyMachine.cs`.

**Interfaces:** Produces `enum KeyEvent { HoldDown, HoldUp, EscDown, OtherKeyDown }`, `enum HotkeyAction { None, StartRecording, StopAndTranscribe, Cancel, Latch }`, `sealed class HotkeyMachine(long minHoldMs, long doubleTapMs) { HotkeyAction On(KeyEvent ev, long nowMs); }`.

- [ ] **Step 1:** Tests: `HoldThenRelease_Transcribes`; `TapUnderMinHold_CancelsSilently`; `DoubleTap_LatchesHandsFree_NextTapStops`; `Esc_CancelsWhileRecording`; `OtherKeyDuringHold_Cancels` (so Ctrl+C still works).
- [ ] **Step 2-4:** FAIL, implement (Idle/Holding/Latched), PASS. Commit `feat(core): hotkey machine`.

### Task 7: Pre-roll buffer

**Files:** Create `VoiceWeave.Core/PreRoll.cs`.

**Interfaces:** Produces `sealed class PreRoll(int sampleRate, int millis) { void Push(ReadOnlySpan<float> s); float[] Take(); }` (oldest to newest, then clears).

- [ ] **Step 1:** Tests: `Keeps_Only_Last500ms_At16k` (8000 samples); `Take_ReturnsInOrder_AndClears`; `BelowCapacity_ReturnsAll`.
- [ ] **Step 2-4:** FAIL, implement ring, PASS. Commit `feat(core): pre-roll`.

### Task 8: Microphone capture

**Files:** Create `VoiceWeave.Win/WasapiMic.cs`.

**Interfaces:** Produces `interface IAudioSource { void Start(); float[] Drain(); void Stop(); }`, `WasapiMic : IAudioSource` (shared-mode capture, converts to 16 kHz mono f32; use NAudio WASAPI capture), `FakeAudio : IAudioSource` for tests. Consumes `PreRoll`.

- [ ] **Step 1:** Test `FakeAudio_PreRollSamplesComeFirst`.
- [ ] **Step 2:** Implement `WasapiMic`.
- [ ] **Step 3:** Manual: `bench mic-test` records 3 s from the Realtek mic and prints peak above 0.01. Expected: peak printed, no error. Commit `feat(win): wasapi capture`.

### Task 9: Safe text insertion and focus checks

**Files:** Create `VoiceWeave.Win/Insertion.cs`, `FocusInfo.cs`.

**Interfaces:** Produces `interface IClipboard { ClipSnapshot Snapshot(); void SetText(string t); string? ReadText(); void Restore(ClipSnapshot s); }`, `interface IPaster { void CtrlV(bool terminal); }`, `static InsertOutcome Insert(IClipboard cb, IPaster p, FocusInfo focus, string text)`, `enum InsertOutcome { Pasted, SkippedPasswordField, SkippedElevatedTarget }`, `record FocusInfo(bool IsPassword, bool IsElevatedTarget, bool IsTerminal, string TextBeforeCursor)` obtained from UI Automation.

- [ ] **Step 1:** Tests with fakes: `ClipboardRestoredAfterPaste`; `SetTextVerifiedBeforePaste` (read-back differs gives retry then error, never pastes stale text); `NonTextFormatsPreserved` (image and file list restored byte-identical); `TerminalUsesCtrlShiftV`; `PasswordField_SkipsWithoutTouchingClipboard`.
- [ ] **Step 2-4:** FAIL, implement generic logic plus Win32 clipboard/`SendInput` (mark the copy to be excluded from clipboard history) and UIA focus detection, PASS.
- [ ] **Step 5:** Manual: Notepad, Chrome text box, Windows Terminal, and an elevated Notepad (must return SkippedElevatedTarget). Commit `feat(win): safe insertion`.

### Task 10: Global hold-key hook

**Files:** Create `VoiceWeave.Win/KeyboardHook.cs`.

**Interfaces:** Produces `static KeyEvent? MapVk(uint vk, bool down)` and `KeyboardHook : IDisposable` (`WH_KEYBOARD_LL` on a dedicated thread, raises `event Action<KeyEvent,long>`; always passes keys through).

- [ ] **Step 1:** Test `MapVk_RightCtrl_Esc_Other`.
- [ ] **Step 2:** Implement hook thread and message loop. **Step 3:** Manual: print events while typing elsewhere; typing unaffected. Commit `feat(win): keyboard hook`.

### Task 11: Model manager and Parakeet

**Files:** Create `VoiceWeave.Engine/ModelManager.cs`, `Parakeet.cs`.

**Interfaces:** Produces `record ModelSpec(string Name, string Url, string Sha256, long Size)`, `static Task<string> EnsureModelAsync(ModelSpec s, string dir)` (resume, verify, atomic rename), `interface IAsr { string Transcribe(float[] pcm16k); }`, `Parakeet : IAsr` (encoder/decoder/joiner int8, 4 threads, audio over 30 s chunked at silence).

- [ ] **Step 1:** Tests: `RejectsWrongHash`; `SkipsWhenValid`; `Chunker_SplitsLongAudio_WithoutLosingSamples` (65 s in, chunks concatenate to input).
- [ ] **Step 2-4:** FAIL, implement (Parakeet v2 files from `csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8`, SHA-256 recorded at first verified download), PASS.
- [ ] **Step 5:** `bench asr <wav>` on `recordings/03.wav` prints "Please review the pull request after standup." with decode under 1 s. Commit `feat(engine): parakeet`.

### Task 12: Cleanup engine

**Files:** Create `VoiceWeave.Engine/LlamaCleaner.cs`.

**Interfaces:** Consumes `CleanupGate`. Produces `interface ICleaner { string Clean(string text, Tone tone, IReadOnlyList<string> protectedTerms); }`, `LlamaCleaner : ICleaner` (Cleanup 1 Q8, 4 threads, context 1024, max 160 new tokens, temperature 0, lazy load, unload after 5 idle minutes), `static string BuildPrompt(string text, Tone tone)`.

- [ ] **Step 1:** Tests: `Prompt_MatchesModelCard` (exact `tone: casual\ntext: ...\nout:\n`); `ProtectedTermChanged_FallsBackToInput`; `OutputOverTwiceInputLength_Rejected` (instruction-following guard).
- [ ] **Step 2-4:** FAIL, implement, PASS.
- [ ] **Step 5:** Integration: "send the report to john no wait to maria by friday" yields "Send the report to Maria by Friday". Commit `feat(engine): cleanup`.

### Task 13: Pipeline, overlay, tray, startup behaviour

**Files:** Create `VoiceWeave.App/Pipeline.cs`, `Overlay.xaml`, `Tray.cs`, `SettingsWindow.xaml`, `VoiceWeave.Win/Startup.cs`.

**Interfaces:** Consumes everything above. Produces `Task<Outcome> Pipeline.RunUtteranceAsync(float[] audio)`; `static class Startup { bool IsEnabled(); void Set(bool enabled); }` (per-user startup entry visible in Windows Startup apps; default off); single-instance mutex that activates the running instance.

- [ ] **Step 1:** Tests with fakes: `Silence_NoTextNoPaste`; `Under200ms_Ignored`; `DictionaryGateCleanupInsert_InOrder`; `QueuedWhileModelLoading_InsertedOnce`; `HistoryRowWritten_TextOnly`; `Startup_DefaultsOff`; `SecondLaunch_ActivatesFirst`.
- [ ] **Step 2-4:** FAIL, implement (transparent click-through always-on-top no-focus bar near the bottom of the active screen; tray with Settings/Quit; on every launch the tray, hook and bar come up ready), PASS.
- [ ] **Step 5:** Manual: launch from the Start menu with Windows startup disabled; hold Right Ctrl in Notepad; text appears. Commit `feat(app): pipeline, overlay, tray`.

### Task 14: Benchmark gate

**Files:** Modify `VoiceWeave.Bench/Program.cs`.

**Interfaces:** Produces `bench score --recordings <dir> --phrases <file>` printing WER, term accuracy, decode time, key-release-to-text p50; exits non-zero when a Global Constraints target is missed.

- [ ] **Step 1:** Test `Report_FlagsMissedTarget` with canned results. **Step 2-4:** FAIL, implement, PASS.
- [ ] **Step 5:** Run on the user's 18 English recordings; save the baseline numbers to `docs/superpowers/specs/`. Commit `feat(bench): gate`.

### Task 15: GitHub Actions release

**Files:** Create `.github/workflows/desktop-release.yml`, `desktop/installer/` (Inno Setup script or MSIX-less zip fallback), `desktop/licenses.txt`.

**Interfaces:** Produces a workflow triggered by tags `desktop-v*` on `windows-latest`: `dotnet test`, `dotnet publish -c Release -r win-x64 --self-contained`, build the installer, and attach `VoiceWeaveSetup.exe` to a GitHub Release.

- [ ] **Step 1:** Create the workflow and installer script. **Step 2:** Push a throwaway tag `desktop-v0.0.1-test` to a branch copy and confirm the run is green and the Release has the installer (needs the user's go-ahead before anything is pushed or made public). **Step 3:** Download and run the installer on the laptop; the app comes up. Commit `ci: desktop release workflow`.

---

## Self-Review

- **Spec coverage:** hotkey (T6, T10), pre-roll/audio (T7, T8), insertion and password/elevated safety (T9), Parakeet (T11), dictionary (T3), cleanup and protected terms (T4, T12), overlay/tray/startup/history (T13), benchmark and targets (T5, T14), sharing/release/licences (T15), llama.cpp risk (T2). Deferred to later plans: Indic, mixed-language, snippets, command mode, per-app tone, correction learning, romanisation toggle, audio ducking, hardware check and light model set, update check.
- **Type consistency:** `Dictionary.Terms` feeds `CleanupGate.ProtectedTermsIntact` and `ICleaner.Clean`; `KeyEvent` produced in T10 and consumed in T6; `IAsr`, `ICleaner`, `IClipboard`, `IPaster` used unchanged in T13.
- **Hardware check gap:** the spec's first-launch AVX2/RAM check is deferred; flagged here so it is not forgotten before sharing widely.
