using System.Drawing;
using System.Drawing.Drawing2D;
using VoiceWeave.Engine;
using Forms = System.Windows.Forms;

namespace VoiceWeave.App;

/// <summary>The notification-area icon and its menu.</summary>
sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _barItem, _startupItem, _statusItem, _romanItem, _learnItem, _undoItem;
    private readonly Dictionary<SpeechMode, Forms.ToolStripMenuItem> _languageItems = new();
    private readonly Forms.ContextMenuStrip _menu = new();

    public event Action? OpenWindow, ToggleBar, ToggleStartup, ToggleRoman, ToggleLearning, UndoLearned, OpenDictionary, OpenModels, Quit;
    public event Action<SpeechMode>? LanguageSelected;

    public TrayIcon(bool barVisible, bool startWithWindows, SpeechMode language, bool romanOutput, bool learnCorrections)
    {
        _statusItem = new Forms.ToolStripMenuItem("Starting…") { Enabled = false };
        var openItem = new Forms.ToolStripMenuItem("Open VoiceWeave") { Font = new Font(Forms.Control.DefaultFont, FontStyle.Bold) };
        openItem.Click += (_, _) => OpenWindow?.Invoke();
        var languageMenu = new Forms.ToolStripMenuItem("Language");
        foreach (var l in SpeechModes.All)
        {
            if (SpeechModes.IsAuto(l) && l == SpeechMode.EnglishTelugu) languageMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            var item = new Forms.ToolStripMenuItem(SpeechModes.DisplayName(l)) { Checked = l == language };
            var chosen = l;
            item.Click += (_, _) => LanguageSelected?.Invoke(chosen);
            _languageItems[l] = item;
            languageMenu.DropDownItems.Add(item);
        }
        _romanItem = new Forms.ToolStripMenuItem("Write Indian languages in English letters") { Checked = romanOutput };
        _romanItem.Click += (_, _) => ToggleRoman?.Invoke();
        _learnItem = new Forms.ToolStripMenuItem("Learn from my corrections") { Checked = learnCorrections };
        _learnItem.Click += (_, _) => ToggleLearning?.Invoke();
        _undoItem = new Forms.ToolStripMenuItem("Undo last learned correction") { Enabled = false };
        _undoItem.Click += (_, _) => UndoLearned?.Invoke();
        _barItem = new Forms.ToolStripMenuItem("Show floating bar") { Checked = barVisible, CheckOnClick = false };
        _startupItem = new Forms.ToolStripMenuItem("Start with Windows") { Checked = startWithWindows };
        var dictionary = new Forms.ToolStripMenuItem("Edit dictionary…");
        var models = new Forms.ToolStripMenuItem("Open models folder");
        var quit = new Forms.ToolStripMenuItem("Quit VoiceWeave");
        _barItem.Click += (_, _) => ToggleBar?.Invoke();
        _startupItem.Click += (_, _) => ToggleStartup?.Invoke();
        dictionary.Click += (_, _) => OpenDictionary?.Invoke();
        models.Click += (_, _) => OpenModels?.Invoke();
        quit.Click += (_, _) => Quit?.Invoke();

        var menu = _menu;
        menu.Items.AddRange(new Forms.ToolStripItem[]
        {
            openItem, _statusItem, new Forms.ToolStripSeparator(), languageMenu, _romanItem, _learnItem, _undoItem, new Forms.ToolStripSeparator(),
            _barItem, _startupItem, dictionary, models, new Forms.ToolStripSeparator(), quit,
        });
        _icon = new Forms.NotifyIcon { Icon = MakeIcon(), Text = "VoiceWeave", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => OpenWindow?.Invoke();
        _icon.BalloonTipClicked += (_, _) => OpenWindow?.Invoke();
    }

    /// <summary>Shows the tray menu at the mouse pointer (used by right-clicking the floating bar).</summary>
    public void ShowMenu() => _menu.Show(Forms.Cursor.Position);

    public void SetStatus(string text)
    {
        _statusItem.Text = text;
        _icon.Text = ("VoiceWeave - " + text)[..Math.Min(63, ("VoiceWeave - " + text).Length)]; // tooltip limit is 63 chars
    }

    public void ShowBalloon(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _icon.ShowBalloonTip(5000);
    }

    public void SetLanguageChecked(SpeechMode language)
    {
        foreach (var (l, item) in _languageItems) item.Checked = l == language;
    }

    public void SetRomanChecked(bool on) => _romanItem.Checked = on;
    public void SetLearnChecked(bool on) => _learnItem.Checked = on;
    public void SetUndoEnabled(bool on) => _undoItem.Enabled = on;
    public void SetBarChecked(bool visible) => _barItem.Checked = visible;
    public void SetStartupChecked(bool enabled) => _startupItem.Checked = enabled;

    /// <summary>The app icon, at the size the notification area wants.</summary>
    private static Icon MakeIcon()
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("VoiceWeave.app.ico")!;
        return new Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
