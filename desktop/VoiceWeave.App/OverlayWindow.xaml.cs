using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using VoiceWeave.Engine;

namespace VoiceWeave.App;

/// <summary>
/// The floating bar. It never takes focus from the app you are typing in. Press and hold it to dictate (release to
/// finish), or click it once to start and click again to finish. Drag it to move it.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;
    private const int WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
    private const int BarCount = 9;
    private static readonly TimeSpan HoldAfter = TimeSpan.FromMilliseconds(220);
    private const double DragThreshold = 6;

    private readonly Settings _settings;
    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private readonly float[] _levels = new float[BarCount];
    private readonly DispatcherTimer _holdTimer = new() { Interval = HoldAfter };
    private readonly DispatcherTimer _toastTimer = new();
    private readonly DispatcherTimer _dotsTimer = new() { Interval = TimeSpan.FromMilliseconds(260) };
    private DictationState _state = DictationState.Idle;
    private Point _downScreen;
    private bool _holdFired, _suppressUp;
    private int _dotPhase;

    /// <summary>The button was pressed and held: start dictating, push-to-talk style.</summary>
    public event Action? HoldStarted;
    /// <summary>The held button was released: finish dictating.</summary>
    public event Action? HoldEnded;
    /// <summary>A quick click: toggles hands-free dictation.</summary>
    public event Action? Clicked;
    /// <summary>Right-click on the bar: show the same menu as the tray icon.</summary>
    public event Action? MenuRequested;

    public OverlayWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;
        for (var i = 0; i < BarCount; i++)
        {
            _bars[i] = new Rectangle { Width = 3, Height = 4, RadiusX = 1.5, RadiusY = 1.5, Margin = new Thickness(1.2, 0, 1.2, 0), Fill = new SolidColorBrush(Color.FromRgb(0xA9, 0xF2, 0xCC)) };
            Bars.Children.Add(_bars[i]);
        }
        _holdTimer.Tick += (_, _) => { _holdTimer.Stop(); if (Mouse.LeftButton == MouseButtonState.Pressed) { _holdFired = true; HoldStarted?.Invoke(); } };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast.Visibility = Visibility.Collapsed; };
        _dotsTimer.Tick += (_, _) => { _dotPhase = (_dotPhase + 1) % 4; Dots.Text = new string('·', _dotPhase == 0 ? 1 : _dotPhase); };

        Pill.MouseLeftButtonDown += OnDown;
        Pill.MouseMove += OnMove;
        Pill.MouseLeftButtonUp += OnUp;
        Pill.MouseRightButtonUp += (_, e) => { MenuRequested?.Invoke(); e.Handled = true; };
        Pill.MouseEnter += (_, _) => { if (_state == DictationState.Idle) Pill.Opacity = 1; };
        Pill.MouseLeave += (_, _) => { if (_state == DictationState.Idle) Pill.Opacity = 0.78; };
        SizeChanged += OnSizeChanged;
        Loaded += (_, _) => PlaceInitially();
    }

    public IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_MOUSEACTIVATE) return IntPtr.Zero;
        handled = true;
        return new IntPtr(MA_NOACTIVATE); // clicking the bar must not pull focus out of the app being dictated into
    }

    public void SetState(DictationState state, bool latched)
    {
        _state = state;
        MicIcon.Visibility = state == DictationState.Idle ? Visibility.Visible : Visibility.Collapsed;
        Bars.Visibility = state == DictationState.Recording ? Visibility.Visible : Visibility.Collapsed;
        Dots.Visibility = state == DictationState.Processing ? Visibility.Visible : Visibility.Collapsed;
        Pill.Opacity = state == DictationState.Idle ? 0.78 : 1;
        Pill.Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x10, 0x28, 0x21));
        Pill.BorderBrush = new SolidColorBrush(state == DictationState.Recording ? Color.FromRgb(0xF2, 0xCB, 0x8C) : Color.FromArgb(0x55, 0x63, 0xB9, 0x9C));
        Pill.MinWidth = state == DictationState.Recording ? 92 : 34;
        if (state == DictationState.Processing) { Dots.Text = "·"; _dotPhase = 0; _dotsTimer.Start(); } else _dotsTimer.Stop();
        if (state != DictationState.Recording) Array.Clear(_levels);
        if (state == DictationState.Recording && latched) ShowToast("Hands-free: click again to finish", 2500);
    }

    public void PushLevel(float rms)
    {
        for (var i = 0; i < BarCount - 1; i++) _levels[i] = _levels[i + 1];
        _levels[BarCount - 1] = Math.Min(1f, rms * 7f);
        for (var i = 0; i < BarCount; i++) _bars[i].Height = 4 + _levels[i] * 20;
    }

    public void ShowToast(string text, int milliseconds = 2500)
    {
        ToastText.Text = text;
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _toastTimer.Start();
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _downScreen = PointToScreen(e.GetPosition(this));
        _holdFired = false;
        _suppressUp = false;
        Pill.CaptureMouse();
        _holdTimer.Start();
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _holdFired || _suppressUp) return;
        var now = PointToScreen(e.GetPosition(this));
        if (Math.Abs(now.X - _downScreen.X) < DragThreshold && Math.Abs(now.Y - _downScreen.Y) < DragThreshold) return;
        _holdTimer.Stop();
        _suppressUp = true;
        Pill.ReleaseMouseCapture();
        DragMove();                    // returns when the button is released
        _settings.BarLeft = Left;
        _settings.BarTop = Top;
        _settings.Save();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        Pill.ReleaseMouseCapture();
        _holdTimer.Stop();
        if (_suppressUp) { _suppressUp = false; return; }
        if (_holdFired) HoldEnded?.Invoke();
        else Clicked?.Invoke();
        _holdFired = false;
        e.Handled = true;
    }

    private void PlaceInitially()
    {
        var work = SystemParameters.WorkArea;
        var left = _settings.BarLeft ?? work.Left + (work.Width - ActualWidth) / 2;
        var top = _settings.BarTop ?? work.Bottom - ActualHeight - 10;
        // A remembered spot on a monitor that is no longer there would hide the bar: fall back to the default.
        var virtualScreen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!virtualScreen.Contains(new Point(left + 4, top + 4)))
        {
            left = work.Left + (work.Width - ActualWidth) / 2;
            top = work.Bottom - ActualHeight - 10;
        }
        Left = left;
        Top = top;
    }

    // Keep the bar's bottom edge and centre fixed when the toast or the recording pill changes the window size.
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded || e.PreviousSize.Height == 0) return;
        Top += e.PreviousSize.Height - e.NewSize.Height;
        Left += (e.PreviousSize.Width - e.NewSize.Width) / 2;
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
