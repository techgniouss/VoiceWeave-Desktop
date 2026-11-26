using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VoiceWeave.Core;
using VoiceWeave.Engine;

namespace VoiceWeave.App;

/// <summary>One line of the history list as shown on screen.</summary>
public sealed record HistoryRow(Guid Id, string When, string Mode, string Text);

/// <summary>The VoiceWeave window: home and welcome, history, dictionary and settings. Closing it leaves dictation running in the tray.</summary>
public partial class MainWindow : Window
{
    private readonly App _app;
    private bool _loading;
    private bool _dictionaryDirty;

    public MainWindow(App app)
    {
        InitializeComponent();
        _app = app;

        foreach (var mode in SpeechModes.All) ModeBox.Items.Add(new ComboBoxItem { Content = SpeechModes.DisplayName(mode), Tag = mode });
        foreach (var (label, days) in new[] { ("7 days", 7), ("30 days", 30), ("90 days", 90), ("Never", 0) })
            RetentionBox.Items.Add(new ComboBoxItem { Content = label, Tag = days });

        AboutText.Text = $"VoiceWeave {VersionText()}. Free and open source (MIT licence). Speech recognition runs on this PC; nothing you say is sent anywhere.";

        _app.StatusChanged += () => Dispatcher.BeginInvoke(RefreshStatus);
        _app.SettingsChanged += () => Dispatcher.BeginInvoke(RefreshSettings);
        _app.UpdateChanged += () => Dispatcher.BeginInvoke(RefreshUpdate);
        _app.Dictated += OnDictated;
        if (_app.History != null) _app.History.Changed += OnHistoryChanged;

        RefreshStatus();
        RefreshSettings();
        RefreshUpdate();
        RefreshHistory();
    }

    // ---- navigation ------------------------------------------------------------------------------------------------

    public void ShowPage(string page)
    {
        switch (page)
        {
            case "history": NavHistory.IsChecked = true; break;
            case "dictionary": NavDictionary.IsChecked = true; break;
            case "settings": NavSettings.IsChecked = true; break;
            default: NavHome.IsChecked = true; break;
        }
    }

    private void OnNav(object sender, RoutedEventArgs e)
    {
        if (HomePage == null) return; // the first radio button is checked before the pages exist
        HomePage.Visibility = NavHome.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = NavHistory.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        DictionaryPage.Visibility = NavDictionary.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (NavHistory.IsChecked == true) RefreshHistory();
        if (NavDictionary.IsChecked == true && !_dictionaryDirty) LoadDictionary();
        if (NavSettings.IsChecked == true) RefreshSettings();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // The X hides the window; the app keeps listening for Ctrl+Win. "Quit VoiceWeave" really quits.
        e.Cancel = true;
        Hide();
        _app.MainWindowHidden();
    }

    // ---- status and settings --------------------------------------------------------------------------------------------

    private void RefreshStatus()
    {
        var text = _app.StatusText;
        StatusText.Text = text;
        var failed = text.StartsWith("Could not", StringComparison.Ordinal) || text.Contains("unavailable") || text.StartsWith("No microphone", StringComparison.Ordinal);
        var ready = text.Contains("ready", StringComparison.OrdinalIgnoreCase) || text.StartsWith("Ready", StringComparison.Ordinal) || text.StartsWith("Last:", StringComparison.Ordinal);
        StatusDot.Fill = (Brush)FindResource(failed ? "Danger" : ready ? "Teal" : "Gold");
    }

    private void RefreshSettings()
    {
        _loading = true;
        try
        {
            var s = _app.Settings;
            foreach (ComboBoxItem item in ModeBox.Items) if ((SpeechMode)item.Tag == _app.Mode) ModeBox.SelectedItem = item;
            RomanBox.IsChecked = s.RomanOutput;
            StartupBox.IsChecked = _app.StartupEnabled;
            BarBox.IsChecked = s.BarVisible;
            LearnBox.IsChecked = s.LearnCorrections;
            KeepHistoryBox.IsChecked = s.KeepHistory;
            AutoUpdateBox.IsChecked = s.AutoUpdate;
            SoundAlikeBox.IsChecked = s.SoundAlikeMatching;
            ToneAppBox.IsChecked = s.ToneByApp;
            VocabBox.IsChecked = s.VocabularyCleanup;
            FasterBox.IsChecked = s.FasterCleanup;
            FasterStatusText.Text = _app.FasterCleanupStatus;
            WakeBox.IsChecked = s.WakeWordEnabled;
            if (!WakePhraseBox.IsKeyboardFocusWithin) WakePhraseBox.Text = s.WakeWordPhrase;
            WakeStatusText.Text = _app.WakeWordStatus;
            foreach (ComboBoxItem item in RetentionBox.Items) if ((int)item.Tag == s.HistoryDays) RetentionBox.SelectedItem = item;
            RetentionBox.IsEnabled = s.KeepHistory;
            ModelsPath.Text = AppPaths.Models;
            WelcomeCard.Visibility = s.WelcomeDismissed ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _loading = false; }
    }

