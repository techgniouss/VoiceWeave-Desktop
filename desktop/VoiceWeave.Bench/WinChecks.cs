using VoiceWeave.Core;
using VoiceWeave.Win;

/// <summary>Manual checks for the Windows pieces that need a real microphone, keyboard and desktop.</summary>
static class WinChecks
{
    public static async Task<int> MicTest()
    {
        Console.WriteLine("Devices: " + string.Join(" | ", WaveInMic.DeviceNames()));
        using var mic = new WaveInMic();
        var rec = new Recorder(mic, new PreRoll(16000, 500));
        rec.Arm();
        await Task.Delay(600);               // let the pre-roll fill
        rec.BeginUtterance();
        Console.WriteLine("Recording 3 s - speak now...");
        await Task.Delay(3000);
        var audio = rec.EndUtterance();
        rec.Disarm();
        var peak = audio.Length == 0 ? 0 : audio.Max(Math.Abs);
        Console.WriteLine($"{audio.Length / 16000.0:F1}s captured (includes 0.5s pre-roll), peak {peak:F3}");
        return peak > 0.01f ? 0 : 2;
    }

    public static async Task<int> HookTest()
    {
        using var hook = new KeyboardHook();
        var machine = new HotkeyMachine(200, 400);
        hook.KeyEvent += (ev, t) => Console.WriteLine($"{ev,-14} -> {machine.On(ev, t)}");
        Console.WriteLine("Hold Right Ctrl, tap Esc, type letters (in any window) for 15 s. Typing must still work normally.");
        await Task.Delay(15000);
        return 0;
    }

    public static int FocusTest()
    {
        Console.WriteLine("Click into a text box in another app; probing in 4 s...");
        Thread.Sleep(4000);
        Console.WriteLine(FocusProbe.Get());
        return 0;
    }

    public static async Task<int> PasteTest()
    {
        Console.WriteLine("Focus Notepad (or any editor) now; pasting 'VoiceWeave paste test' in 5 s. Your clipboard will be restored.");
        await Task.Delay(5000);
        using var cb = new Win32Clipboard();
        var outcome = await TextInserter.InsertAsync(cb, new SendInputPaster(), FocusProbe.Get(), "VoiceWeave paste test", TimeSpan.FromMilliseconds(150));
        Console.WriteLine(outcome);
        return 0;
    }
}
