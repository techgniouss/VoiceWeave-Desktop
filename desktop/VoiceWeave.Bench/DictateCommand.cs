using System.Diagnostics;
using System.Threading.Channels;
using VoiceWeave.Core;
using VoiceWeave.Engine;
using VoiceWeave.Win;
using CoreDictionary = VoiceWeave.Core.Dictionary;

/// <summary>
/// Console version of the real app: hold Right Ctrl to dictate into whatever window has focus.
/// Usage: dictate --models &lt;dir&gt; [--dict &lt;file&gt;] [--seconds N]
/// </summary>
static class DictateCommand
{
    public static async Task<int> Run(string[] args)
    {
        string? Arg(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        var models = Arg("--models");
        if (models == null) { Console.WriteLine("usage: dictate --models <dir> [--dict <file>] [--seconds N]"); return 1; }
        var seconds = int.TryParse(Arg("--seconds"), out var s) ? s : 300;
        var dictText = Arg("--dict") is { } df && File.Exists(df) ? File.ReadAllText(df) : "";

        Console.WriteLine("Loading speech model (about 15 s)...");
        using var asr = new Parakeet(models);
        using var cleaner = new LlamaCleaner(models);
        var pipeline = new DictationPipeline(asr, new CleanupService(cleaner), CoreDictionary.Parse(dictText));

        using var mic = new WaveInMic();
        var recorder = new Recorder(mic, new PreRoll(16000, 500));
        recorder.Arm();
        using var clipboard = new Win32Clipboard();
        var paster = new SendInputPaster();
        using var hook = new KeyboardHook();

        // The hook thread only enqueues; all work happens here, in order.
        var events = Channel.CreateUnbounded<(KeyEvent ev, long t)>();
        hook.KeyEvent += (ev, t) => events.Writer.TryWrite((ev, t));
        var machine = new HotkeyMachine(minHoldMs: 200, doubleTapMs: 400);

        Console.WriteLine($"Ready. Focus any text box, HOLD Right Ctrl, speak, release. (Esc cancels; stops after {seconds} s.)");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        try
        {
            await foreach (var (ev, t) in events.Reader.ReadAllAsync(cts.Token))
            {
                switch (machine.On(ev, t))
                {
                    case HotkeyAction.StartRecording:
                        recorder.BeginUtterance();
                        Console.WriteLine("  ● recording...");
                        break;
                    case HotkeyAction.Cancel:
                        recorder.Cancel();
                        Console.WriteLine("  ✕ cancelled");
                        break;
                    case HotkeyAction.StopAndTranscribe:
                        var released = Stopwatch.StartNew();
                        var audio = recorder.EndUtterance();
                        var focus = FocusProbe.Get();
                        var result = await Task.Run(() => pipeline.Run(audio, Tone.Casual));
                        if (result.Final.Length == 0) { Console.WriteLine($"  (nothing heard, {audio.Length / 16000.0:F1}s of audio)"); break; }
                        var outcome = await TextInserter.InsertAsync(clipboard, paster, focus, result.Final, TimeSpan.FromMilliseconds(150));
                        Console.WriteLine($"  raw      : {result.Raw}");
                        if (result.Corrected != result.Raw) Console.WriteLine($"  dictionary: {result.Corrected}");
                        Console.WriteLine($"  final    : {result.Final}");
                        Console.WriteLine($"  {outcome}  | speech {result.AsrTime.TotalSeconds:F2}s  cleanup {result.CleanupTime.TotalSeconds:F2}s  release-to-pasted {released.Elapsed.TotalSeconds:F2}s");
                        break;
                    case HotkeyAction.Latch:
                        Console.WriteLine("  ● hands-free: tap Right Ctrl again to finish");
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        recorder.Disarm();
        return 0;
    }
}
