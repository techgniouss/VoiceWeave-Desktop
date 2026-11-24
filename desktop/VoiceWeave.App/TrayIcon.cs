using System.Drawing;
using System.Drawing.Drawing2D;
using VoiceWeave.Engine;
using Forms = System.Windows.Forms;

namespace VoiceWeave.App;

/// <summary>The notification-area icon and its menu.</summary>
sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _barItem, _startupItem, _statusItem, _romanItem;
    private readonly Dictionary<Language, Forms.ToolStripMenuItem> _languageItems = new();

    public event Action? ToggleBar, ToggleStartup, ToggleRoman, OpenDictionary, OpenModels, Quit;
    public event Action<Language>? LanguageSelected;

    public TrayIcon(bool barVisible, bool startWithWindows, Language language, bool romanOutput)
    {
        _statusItem = new Forms.ToolStripMenuItem("Starting…") { Enabled = false };
        var languageMenu = new Forms.ToolStripMenuItem("Language");
        foreach (var l in Languages.All)
        {
            var item = new Forms.ToolStripMenuItem(Languages.DisplayName(l)) { Checked = l == language };
            var chosen = l;
            item.Click += (_, _) => LanguageSelected?.Invoke(chosen);
            _languageItems[l] = item;
            languageMenu.DropDownItems.Add(item);
        }
        _romanItem = new Forms.ToolStripMenuItem("Write Indian languages in English letters") { Checked = romanOutput };
        _romanItem.Click += (_, _) => ToggleRoman?.Invoke();
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

        var menu = new Forms.ContextMenuStrip();
        menu.Items.AddRange(new Forms.ToolStripItem[]
        {
            _statusItem, new Forms.ToolStripSeparator(), languageMenu, _romanItem, new Forms.ToolStripSeparator(),
            _barItem, _startupItem, dictionary, models, new Forms.ToolStripSeparator(), quit,
        });
        _icon = new Forms.NotifyIcon { Icon = MakeIcon(), Text = "VoiceWeave", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => ToggleBar?.Invoke();
    }

    public void SetStatus(string text)
    {
        _statusItem.Text = text;
        _icon.Text = ("VoiceWeave - " + text)[..Math.Min(63, ("VoiceWeave - " + text).Length)]; // tooltip limit is 63 chars
    }

    public void SetLanguageChecked(Language language)
    {
        foreach (var (l, item) in _languageItems) item.Checked = l == language;
    }

    public void SetRomanChecked(bool on) => _romanItem.Checked = on;
    public void SetBarChecked(bool visible) => _barItem.Checked = visible;
    public void SetStartupChecked(bool enabled) => _startupItem.Checked = enabled;

    private static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var back = new SolidBrush(Color.FromArgb(255, 28, 28, 32));
            g.FillEllipse(back, 1, 1, 30, 30);
            using var white = new SolidBrush(Color.White);
            using var pen = new Pen(Color.White, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.FillRectangle(white, 12, 6, 8, 12);
            g.FillEllipse(white, 12, 3, 8, 8);
            g.FillEllipse(white, 12, 13, 8, 8);
            g.DrawArc(pen, 8, 9, 16, 14, 0, 180);
            g.DrawLine(pen, 16, 23, 16, 27);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
