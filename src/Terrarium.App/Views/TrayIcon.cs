using System;
using System.Runtime.InteropServices;
using WF = System.Windows.Forms;

namespace Terrarium.App.Views;

/// <summary>System-tray icon with one context menu, shared with the widget's right-click.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly WF.NotifyIcon _icon;
    private readonly WF.ContextMenuStrip _menu;
    private Action? _balloonAction;

    /// <summary>Raised just before the menu opens, so it can be rebuilt with current state.</summary>
    public event Action? MenuOpening;
    public event Action? LeftClicked;
    public event Action? DoubleClicked;

    public WF.ToolStripItemCollection Items => _menu.Items;

    public TrayIcon()
    {
        _menu = new WF.ContextMenuStrip
        {
            ShowCheckMargin = true,
            ShowImageMargin = false,
            Font = new System.Drawing.Font("Segoe UI", 9f),
        };
        _menu.Opening += (_, _) => MenuOpening?.Invoke();

        _icon = new WF.NotifyIcon
        {
            Icon = IconFactory.CreateTrayIcon(),
            Text = "Terrarium",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == WF.MouseButtons.Left) LeftClicked?.Invoke(); };
        _icon.MouseDoubleClick += (_, e) => { if (e.Button == WF.MouseButtons.Left) DoubleClicked?.Invoke(); };
        _icon.BalloonTipClicked += (_, _) =>
        {
            var a = _balloonAction;
            _balloonAction = null;
            a?.Invoke();
        };
        _icon.BalloonTipClosed += (_, _) => _balloonAction = null;
    }

    public void Notify(string title, string text, Action? onClick = null)
    {
        _balloonAction = onClick;
        _icon.ShowBalloonTip(6000, title, text, WF.ToolTipIcon.None);
    }

    public void SetTooltip(string text) => _icon.Text = text.Length > 127 ? text[..127] : text;

    public void ShowMenuAtCursor()
    {
        _menu.Show(WF.Cursor.Position);
        // The widget never takes focus, so give the menu foreground; otherwise it won't close on outside clicks.
        SetForegroundWindow(_menu.Handle);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
