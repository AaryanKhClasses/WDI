using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Runtime.InteropServices;
using WDI.Widgets.Clock;
using WinRT.Interop;

namespace WDI;

public enum IslandState
{
    Hidden, Opening, Collapsed, Collapsing, Closing, Expanded, Expanding
}

public sealed partial class MainWindow : Window
{
    private readonly AppWindow _appWindow;
    private readonly IntPtr _hwnd;

    private readonly ClockService _clockService = new();
    private DispatcherQueueTimer? _clockTimer;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int DWMWA_NCRENDERING_POLICY = 2;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMNCRP_DISABLED = 1;
    private const int VK_ESCAPE = 0x1B;

    private const int IslandWidth = 300;
    private const int IslandHeight = 50;
    private const int ExpandedWidth = 520;
    private const int ExpandedHeight = 250;
    private const int IslandVisibleOffset = 0;
    private const int OpenAnimationDurationMs = 250;
    private const int CloseAnimationDurationMs = 200;
    private const int ExpandAnimationDurationMs = 300;
    private const int CollapseAnimationDurationMs = 250;
    private DispatcherQueueTimer? _animationTimer;
    private DateTime _animationStartTime;
    private int _animationStartY;
    private int _animationTargetY;
    private int _animationDurationMs;
    private IslandState _animationTargetState;
    private int _animationStartWidth;
    private int _animationStartHeight;
    private int _animationTargetWidth;
    private int _animationTargetHeight;

    private const int TriggerWidth = 300;
    private const int TriggerHeight = 8;
    private const int ActivationDelayMs = 150;
    private const int CollapseDelayMs = 300;
    private DispatcherQueueTimer? _inputTimer;
    private DateTime? _triggerEnteredAt;
    private DateTime? _islandLeftAt;
    private IslandState _islandState = IslandState.Hidden;
    private bool _initialActivation = true;
    private bool _escapePressed = false;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public MainWindow()
    {
        InitializeComponent();
        Activated += OnWindowActivated;
        IslandBackground.PointerPressed += IslandBackground_PointerPressed;

        _hwnd = WindowNative.GetWindowHandle(this);
        var windowID = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowID);

