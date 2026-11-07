using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Terrarium.App.Interop;
using Terrarium.Core.Rendering;

namespace Terrarium.App.Views;

/// <summary>
/// The terrarium itself: a borderless, per-pixel transparent window that never takes focus
/// (so clicking it doesn't count as switching apps), stays out of Alt-Tab and the taskbar,
/// and draws the 64×80 art at an integer physical-pixel scale so it's always crisp.
/// Positions are kept in physical pixels to stay correct across mixed-DPI monitors.
/// </summary>
internal sealed class WidgetWindow : Window
{
    private readonly WriteableBitmap _bitmap;
    private readonly Image _image;
    private readonly Border _hover;
    private readonly TextBlock _hoverText;
    private IntPtr _hwnd;
    private int _scale;

    public event Action? RightClicked;
    public event Action? DoubleClicked;
    public event Action<int>? ScaleRequested;
    public event Action? Moved;
    public Func<string>? HoverTextProvider { get; set; }

    public IntPtr Handle => _hwnd;

    public WidgetWindow(int scale)
    {
        _scale = scale;
        Title = "Terrarium";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _bitmap = new WriteableBitmap(TerrariumRenderer.Width, TerrariumRenderer.Height, 96, 96, PixelFormats.Pbgra32, null);
        _image = new Image { Source = _bitmap, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);

        _hoverText = new TextBlock
        {
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        _hover = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 34, 40, 34)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 3, 6, 4),
            Margin = new Thickness(6, 0, 6, 6),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Child = _hoverText,
        };

        var grid = new Grid();
        grid.Children.Add(_image);
        grid.Children.Add(_hover);
        Content = grid;

        SourceInitialized += OnSourceInitialized;
        DpiChanged += (_, _) => ApplyScale(_scale);
        MouseLeftButtonDown += OnLeftDown;
        MouseRightButtonUp += (_, e) => { e.Handled = true; RightClicked?.Invoke(); };
        MouseWheel += OnWheel;
        MouseEnter += (_, _) => ShowHover();
        MouseLeave += (_, _) => _hover.Visibility = Visibility.Collapsed;

        ApplyScale(scale);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        long ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
        ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
        ex &= ~Native.WS_EX_APPWINDOW;
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
        ApplyScale(_scale);
    }

    public void ApplyScale(int scale)
    {
        _scale = Math.Clamp(scale, 1, 8);
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (dpi <= 0) dpi = 1;
        // Integer physical pixels per art pixel; convert to WPF's device-independent units.
        _image.Width = TerrariumRenderer.Width * _scale / dpi;
        _image.Height = TerrariumRenderer.Height * _scale / dpi;
        _hoverText.FontSize = Math.Max(10, 3.6 * _scale / dpi + 1);
    }

    /// <summary>Changes size while keeping the bottom-centre in place, so plants "grow" upward from where they sit.</summary>
    public void SetScaleAnchored(int scale)
    {
        Native.RECT before = default;
        bool haveBefore = _hwnd != IntPtr.Zero && Native.GetWindowRect(_hwnd, out before);
        ApplyScale(scale);
        UpdateLayout();
        if (!haveBefore || !Native.GetWindowRect(_hwnd, out var after)) return;
        int w = after.Right - after.Left, h = after.Bottom - after.Top;
        int cx = (before.Left + before.Right) / 2;
        SetPhysicalPosition(cx - w / 2, before.Bottom - h);
        ClampToWorkArea();
    }

    public void Present(uint[] premultipliedBgra)
    {
        _bitmap.WritePixels(new Int32Rect(0, 0, TerrariumRenderer.Width, TerrariumRenderer.Height),
            premultipliedBgra, TerrariumRenderer.Width * 4, 0);
    }

    public void RefreshHover()
    {
        if (_hover.Visibility == Visibility.Visible) ShowHover();
    }

    private void ShowHover()
    {
        var text = HoverTextProvider?.Invoke();
        if (string.IsNullOrEmpty(text)) return;
        _hoverText.Text = text;
        _hover.Visibility = Visibility.Visible;
    }

    private void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            DoubleClicked?.Invoke();
            return;
        }
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return; // mouse released before the drag began
        }
        SnapToEdges();
        Moved?.Invoke();
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        ScaleRequested?.Invoke(_scale + (e.Delta > 0 ? 1 : -1));
        e.Handled = true;
    }

    // ───────────── physical-pixel positioning ─────────────

    public (int X, int Y) GetPhysicalPosition()
    {
        if (_hwnd == IntPtr.Zero || !Native.GetWindowRect(_hwnd, out var r)) return (0, 0);
        return (r.Left, r.Top);
    }

    public void SetPhysicalPosition(int x, int y)
    {
        if (_hwnd == IntPtr.Zero) return;
        Native.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    /// <summary>Places the widget at a saved spot, or bottom-right above the taskbar if that spot is off-screen.</summary>
    public void RestorePosition(double? x, double? y)
    {
        if (_hwnd == IntPtr.Zero) return;
        if (x is double sx && y is double sy)
        {
            var pt = new Native.POINT { X = (int)sx + 20, Y = (int)sy + 20 };
            if (Native.MonitorFromPoint(pt, Native.MONITOR_DEFAULTTONULL) != IntPtr.Zero)
            {
                SetPhysicalPosition((int)sx, (int)sy);
                ClampToWorkArea();
                return;
            }
        }
        var work = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1920, 1040);
        Native.GetWindowRect(_hwnd, out var r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        int margin = 12 * _scale / 3;
        SetPhysicalPosition(work.Right - w - margin, work.Bottom - h);
    }

    /// <summary>Snaps to monitor work-area edges (e.g. sitting right on the taskbar) when dropped close to one.</summary>
    public void SnapToEdges()
    {
        if (!TryGetWork(out var r, out var work)) return;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        int snap = 18 * Math.Max(1, _scale) / 3;
        int x = r.Left, y = r.Top;
        if (Math.Abs(x - work.Left) < snap) x = work.Left;
        if (Math.Abs(work.Right - (x + w)) < snap) x = work.Right - w;
        if (Math.Abs(y - work.Top) < snap) y = work.Top;
        if (Math.Abs(work.Bottom - (y + h)) < snap) y = work.Bottom - h;
        if (x != r.Left || y != r.Top) SetPhysicalPosition(x, y);
        ClampToWorkArea();
    }

    public void ClampToWorkArea()
    {
        if (!TryGetWork(out var r, out var work)) return;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        int x = Math.Clamp(r.Left, work.Left, Math.Max(work.Left, work.Right - w));
        int y = Math.Clamp(r.Top, work.Top, Math.Max(work.Top, work.Bottom - h));
        if (x != r.Left || y != r.Top) SetPhysicalPosition(x, y);
    }

    private bool TryGetWork(out Native.RECT window, out Native.RECT work)
    {
        work = default;
        if (_hwnd == IntPtr.Zero || !Native.GetWindowRect(_hwnd, out window)) { window = default; return false; }
        var mon = Native.MonitorFromWindow(_hwnd, Native.MONITOR_DEFAULTTONEAREST);
        var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(mon, ref mi)) return false;
        work = mi.rcWork;
        return true;
    }

    public void SetAlwaysOnTop(bool onTop)
    {
        Topmost = onTop;
        if (_hwnd != IntPtr.Zero)
            Native.SetWindowPos(_hwnd, onTop ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>The taskbar is also topmost; re-asserting now and then keeps the widget above it.</summary>
    public void ReassertTopmost()
    {
        if (_hwnd != IntPtr.Zero && Topmost)
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
    }
}
