using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using VoiceWeave.Core;

namespace VoiceWeave.Win;

/// <summary>
/// Clipboard access that snapshots and restores every in-memory format (text, images, file lists, rich text),
/// and marks text it writes so clipboard history and cloud sync skip it.
/// </summary>
public sealed class Win32Clipboard : IClipboard, IDisposable
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    // Formats whose clipboard data is a GDI/metafile handle rather than global memory; Windows re-synthesises
    // the common ones (a bitmap from DIB) so skipping them loses nothing.
    private static readonly HashSet<uint> HandleFormats = new() { 2, 3, 9, 14, 0x82, 0x83, 0x8E };

    private readonly HwndSource _owner =
        new(new HwndSourceParameters("VoiceWeaveClipboard") { ParentWindow = new IntPtr(-3) }); // message-only window
    private readonly uint _fmtExclude = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    private readonly uint _fmtHistory = RegisterClipboardFormat("CanIncludeInClipboardHistory");
    private readonly uint _fmtCloud = RegisterClipboardFormat("CanUploadToCloudClipboard");

    public ClipSnapshot Snapshot()
    {
        var formats = new Dictionary<uint, byte[]>();
        WithClipboard(() =>
        {
            uint fmt = 0;
            while ((fmt = EnumClipboardFormats(fmt)) != 0)
            {
                if (HandleFormats.Contains(fmt) || (fmt >= 0x200 && fmt <= 0x3FF)) continue;
                var h = GetClipboardData(fmt);
                if (h == IntPtr.Zero) continue;
                var size = (int)GlobalSize(h);
                var p = GlobalLock(h);
                if (p == IntPtr.Zero) continue;
                try
                {
                    var bytes = new byte[size];
                    Marshal.Copy(p, bytes, 0, size);
                    formats[fmt] = bytes;
                }
                finally { GlobalUnlock(h); }
            }
        });
        return new ClipSnapshot(formats);
    }

    public void SetText(string text) => WithClipboard(() =>
    {
        EmptyClipboard();
        PutBytes(CF_UNICODETEXT, Encoding.Unicode.GetBytes(text + "\0"));
        PutBytes(_fmtExclude, BitConverter.GetBytes(1));
        PutBytes(_fmtHistory, BitConverter.GetBytes(0));
        PutBytes(_fmtCloud, BitConverter.GetBytes(0));
    });

    public string? ReadText()
    {
        string? result = null;
        WithClipboard(() =>
        {
            var h = GetClipboardData(CF_UNICODETEXT);
            if (h == IntPtr.Zero) return;
            var p = GlobalLock(h);
            if (p == IntPtr.Zero) return;
            try { result = Marshal.PtrToStringUni(p); }
            finally { GlobalUnlock(h); }
        });
        return result;
    }

    public void Restore(ClipSnapshot snapshot) => WithClipboard(() =>
    {
        EmptyClipboard();
        foreach (var (fmt, bytes) in snapshot.Formats) PutBytes(fmt, bytes);
    });

    private static void PutBytes(uint format, byte[] bytes)
    {
        var h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
        if (h == IntPtr.Zero) return;
        var p = GlobalLock(h);
        Marshal.Copy(bytes, 0, p, bytes.Length);
        GlobalUnlock(h);
        if (SetClipboardData(format, h) == IntPtr.Zero) GlobalFree(h); // on success the system owns the memory
    }

    private void WithClipboard(Action action)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (OpenClipboard(_owner.Handle))
            {
                try { action(); return; }
                finally { CloseClipboard(); }
            }
            Thread.Sleep(15); // another app (or clipboard history) holds it briefly
        }
        throw new InvalidOperationException("Could not open the clipboard.");
    }

    public void Dispose() => _owner.Dispose();

    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr mem);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr mem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr mem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr mem);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr mem);
}