    private void RefreshUpdate()
    {
        var update = _app.Update;
        UpdateBanner.Visibility = update != null ? Visibility.Visible : Visibility.Collapsed;
        if (update != null)
        {
            UpdateTitle.Text = $"VoiceWeave {update.Version} is available";
            UpdateDetail.Text = _app.UpdateBusy ? _app.UpdateStatus : "Installs in a minute and reopens by itself. Your settings, models and history stay.";
        }
        UpdateInstall.IsEnabled = !_app.UpdateBusy;
        CheckNowButton.IsEnabled = !_app.UpdateBusy;
        UpdateStatusText.Text = _app.UpdateStatus;
    }

    private void OnInstallUpdate(object sender, RoutedEventArgs e) => _ = _app.InstallUpdateAsync();
    private void OnCheckNow(object sender, RoutedEventArgs e) => _ = _app.CheckForUpdateAsync(manual: true);
    private void OnAutoUpdateClicked(object sender, RoutedEventArgs e) { if (!_loading) _app.SetAutoUpdate(AutoUpdateBox.IsChecked == true); }

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ModeBox.SelectedItem is not ComboBoxItem { Tag: SpeechMode mode }) return;
        _app.ChangeMode(mode);
    }

    private void OnRomanClicked(object sender, RoutedEventArgs e) { if (!_loading) _app.SetRoman(RomanBox.IsChecked == true); }
    private void OnStartupClicked(object sender, RoutedEventArgs e) { if (!_loading) _app.SetStartup(StartupBox.IsChecked == true); }
    private void OnBarClicked(object sender, RoutedEventArgs e) { if (!_loading) _app.SetBarVisible(BarBox.IsChecked == true); }
    private void OnToneAppClicked(object sender, RoutedEventArgs e) { if (!_loading) _app.SetToneByApp(ToneAppBox.IsChecked == true); }
    private void OnFasterClicked(object sender, RoutedEventArgs e) { if (!_loading) _app.SetFasterCleanup(FasterBox.IsChecked == true); }
    private void OnVocabClicked(object sender, RoutedEventArgs e) { if (!_loading) _app.SetVocabularyCleanup(VocabBox.IsChecked == true); }
    private void OnSoundAlikeClicked(object sender, RoutedEventArgs e) { if (!_loading) _app.SetSoundAlike(SoundAlikeBox.IsChecked == true); }
    private void OnWakeClicked(object sender, RoutedEventArgs e) { if (!_loading) _app.SetWakeWord(WakeBox.IsChecked == true); }
    private void OnSetWakePhrase(object sender, RoutedEventArgs e)
    {
        var problem = _app.SetWakeWordPhrase(WakePhraseBox.Text);
        if (problem != null) WakeStatusText.Text = problem;
    }
    private void OnLearnClicked(object sender, RoutedEventArgs e) { if (!_loading) _app.SetLearn(LearnBox.IsChecked == true); }
    private void OnKeepHistoryClicked(object sender, RoutedEventArgs e) { if (!_loading) _app.SetKeepHistory(KeepHistoryBox.IsChecked == true); }
    private void OnRetentionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || RetentionBox.SelectedItem is not ComboBoxItem { Tag: int days }) return;
        _app.SetHistoryDays(days);
        if (_app.History != null) _app.History.Changed += OnHistoryChanged; // a new store is opened with the new retention
        RefreshHistory();
    }

    private void OnDismissWelcome(object sender, RoutedEventArgs e) => _app.DismissWelcome();
    private void OnQuit(object sender, RoutedEventArgs e) => _app.QuitApp();

    // ---- history -----------------------------------------------------------------------------------------------------

    private void OnDictated(HistoryEntry entry)
    {
        LastInfo.Text = $"Last dictation: {entry.Seconds:F1} s from release to text.";
    }

    private void OnHistoryChanged() => Dispatcher.BeginInvoke(RefreshHistory);

    private void RefreshHistory()
    {
        var store = _app.History;
        if (store == null || HistoryList == null) return;
        var entries = store.Search(SearchBox.Text);
        HistoryList.ItemsSource = entries.Select(ToRow).ToList();
        var total = store.All.Count;
        HistoryCount.Text = total == 0 ? "" : entries.Count == total ? $"{total} dictation{(total == 1 ? "" : "s")}" : $"{entries.Count} of {total}";
        ClearAll.IsEnabled = total > 0;

        var empty = entries.Count == 0;
        HistoryEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty)
        {
            var searching = SearchBox.Text.Trim().Length > 0 && total > 0;
            HistoryEmptyTitle.Text = searching ? "No matches" : "Nothing here yet";
            HistoryEmptyText.Text = searching ? "Try a different word." : _app.Settings.KeepHistory
                ? "Your dictations will appear here." : "History is turned off. Switch it on below to start keeping one.";
        }
    }

    private static HistoryRow ToRow(HistoryEntry e)
    {
        var local = e.When.ToLocalTime();
        var day = local.Date == DateTime.Today ? "Today" : local.Date == DateTime.Today.AddDays(-1) ? "Yesterday" : local.ToString("d MMM");
        return new HistoryRow(e.Id, $"{day}, {local:HH:mm}", SpeechModes.DisplayName(SpeechModes.Parse(e.Mode)), e.Text);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => RefreshHistory();

    private void OnCopyEntry(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not Guid id) return;
        var entry = _app.History?.All.FirstOrDefault(x => x.Id == id);
        if (entry == null) return;
        try { Clipboard.SetText(entry.Text); ((Button)sender).Content = "Copied"; }
        catch (System.Runtime.InteropServices.COMException) { ((Button)sender).Content = "Try again"; } // another app holds the clipboard
    }

    private void OnDeleteEntry(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is Guid id) _app.History?.Delete(id);
    }

    private void OnClearHistory(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, "Delete every saved dictation? This cannot be undone.", "Clear history",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes) _app.History?.Clear();
    }

    // ---- dictionary --------------------------------------------------------------------------------------------------

    private void LoadDictionary()
    {
        try { DictionaryBox.Text = File.Exists(AppPaths.Dictionary) ? File.ReadAllText(AppPaths.Dictionary) : ""; }
        catch (IOException) { DictionaryBox.Text = ""; }
        _dictionaryDirty = false;
        DictionarySave.IsEnabled = DictionaryRevert.IsEnabled = false;
        DictionaryStatus.Text = "";
    }

    private void OnDictionaryEdited(object sender, TextChangedEventArgs e)
    {
        if (DictionarySave == null) return;
        // Loading the file also raises this event; only a real edit marks the text as changed.
        var onDisk = File.Exists(AppPaths.Dictionary) ? SafeRead(AppPaths.Dictionary) : "";
        _dictionaryDirty = DictionaryBox.Text != onDisk;
        DictionarySave.IsEnabled = DictionaryRevert.IsEnabled = _dictionaryDirty;
        DictionaryStatus.Text = _dictionaryDirty ? "Unsaved changes" : "";
    }

    private void OnSaveDictionary(object sender, RoutedEventArgs e)
    {
        try
        {
            File.WriteAllText(AppPaths.Dictionary, DictionaryBox.Text);
            _dictionaryDirty = false;
            DictionarySave.IsEnabled = DictionaryRevert.IsEnabled = false;
            DictionaryStatus.Text = "Saved. New rules work from your next dictation.";
        }
        catch (IOException ex) { DictionaryStatus.Text = "Could not save: " + ex.Message; }
        catch (UnauthorizedAccessException ex) { DictionaryStatus.Text = "Could not save: " + ex.Message; }
    }

    private void OnRevertDictionary(object sender, RoutedEventArgs e) => LoadDictionary();

    private static string SafeRead(string path)
    {
        try { return File.ReadAllText(path); }
        catch (IOException) { return ""; }
    }

    // ---- settings page buttons ------------------------------------------------------------------------------------------

    private void OnOpenModels(object sender, RoutedEventArgs e) { Directory.CreateDirectory(AppPaths.Models); App.Shell(AppPaths.Models); }
    private void OnOpenLog(object sender, RoutedEventArgs e) { if (File.Exists(AppPaths.Log)) App.Shell(AppPaths.Log); }
    private void OnOpenData(object sender, RoutedEventArgs e) => App.Shell(AppPaths.Root);
    private void OnOpenProject(object sender, RoutedEventArgs e) => App.Shell("https://github.com/techgniouss/VoiceWeave-Desktop");
    private void OnOpenLicences(object sender, RoutedEventArgs e)
    {
        var file = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY.txt");
        App.Shell(File.Exists(file) ? file : "https://github.com/techgniouss/VoiceWeave-Desktop/blob/main/desktop/licenses/THIRD-PARTY.txt");
    }

    private static string VersionText()
    {
        var info = FileVersionInfo.GetVersionInfo(typeof(MainWindow).Assembly.Location).ProductVersion ?? "";
        var plus = info.IndexOf('+');
        return plus > 0 ? info[..plus] : info;
    }
}
