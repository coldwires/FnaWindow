using System;
using System.Runtime.InteropServices;

namespace FnaWindow;

/// <summary>
/// Rounded window corners the way classic Windows did them: a window region clips the corner
/// pixels at the OS level, so the desktop shows through the arc. Skin art with rounded frame
/// corners pairs with this; without it, a transparent art corner exposes the app's own surface
/// as a square artifact (a rectangular window has no see-through pixels).
/// </summary>
internal static class WindowShape
{
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseW, int ellipseH);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    /// <summary>Apply (radius > 0) or clear (radius 0) the rounded region. The system takes
    /// ownership of the region handle, so nothing here needs disposing.</summary>
    public static void Apply(IntPtr hwnd, int width, int height, int radius)
    {
        if (hwnd == IntPtr.Zero || width <= 0 || height <= 0) return;
        IntPtr region = radius > 0 ? CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2) : IntPtr.Zero;
        SetWindowRgn(hwnd, region, true);
    }
}
