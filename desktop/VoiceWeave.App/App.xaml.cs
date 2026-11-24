using System.IO;
using System.Threading.Channels;
using System.Windows;
using VoiceWeave.Core;
using VoiceWeave.Engine;
using VoiceWeave.Win;
using CoreDictionary = VoiceWeave.Core.Dictionary;

namespace VoiceWeave.App;

public partial class App : Application
{
    private const string DefaultDictionary =
        "# Words the speech recogniser often gets wrong.  Format:  what it hears => what you want\n" +
        "# Exact phrases are replaced; close misspellings of the right-hand side are fixed automatically.\n" +
        "# Example:\n" +
        "#   quark us => Quarkus\n" +
        "#   argo cd => Argo CD\n";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private Settings _settings = new();
    private OverlayWindow _overlay = null!;
    private MainWindow? _main;
    private TrayIcon _tray = null!;
    private WaveInMic? _mic;
    private Win32Clipboard? _clipboard;
    private KeyboardHook? _hook;
    private DictationController _controller = null!;
    private DictationPipeline? _pipeline;
    private EngineManager _engines = null!;
    private SpeechRouter _router = null!;
    private LanguageIdManager _lid = null!;
    private LlamaCleaner _cleaner = null!;
    private CorrectionLearner _learner = null!;
    private System.Net.Http.HttpClient? _http;
    private FileSystemWatcher? _watcher;
    private readonly StartupRegistration _startup = new();
    private readonly CancellationTokenSource _exit = new();

    // ---- what the window sees ------------------------------------------------------------------------------------

    public Settings Settings => _settings;
    public HistoryStore? History { get; private set; }
    public string StatusText { get; private set; } = "Starting…";
    public SpeechMode Mode => _router.Mode;
    public bool StartupEnabled => _startup.IsEnabled();

    /// <summary>The status line changed (loading, downloading, ready…).</summary>
    public event Action? StatusChanged;
    /// <summary>A setting changed from the tray or the window, so every view should refresh.</summary>
    public event Action? SettingsChanged;
    /// <summary>A dictation finished and was added to the history (if history is on).</summary>
    public event Action<HistoryEntry>? Dictated;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(true, @"Local\VoiceWeave.SingleInstance", out var first);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\VoiceWeave.Show");
        if (!first)
        {
            _showEvent.Set(); // ask the running copy to show itself
            Shutdown();
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) => DiagLog.Write("CRASH: " + args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) => DiagLog.Write("background error: " + args.Exception.GetBaseException());
        DispatcherUnhandledException += (_, args) =>
        {
            DiagLog.Write("UI error: " + args.Exception);
            _overlay?.ShowToast("Something went wrong: " + args.Exception.Message, 5000);
            args.Handled = true;
        };

        _settings = Settings.Load();
        AppPaths.ModelsFolderSetting = _settings.ModelsFolder;
        Directory.CreateDirectory(AppPaths.Root);
        DiagLog.Write($"started v{typeof(App).Assembly.GetName().Version}; language={_settings.Language} roman={_settings.RomanOutput}; models={AppPaths.Models}");
        if (!File.Exists(AppPaths.Dictionary)) File.WriteAllText(AppPaths.Dictionary, DefaultDictionary);
        ApplyHistorySettings();

        _overlay = new OverlayWindow(_settings);
        _overlay.Show();
        if (!_settings.BarVisible) _overlay.Hide();

        _tray = new TrayIcon(_settings.BarVisible, _startup.IsEnabled(), SpeechModes.Parse(_settings.Language), _settings.RomanOutput, _settings.LearnCorrections);
        WireTray();

        InitEngines();
        StartAudioAndController();
        WireOverlay();
        StartHotkey();
        WatchDictionary();
        WatchForSecondLaunch();

