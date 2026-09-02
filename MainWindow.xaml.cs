using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace WDI;

public sealed partial class MainWindow : Window
{
    private readonly AppWindow _appWindow;
    private readonly IntPtr _hwnd;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int DWMWA_NCRENDERING_POLICY = 2;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMNCRP_DISABLED = 1;

    private const int TriggerWidth = 300;
    private const int TriggerHeight = 8;
    private const int ActivationDelayMs = 150;
    private const int CollapseDelayMs = 300;
    private DispatcherQueueTimer? _mouseTimer;
    private DateTime? _triggerEnteredAt;
    private DateTime? _islandLeftAt;
    private bool _islandVisible;
    private bool _initialActivation = true;

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

    public MainWindow()
    {
        InitializeComponent();
        Activated += OnWindowActivated;

        _hwnd = WindowNative.GetWindowHandle(this);
        var windowID = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowID);

        ConfigureWindow(_hwnd);
        StartMouseTracking();
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (!_initialActivation) return;
        _initialActivation = true;
        _appWindow.Hide();
    }

    private void ConfigureWindow(IntPtr hwnd)
    {
        const int width = 300;
        const int height = 50;
        const int radius = 25;

        var presenter = OverlappedPresenter.CreateForToolWindow();
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        _appWindow.SetPresenter(presenter);

        ConfigureNativeWindow(hwnd);
        _appWindow.Resize(new Windows.Graphics.SizeInt32 { Height = height, Width = width});
        SetRoundedWindowRegion(hwnd, width, height, radius);

        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var x = workArea.X + (workArea.Width - width) / 2;
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

    private void StartMouseTracking()
    {
        _mouseTimer = DispatcherQueue.CreateTimer();
        _mouseTimer.Interval = TimeSpan.FromMilliseconds(16);
        _mouseTimer.Tick += (_, _) => UpdateMouseState();
        _mouseTimer.Start();
    }

    private void UpdateMouseState()
    {
        if (!GetCursorPos(out var cursor)) return;
        if(!_islandVisible)
        {
            UpdateHiddenState(cursor);
            return;
        }
        UpdateVisibleState(cursor);
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
        if(insideTrigger)
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
            return;
        }
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
        if (_islandVisible) return;
        _triggerEnteredAt = null;
        _islandLeftAt = null;
        _islandVisible = true;
        _appWindow.Show();
    }

    private void HideIsland()
    {
        if (!_islandVisible) return;
        _islandVisible = false;
        _islandLeftAt = null;
        _appWindow.Hide();
    }
}
