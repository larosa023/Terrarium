using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Terrarium.Core.Rendering;
using Drawing = System.Drawing;
using DrawingImaging = System.Drawing.Imaging;

namespace Terrarium.App.Views;

/// <summary>Builds tray and window icons from the same procedural pixel art as the .exe icon.</summary>
internal static class IconFactory
{
    private static BitmapSource? _windowIcon;

    public static BitmapSource WindowIcon => _windowIcon ??= CreateWindowIcon();

    private static BitmapSource CreateWindowIcon()
    {
        var art = IconArt.Render(32);
        var bmp = BitmapSource.Create(32, 32, 96, 96, PixelFormats.Pbgra32, null, art.Pixels, 32 * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>A crisp tray icon at the system's small-icon size (16 px at 100%, 32 px at 200%).</summary>
    public static Drawing.Icon CreateTrayIcon()
    {
        int size = System.Windows.Forms.SystemInformation.SmallIconSize.Width;
        var art = IconArt.Render(size >= 28 ? 32 : 16);
        using var bmp = new Drawing.Bitmap(size, size, DrawingImaging.PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Drawing.Rectangle(0, 0, size, size), DrawingImaging.ImageLockMode.WriteOnly, DrawingImaging.PixelFormat.Format32bppArgb);
        try
        {
            var row = new int[size];
            for (int y = 0; y < size; y++)
            {
                int sy = y * art.Height / size;
                for (int x = 0; x < size; x++)
                    row[x] = unchecked((int)art.GetStraight(x * art.Width / size, sy));
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, size);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        IntPtr hicon = bmp.GetHicon();
        // Clone so the Icon owns its own handle, then free the temporary one.
        using var tmp = Drawing.Icon.FromHandle(hicon);
        var icon = (Drawing.Icon)tmp.Clone();
        DestroyIcon(hicon);
        return icon;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
