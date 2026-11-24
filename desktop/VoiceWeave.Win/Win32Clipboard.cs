using System.Runtime.InteropServices;
using System.Text;
using VoiceWeave.Core;

namespace VoiceWeave.Win;

/// <summary>
/// Clipboard access that snapshots and restores every in-memory format (text, images, file lists, rich text),
/// and marks text it writes so clipboard history and cloud sync skip it.
/// </summary>
public sealed class Win32Clipboard : IClipboard, IDisposable
{
    private readonly bool _ownsWindow;

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    // Formats whose clipboard data is a GDI/metafile handle rather than global memory; Windows re-synthesises
    // the common ones (a bitmap from DIB) so skipping them loses nothing.
    private static readonly HashSet<uint> HandleFormats = new() { 2, 3, 9, 14, 0x82, 0x83, 0x8E };

    // A message-only window owns the clipboard while we write to it; plain Win32, so it works on any thread.
    private readonly IntPtr _owner;

    /// <param name="ownerWindow">An existing window to own the clipboard while writing (the app passes its main window);
    /// when omitted, a message-only window is created and destroyed with this object.</param>
    public Win32Clipboard(IntPtr ownerWindow = default)
    {
        _ownsWindow = ownerWindow == IntPtr.Zero;
        _owner = _ownsWindow
            ? CreateWindowEx(0, "STATIC", "VoiceWeaveClipboard", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero)
            : ownerWindow;
    }
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
            if (OpenClipboard(_owner))
            {
                try { action(); return; }
                finally { CloseClipboard(); }
            }
            Thread.Sleep(15); // another app (or clipboard history) holds it briefly
        }
        throw new InvalidOperationException("Could not open the clipboard.");
    }

    public void Dispose() { if (_ownsWindow && _owner != IntPtr.Zero) DestroyWindow(_owner); }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
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