        ConfigureWindow(_hwnd);
        StartClock();
        StartInputTracking();
    }

    private void IslandBackground_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_islandState == IslandState.Collapsed) ExpandIsland();
        if (_islandState == IslandState.Expanded) CollapseIsland();
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (!_initialActivation) return;
        _initialActivation = false;
        _appWindow.Hide();
    }

    private void StartClock()
    {
        UpdateClock();
        ScheduleNextClockUpdate();
    }

    private void ScheduleNextClockUpdate()
    {
        var now = _clockService.GetCurrentTime();
        var nextMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddMinutes(1);
        var delay = nextMinute - now;

        _clockTimer ??= DispatcherQueue.CreateTimer();
        _clockTimer.Stop();
        _clockTimer.Interval = delay;
        _clockTimer.Tick -= ClockTimerTick;
        _clockTimer.Tick += ClockTimerTick;
        _clockTimer.Start();
    }

    private void ClockTimerTick(DispatcherQueueTimer sender, object args)
    {
        UpdateClock();
        ScheduleNextClockUpdate();
    }

    private void UpdateClock()
    {
        var now = _clockService.GetCurrentTime();
        IslandText.Text = now.ToString("HH:mm");
        ExpandedClockView.Update();
    }

    private void ConfigureWindow(IntPtr hwnd)
    {
        const int radius = 25;

        var presenter = OverlappedPresenter.CreateForToolWindow();
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        _appWindow.SetPresenter(presenter);

        ConfigureNativeWindow(hwnd);
        _appWindow.Resize(new Windows.Graphics.SizeInt32 { Height = IslandHeight, Width = IslandWidth});
        SetRoundedWindowRegion(hwnd, IslandWidth, IslandHeight, radius);

        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var x = workArea.X + (workArea.Width - IslandWidth) / 2;
        var y = workArea.Y;
        _appWindow.Move(new Windows.Graphics.PointInt32 { X = x, Y = y });
    }

    private static void ConfigureNativeWindow(IntPtr hwnd)
    {
        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);

        var newExStyle = exStyle.ToInt64() | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(newExStyle));

        var ncrp = DWMNCRP_DISABLED;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_NCRENDERING_POLICY, ref ncrp, sizeof(int));

        var cornerPreference = 0;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

        var borderColor = 0x00000000;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
    }

    private static void SetRoundedWindowRegion(IntPtr hwnd, int width, int height, int radius)
    {
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        _ = SetWindowRgn(hwnd, region, true);
    }

    private void StartInputTracking()
    {
        _inputTimer = DispatcherQueue.CreateTimer();
        _inputTimer.Interval = TimeSpan.FromMilliseconds(16);
        _inputTimer.Tick += (_, _) => UpdateInputState();
        _inputTimer.Start();
    }

    private void UpdateInputState()
    {
        if (!GetCursorPos(out var cursor)) return;
        UpdateEscapeState();
        if (_islandState == IslandState.Hidden)
        {
            UpdateHiddenState(cursor);
            return;
        }
        UpdateVisibleState(cursor);
    }

    private void UpdateEscapeState()
    {
        bool escapePressed = (GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0;
        if (escapePressed && !_escapePressed)
            if (_islandState == IslandState.Expanded) CollapseIsland();

        _escapePressed = escapePressed;
    }

    private void UpdateHiddenState(POINT cursor)
    {
        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        int triggerLeft = workArea.X + (workArea.Width - TriggerWidth) / 2;
        int triggerRight = triggerLeft + TriggerWidth;
        int triggerTop = workArea.Y;
        int triggerBottom = triggerTop + TriggerHeight;

        bool insideTrigger = cursor.X >= triggerLeft && cursor.X <= triggerRight && cursor.Y >= triggerTop && cursor.Y <= triggerBottom;
        if (insideTrigger)
        {
            _triggerEnteredAt ??= DateTime.UtcNow;
            var elapsed = DateTime.UtcNow - _triggerEnteredAt.Value;
            if (elapsed.TotalMilliseconds >= ActivationDelayMs) ShowIsland();
        }
        else _triggerEnteredAt = null;
    }

    private void UpdateVisibleState(POINT cursor)
    {
        if (!GetWindowRect(_hwnd, out var rect)) return;
        bool insideIsland = cursor.X >= rect.Left && cursor.X <= rect.Right && cursor.Y >= rect.Top && cursor.Y <= rect.Bottom;
        if (insideIsland)
        {
            _islandLeftAt = null;
            if (_islandState == IslandState.Closing) ReverseOpening();
            return;
        }
        if (_islandState == IslandState.Opening || _islandState == IslandState.Expanding || _islandState == IslandState.Collapsing || _islandState == IslandState.Expanded) return;
        UpdateIslandExitState();
    }

    private void UpdateIslandExitState()
    {
        _islandLeftAt ??= DateTime.UtcNow;
        var elapsed = DateTime.UtcNow - _islandLeftAt.Value;
        if (elapsed.TotalMilliseconds >= CollapseDelayMs) HideIsland();
    }

    private void ShowIsland()
    {
        if (_islandState == IslandState.Collapsed || _islandState == IslandState.Opening) return;
        _triggerEnteredAt = null;
        _islandLeftAt = null;

        int hiddenY = GetHiddenY();
        int visibleY = GetVisibleY();
        _appWindow.Move(new Windows.Graphics.PointInt32 { X = GetIslandX(), Y = hiddenY });
        _islandState = IslandState.Opening;
        _appWindow.Show();
        StartAnimation(hiddenY, visibleY, OpenAnimationDurationMs, IslandState.Collapsed);
    }

    private void HideIsland()
    {
        if (_islandState == IslandState.Hidden || _islandState == IslandState.Closing) return;
        _islandLeftAt = null;

        int currentY = GetCurrentWindowY();
        int hiddenY = GetHiddenY();
        _islandState = IslandState.Closing;
        StartAnimation(currentY, hiddenY, CloseAnimationDurationMs, IslandState.Hidden);
    }

    private void ReverseOpening()
    {
        int currentY = GetCurrentWindowY();
        _islandState = IslandState.Opening;
        StartAnimation(currentY, GetVisibleY(), OpenAnimationDurationMs, IslandState.Collapsed);
    }

    private void ExpandIsland()
    {
        if (_islandState != IslandState.Collapsed) return;
        _islandLeftAt = null;

        int currentY = GetCurrentWindowY();
        _islandState = IslandState.Expanding;
        CollapsedContent.Visibility = Visibility.Collapsed;
        ExpandedContent.Visibility = Visibility.Collapsed;
        StartAnimation(currentY, currentY, ExpandAnimationDurationMs, IslandState.Expanded, IslandWidth, ExpandedWidth, IslandHeight, ExpandedHeight);
    }

    private void CollapseIsland()
    {
        if (_islandState != IslandState.Expanded) return;
        _islandState = IslandState.Collapsing;
        ExpandedContent.Visibility = Visibility.Collapsed;
        CollapsedContent.Visibility = Visibility.Collapsed;
        StartAnimation(GetCurrentWindowY(), GetVisibleY(), CollapseAnimationDurationMs, IslandState.Collapsed, ExpandedWidth, IslandWidth, ExpandedHeight, IslandHeight);
    }

    private static double EaseOutCubic(double t)
    {
        return 1 - Math.Pow(1 - t, 3);
    }

    private void StartAnimation(
        int startY, int targetY, int durationMs, IslandState targetState,
        int startWidth = IslandWidth, int targetWidth = IslandWidth,
        int startHeight = IslandHeight, int targetHeight = IslandHeight
    )
    {
        _animationStartTime = DateTime.UtcNow;
        _animationStartY = startY;
        _animationDurationMs = durationMs;
        _animationTargetY = targetY;
        _animationTargetState = targetState;
        _animationStartWidth = startWidth;
        _animationStartHeight = startHeight;
        _animationTargetWidth = targetWidth;
        _animationTargetHeight = targetHeight;

        _animationTimer ??= DispatcherQueue.CreateTimer();
        _animationTimer.Interval = TimeSpan.FromMilliseconds(16);
        _animationTimer.Tick -= AnimationTick;
        _animationTimer.Tick += AnimationTick;
        if (!_animationTimer.IsRunning) _animationTimer.Start();
    }

    private void AnimationTick(DispatcherQueueTimer sender, object args)
    {
        var elapsed = DateTime.UtcNow - _animationStartTime;
        double progress = elapsed.TotalMilliseconds / _animationDurationMs;
        progress = Math.Clamp(progress, 0, 1);
        double eased = EaseOutCubic(progress);

        if (_animationTargetState == IslandState.Collapsed && _animationStartHeight == IslandHeight && _animationStartWidth == IslandWidth && _animationTargetHeight == IslandHeight && _animationTargetWidth == IslandWidth)
        {
            double contentProgress = Math.Clamp(progress * 1.4, 0, 1);
            IslandText.Opacity = contentProgress;
        }

        if (_animationTargetState == IslandState.Hidden) IslandText.Opacity = 1 - progress;

        int y = (int)Math.Round(_animationStartY + (_animationTargetY - _animationStartY) * eased);
        int w = (int)Math.Round(_animationStartWidth + (_animationTargetWidth - _animationStartWidth) * eased);
        int h = (int)Math.Round(_animationStartHeight + (_animationTargetHeight - _animationStartHeight) * eased);
        int x = GetCenteredX(w);

        _appWindow.Resize(new Windows.Graphics.SizeInt32 { Width = w, Height = h });
        SetRoundedWindowRegion(_hwnd, w, h, GetCornerRadius(w, h));
        _appWindow.Move(new Windows.Graphics.PointInt32 { X = x, Y = y });

        if (progress >= 1.0)
        {
            _animationTimer?.Stop();
            _appWindow.Resize(new Windows.Graphics.SizeInt32 { Width = _animationTargetWidth, Height = _animationTargetHeight });
            SetRoundedWindowRegion(_hwnd, _animationTargetWidth, _animationTargetHeight, GetCornerRadius(_animationTargetWidth, _animationTargetHeight));
            _appWindow.Move(new Windows.Graphics.PointInt32 { X = GetCenteredX(_animationTargetWidth), Y = _animationTargetY });
            _islandState = _animationTargetState;
            if (_islandState == IslandState.Expanded) ExpandedContent.Visibility = Visibility.Visible;
            if (_islandState == IslandState.Collapsed) CollapsedContent.Visibility = Visibility.Visible;
            if (_islandState == IslandState.Hidden) _appWindow.Hide();
        }
    }

    private int GetIslandX()
    {
        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        return workArea.X + (workArea.Width - IslandWidth) / 2;
    }

    private int GetCenteredX(int width)
    {
        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        return workArea.X + (workArea.Width - width) / 2;
    }

    private int GetHiddenY()
    {
        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        return workArea.Y - IslandHeight;
    }

    private int GetVisibleY()
    {
        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        return workArea.Y + IslandVisibleOffset;
    }

    public int GetCurrentWindowY()
    {
        if (!GetWindowRect(_hwnd, out var rect)) return GetVisibleY();
        return rect.Top;
    }

    private static int GetCornerRadius(int width, int height)
    {
        if (width <= IslandWidth && height <= IslandHeight) return height / 2;
        return 30;
    }

    private void ShowContent() => IslandText.Opacity = 1;
    private void HideContent() => IslandText.Opacity = 0;
}