        // Started by hand: show the window. Started at sign-in (--background) it stays in the tray, except the very first time.
        var background = e.Args.Contains("--background");
        if (!background || !_settings.WelcomeDismissed) ShowMainWindow();
    }

    // ---- the window ----------------------------------------------------------------------------------------------

    public void ShowMainWindow()
    {
        _main ??= new MainWindow(this);
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    /// <summary>The window was closed with the X: the app keeps running in the tray. Say so once.</summary>
    internal void MainWindowHidden()
    {
        if (_settings.TrayHintShown) return;
        _settings.TrayHintShown = true;
        _settings.Save();
        _tray.ShowBalloon("VoiceWeave is still running", "Hold Ctrl+Win to dictate. Open the window again from the tray icon.");
    }

    public void QuitApp() => Dispatcher.BeginInvoke(() => Shutdown());

    // ---- settings, shared by the tray and the window ---------------------------------------------------------------

    public void ChangeMode(SpeechMode mode)
    {
        if (mode == _router.Mode) return;
        DiagLog.Write("language selected: " + mode);
        _settings.Language = SpeechModes.Code(mode);
        _settings.Save();
        _router.Mode = mode;
        _tray.SetLanguageChecked(mode);
        SettingsChanged?.Invoke();
        _ = PrepareModeAsync(mode);
    }

    public void SetRoman(bool on)
    {
        _settings.RomanOutput = on;
        _settings.Save();
        _tray.SetRomanChecked(on);
        SettingsChanged?.Invoke();
    }

    public void SetLearn(bool on)
    {
        _settings.LearnCorrections = on;
        _settings.Save();
        _learner.Enabled = on;
        if (!on) _learner.Cancel();
        _tray.SetLearnChecked(on);
        SettingsChanged?.Invoke();
    }

    public void SetBarVisible(bool on)
    {
        _settings.BarVisible = on;
        if (on) _overlay.Show(); else _overlay.Hide();
        _tray.SetBarChecked(on);
        _settings.Save();
        SettingsChanged?.Invoke();
    }

    public void SetStartup(bool on)
    {
        _startup.Set(on, Environment.ProcessPath!);
        _tray.SetStartupChecked(_startup.IsEnabled());
        SettingsChanged?.Invoke();
    }

    public void SetKeepHistory(bool on)
    {
        _settings.KeepHistory = on;
        _settings.Save();
        SettingsChanged?.Invoke();
    }

    public void SetHistoryDays(int days)
    {
        _settings.HistoryDays = days;
        _settings.Save();
        ApplyHistorySettings();
        SettingsChanged?.Invoke();
    }

    public void DismissWelcome()
    {
        _settings.WelcomeDismissed = true;
        _settings.Save();
        SettingsChanged?.Invoke();
    }

    /// <summary>(Re)opens the history with the retention the user chose; older entries fall away.</summary>
    private void ApplyHistorySettings()
    {
        var retention = _settings.HistoryDays > 0 ? TimeSpan.FromDays(_settings.HistoryDays) : (TimeSpan?)null;
        History = new HistoryStore(AppPaths.History, 1000, retention);
    }

    // ---- models and pipeline -------------------------------------------------------------------------------------

    private void InitEngines()
    {
        _http = new System.Net.Http.HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var models = new ModelManager(_http);
        _engines = new EngineManager(models, AppPaths.Models, LanguagePacks.For);
        _lid = new LanguageIdManager(models, AppPaths.Models, WhisperLanguageId.Files, dir => new WhisperLanguageId(dir));
        var mode = SpeechModes.Parse(_settings.Language);
        _router = new SpeechRouter(_engines.Get, () => _lid.Current, mode)
        {
            Trace = line => DiagLog.Write("segment: " + line),
            KnownTerms = () => _pipeline!.Dictionary.Terms.ToHashSet(StringComparer.OrdinalIgnoreCase),
        };
        _cleaner = new LlamaCleaner(AppPaths.Models);
        _pipeline = new DictationPipeline(_router, new CleanupService(_cleaner), LoadDictionary())
        {
            RomanOutput = () => _settings.RomanOutput,
        };
        _ = PrepareModeAsync(mode);
    }

    /// <summary>Loads everything a speech mode needs: each language's recogniser, plus language detection for the auto modes.</summary>
    private async Task EnsureModeAsync(SpeechMode mode, IProgress<double>? progress = null)
    {
        foreach (var language in SpeechModes.Needs(mode)) await _engines.EnsureReadyAsync(language, progress, _exit.Token);
        if (SpeechModes.IsAuto(mode)) await _lid.EnsureReadyAsync(progress, _exit.Token);
    }

    /// <summary>Downloads (first time only) and loads a mode's models, telling the user how it is going.</summary>
    private async Task PrepareModeAsync(SpeechMode mode)
    {
        var name = SpeechModes.DisplayName(mode);
        try
        {
            var progress = new Progress<double>(p => { if (p is > 0 and < 1) Status($"{name}: downloading {p:P0}", toast: true); });
            var loaded = SpeechModes.Needs(mode).All(_engines.IsReady) && (!SpeechModes.IsAuto(mode) || _lid.Current != null);
            if (!loaded) Status($"Loading {name}…", toast: true);
            await EnsureModeAsync(mode, progress);
            if (SpeechModes.Needs(mode).Contains(Language.English)) _ = Task.Run(WarmCleanup);
            Status(mode == SpeechMode.English ? "Ready: hold Ctrl+Win to dictate" : $"{name} ready: hold Ctrl+Win to dictate", toast: true);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Status($"Could not load {name}: {e.Message}", toast: true);
        }
    }

    /// <summary>Loads the cleanup model now (a few seconds) so the first sentence that needs it is not delayed.</summary>
    private void WarmCleanup()
    {
        try { _cleaner.Clean("um hello", Tone.Casual); DiagLog.Write("cleanup model warmed up"); }
        catch (Exception e) { DiagLog.Write("cleanup warm-up failed: " + e.Message); }
    }

    private static CoreDictionary LoadDictionary()
    {
        try { return CoreDictionary.Parse(File.ReadAllText(AppPaths.Dictionary)); }
        catch (IOException) { return CoreDictionary.Parse(""); }
    }

    private void WatchDictionary()
    {
        _watcher = new FileSystemWatcher(AppPaths.Root, Path.GetFileName(AppPaths.Dictionary)) { EnableRaisingEvents = true };
        _watcher.Changed += async (_, _) =>
        {
            await Task.Delay(300); // editors write in several steps
            if (_pipeline != null) _pipeline.Dictionary = LoadDictionary();
        };
    }

    // ---- audio, controller ---------------------------------------------------------------------------------------

    private void StartAudioAndController()
    {
        _mic = new WaveInMic();
        var recorder = new Recorder(_mic, new PreRoll(16000, 500));
        try { recorder.Arm(); }
        catch (Exception e)
        {
            Status("No microphone available: " + e.Message, toast: true);
        }
        _clipboard = new Win32Clipboard(_overlay.Handle);
        // Each dictation gets a session that recognises sentences while you are still speaking.
        _controller = new DictationController(recorder,
            () => _pipeline!.StartSession(Tone.Casual, () => EnsureModeAsync(SpeechModes.Parse(_settings.Language))),
            _clipboard, new SendInputPaster(), FocusProbe.Get, TimeSpan.FromMilliseconds(150));

        // Self-learning: after a paste, watch that stretch of the text box and turn the user's fixes into dictionary rules.
        _learner = new CorrectionLearner(UiaFieldText.CaptureFocused, new DictionaryFileStore(), LearnerTiming.Default)
        {
            Enabled = _settings.LearnCorrections,
        };
        _learner.Learned += message =>
        {
            DiagLog.Write("learned a correction");
            Dispatcher.BeginInvoke(() =>
            {
                _overlay.ShowToast(message, 3500);
                _tray.SetUndoEnabled(_learner.CanUndo);
            });
        };
        _controller.Pasted += (result, _) => _learner.Watch(result.Final);
        _controller.StateChanged += state => { if (state == DictationState.Recording) _learner.Cancel(); };

        // History: every dictation that produced text, except text meant for a password box.
        _controller.Finished += (result, outcome, total) =>
        {
            if (outcome == InsertOutcome.SkippedPasswordField) return;
            var entry = new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, result.Final, SpeechModes.Code(_router.Mode), total.TotalSeconds);
            if (_settings.KeepHistory) History?.Add(entry);
            Dispatcher.BeginInvoke(() => Dictated?.Invoke(entry));
        };

        _mic.Samples += _controller.PushLevel;
        _controller.Level += rms => Dispatcher.BeginInvoke(() => _overlay.PushLevel(rms));
        _controller.RegionalForced += () =>
        {
            var partner = SpeechModes.Partner(_router.Mode);
            if (partner == null) return;
            DiagLog.Write("regional language forced");
            Dispatcher.BeginInvoke(() => _overlay.ShowToast($"{Languages.DisplayName(partner.Value)} (Shift held)", 2000));
        };
        _controller.StateChanged += state => DiagLog.Write($"state: {state}");
        _controller.StateChanged += state => Dispatcher.BeginInvoke(() => _overlay.SetState(state, _controller.IsLatched));
        _controller.Message += text => DiagLog.Write("message: " + text);
        _controller.Message += text => Dispatcher.BeginInvoke(() => _overlay.ShowToast(text, 3500));
        _controller.Pasted += (r, total) => DiagLog.Write(
            $"pasted ({SpeechModes.Code(_router.Mode)}): {r.Final.Length} chars, total {total.TotalSeconds:F2}s (speech {r.AsrTime.TotalSeconds:F2}s, cleanup {r.CleanupTime.TotalSeconds:F2}s)");
        _controller.Pasted += (r, total) => Dispatcher.BeginInvoke(() =>
            _tray.SetStatus($"Last: {total.TotalSeconds:F1}s (speech {r.AsrTime.TotalSeconds:F1}s, cleanup {r.CleanupTime.TotalSeconds:F1}s)"));
    }

    // ---- floating bar and tray -----------------------------------------------------------------------------------

    private void WireOverlay()
    {
        _overlay.MenuRequested += () => _tray.ShowMenu();
        _overlay.HoldStarted += () => _controller.Begin(latched: false);
        _overlay.HoldEnded += () => _controller.Finish();
        _overlay.Clicked += () =>
        {
            if (_controller.State == DictationState.Recording) _controller.Finish();
            else _controller.Begin(latched: true);
        };
    }

    private void WireTray()
    {
        _tray.OpenWindow += () => Dispatcher.BeginInvoke(ShowMainWindow);
        _tray.ToggleBar += () => SetBarVisible(!_settings.BarVisible);
        _tray.ToggleStartup += () => SetStartup(!_startup.IsEnabled());
        _tray.LanguageSelected += ChangeMode;
        _tray.ToggleRoman += () => SetRoman(!_settings.RomanOutput);
        _tray.ToggleLearning += () => SetLearn(!_settings.LearnCorrections);
        _tray.UndoLearned += () =>
        {
            var undone = _learner.UndoLast();
            _tray.SetUndoEnabled(_learner.CanUndo);
            _overlay.ShowToast(undone ? "Removed the last learned corrections" : "Nothing to undo", 2500);
        };
        _tray.OpenDictionary += () => Dispatcher.BeginInvoke(() => { ShowMainWindow(); _main?.ShowPage("dictionary"); });
        _tray.OpenModels += () => { Directory.CreateDirectory(AppPaths.Models); Shell(AppPaths.Models); };
        _tray.Quit += QuitApp;
    }

    internal static void Shell(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private void Status(string text, bool toast)
    {
        DiagLog.Write("status: " + text);
        Dispatcher.BeginInvoke(() =>
        {
            StatusText = text;
            _tray.SetStatus(text);
            StatusChanged?.Invoke();
            if (toast) _overlay.ShowToast(text, 3500);
        });
    }

    // ---- Ctrl+Win hotkey -----------------------------------------------------------------------------------------

    private void StartHotkey()
    {
        try { _hook = new KeyboardHook(); }
        catch (InvalidOperationException e)
        {
            Status("Hotkey unavailable: " + e.Message, toast: true);
            return;
        }
        // The hook thread only enqueues; the state machine and controller run on this worker, in order.
        var events = Channel.CreateUnbounded<(Core.KeyEvent ev, long t)>();
        _hook.KeyEvent += (ev, t) => events.Writer.TryWrite((ev, t));
        var machine = new HotkeyMachine(minHoldMs: 220, doubleTapMs: 450);
        _ = Task.Run(async () =>
        {
            await foreach (var (ev, t) in events.Reader.ReadAllAsync(_exit.Token))
            {
                var action = machine.On(ev, t);
                if (action != HotkeyAction.None) DiagLog.Write($"hotkey: {ev} -> {action}");
                switch (action)
                {
                    case HotkeyAction.StartRecording: _controller.Begin(latched: false); break;
                    case HotkeyAction.StopAndTranscribe: _controller.Finish(); break;
                    case HotkeyAction.Cancel: _controller.Cancel(); break;
                    case HotkeyAction.ForceRegional: _controller.ForceRegional(); break;
                    case HotkeyAction.Latch:
                        _controller.Latch();
                        await Dispatcher.BeginInvoke(() => _overlay.SetState(DictationState.Recording, latched: true));
                        break;
                }
            }
        });
    }

    // ---- second launch -------------------------------------------------------------------------------------------

    private void WatchForSecondLaunch()
    {
        _ = Task.Run(() =>
        {
            while (!_exit.IsCancellationRequested)
            {
                if (!_showEvent!.WaitOne(500)) continue;
                Dispatcher.BeginInvoke(() =>
                {
                    SetBarVisible(true);
                    ShowMainWindow();
                });
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exit.Cancel();
        _hook?.Dispose();
        _mic?.Dispose();
        _watcher?.Dispose();
        _engines?.Dispose();
        _lid?.Dispose();
        _learner?.Dispose();
        _cleaner?.Dispose();
        _http?.Dispose();
        _tray?.Dispose();
        _clipboard?.Dispose();
        try { _controller?.Dispose(); } catch (AggregateException) { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
