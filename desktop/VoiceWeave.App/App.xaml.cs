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
    private TrayIcon _tray = null!;
    private WaveInMic? _mic;
    private Win32Clipboard? _clipboard;
    private KeyboardHook? _hook;
    private DictationController _controller = null!;
    private DictationPipeline? _pipeline;
    private FileSystemWatcher? _watcher;
    private readonly StartupRegistration _startup = new();
    private readonly CancellationTokenSource _exit = new();

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

        DispatcherUnhandledException += (_, args) =>
        {
            _overlay?.ShowToast("Something went wrong: " + args.Exception.Message, 5000);
            args.Handled = true;
        };

        _settings = Settings.Load();
        Directory.CreateDirectory(AppPaths.Root);
        if (!File.Exists(AppPaths.Dictionary)) File.WriteAllText(AppPaths.Dictionary, DefaultDictionary);

        _overlay = new OverlayWindow(_settings);
        _overlay.Show();
        if (!_settings.BarVisible) _overlay.Hide();

        _tray = new TrayIcon(_settings.BarVisible, _startup.IsEnabled());
        WireTray();

        var ready = LoadPipelineAsync();
        StartAudioAndController(ready);
        WireOverlay();
        StartHotkey();
        WatchDictionary();
        WatchForSecondLaunch();
    }

    // ---- models and pipeline -------------------------------------------------------------------------------------

    private async Task LoadPipelineAsync()
    {
        try
        {
            var dir = AppPaths.Models;
            using var http = new System.Net.Http.HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var manager = new ModelManager(http);
            var files = Parakeet.Files.Append(LlamaCleaner.File).ToArray();
            foreach (var spec in files)
            {
                var name = Path.GetFileName(spec.Name);
                var progress = new Progress<double>(p => Status($"Downloading {name}: {p:P0}", toast: spec.Size > 50_000_000));
                await manager.EnsureAsync(spec, dir, progress, _exit.Token);
            }
            Status("Loading speech model…", toast: true);
            var asr = await Task.Run(() => new Parakeet(dir), _exit.Token);
            var cleaner = new LlamaCleaner(dir);
            _pipeline = new DictationPipeline(asr, new CleanupService(cleaner), LoadDictionary());
            Status("Ready: hold Ctrl+Win to dictate", toast: true);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Status("Could not load models: " + e.Message, toast: true);
            throw;
        }
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

    private void StartAudioAndController(Task ready)
    {
        _mic = new WaveInMic();
        var recorder = new Recorder(_mic, new PreRoll(16000, 500));
        try { recorder.Arm(); }
        catch (Exception e)
        {
            Status("No microphone available: " + e.Message, toast: true);
        }
        _clipboard = new Win32Clipboard(_overlay.Handle);
        _controller = new DictationController(recorder, audio => _pipeline!.Run(audio, Tone.Casual),
            _clipboard, new SendInputPaster(), FocusProbe.Get, ready, TimeSpan.FromMilliseconds(150));

        _mic.Samples += _controller.PushLevel;
        _controller.Level += rms => Dispatcher.BeginInvoke(() => _overlay.PushLevel(rms));
        _controller.StateChanged += state => Dispatcher.BeginInvoke(() => _overlay.SetState(state, _controller.IsLatched));
        _controller.Message += text => Dispatcher.BeginInvoke(() => _overlay.ShowToast(text, 3500));
        _controller.Pasted += (r, total) => Dispatcher.BeginInvoke(() =>
            _tray.SetStatus($"Last: {total.TotalSeconds:F1}s (speech {r.AsrTime.TotalSeconds:F1}s, cleanup {r.CleanupTime.TotalSeconds:F1}s)"));
    }

    // ---- floating bar and tray -----------------------------------------------------------------------------------

    private void WireOverlay()
    {
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
        _tray.ToggleBar += () =>
        {
            _settings.BarVisible = !_settings.BarVisible;
            if (_settings.BarVisible) _overlay.Show(); else _overlay.Hide();
            _tray.SetBarChecked(_settings.BarVisible);
            _settings.Save();
        };
        _tray.ToggleStartup += () =>
        {
            _startup.Set(!_startup.IsEnabled(), Environment.ProcessPath!);
            _tray.SetStartupChecked(_startup.IsEnabled());
        };
        _tray.OpenDictionary += () => Shell(AppPaths.Dictionary);
        _tray.OpenModels += () => { Directory.CreateDirectory(AppPaths.Models); Shell(AppPaths.Models); };
        _tray.Quit += () => Dispatcher.BeginInvoke(() => Shutdown());
    }

    private static void Shell(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private void Status(string text, bool toast)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _tray.SetStatus(text);
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
                switch (machine.On(ev, t))
                {
                    case HotkeyAction.StartRecording: _controller.Begin(latched: false); break;
                    case HotkeyAction.StopAndTranscribe: _controller.Finish(); break;
                    case HotkeyAction.Cancel: _controller.Cancel(); break;
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
                    _settings.BarVisible = true;
                    _overlay.Show();
                    _tray.SetBarChecked(true);
                    _overlay.ShowToast("VoiceWeave is running: hold Ctrl+Win to dictate", 3000);
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
        _tray?.Dispose();
        _clipboard?.Dispose();
        try { _controller?.Dispose(); } catch (AggregateException) { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
