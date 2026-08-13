using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>
/// The Win 3.1 move/size outline for the top-level window: while the caption or an edge is
/// dragged the window holds still and this shows a dithered frame at the proposed bounds, via
/// four topmost, click-through layered bar windows on the desktop (bars, not one big surface,
/// so a resize only ever repaints thin strips). Opt in per app with
/// <see cref="WindowGame.OutlineWindowDrag"/>; also carries the raw cursor/window queries the
/// drag state machine needs, because the cursor leaves the client area mid-drag and the
/// in-window input state cannot see it out there.
/// </summary>
internal static class DragOutline
{
    private const int Thickness = 4;
    private const string ClassName = "FnaWindowDragOutline";

    private static readonly IntPtr[] _bars = new IntPtr[4]; // top, bottom, left, right
    private static readonly int[] _barW = new int[4], _barH = new int[4];
    private static bool _registered;

    // ---- outline ----------------------------------------------------------------------------

    public static void Show(Rectangle b)
    {
        int t = Math.Min(Thickness, Math.Min(b.Width, b.Height) / 2);
        if (t <= 0) return;
        Bar(0, b.X, b.Y, b.Width, t);
        Bar(1, b.X, b.Bottom - t, b.Width, t);
        Bar(2, b.X, b.Y + t, t, b.Height - 2 * t);
        Bar(3, b.Right - t, b.Y + t, t, b.Height - 2 * t);
    }

    public static void Hide()
    {
        foreach (var bar in _bars)
            if (bar != IntPtr.Zero) ShowWindow(bar, SW_HIDE);
    }

    private static void Bar(int i, int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0) { if (_bars[i] != IntPtr.Zero) ShowWindow(_bars[i], SW_HIDE); return; }
        if (_bars[i] == IntPtr.Zero)
        {
            if (!_registered)
            {
                // No managed WndProc needed: the bars never handle a message themselves.
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    lpfnWndProc = GetProcAddress(GetModuleHandleW("user32"), "DefWindowProcW"),
                    hInstance = GetModuleHandleW(null),
                    lpszClassName = ClassName,
                };
                if (RegisterClassExW(ref wc) == 0) return;
                _registered = true;
            }
            _bars[i] = CreateWindowExW(
                WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                ClassName, string.Empty, WS_POPUP, 0, 0, w, h, IntPtr.Zero, IntPtr.Zero,
                GetModuleHandleW(null), IntPtr.Zero);
            if (_bars[i] == IntPtr.Zero) return;
        }
        if (w != _barW[i] || h != _barH[i]) Repaint(i, w, h);
        SetWindowPos(_bars[i], HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    // Paint a bar: a checkerboard dither of black and white pixels, so the frame reads over any
    // background. Bars are at most Thickness wide, so this stays cheap even when a resize drag
    // changes a bar's length every frame.
    private static void Repaint(int i, int w, int h)
    {
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(screen);
        var bmi = new BITMAPINFO { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        IntPtr bmp = CreateDIBSection(mem, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
        if (bmp == IntPtr.Zero) { DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen); return; }
        IntPtr old = SelectObject(mem, bmp);

        int black = unchecked((int)0xFF000000), white = unchecked((int)0xFFFFFFFF);
        var px = new int[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = ((x + y) & 1) == 0 ? black : white;
        Marshal.Copy(px, 0, bits, px.Length);

        var size = new POINT { X = w, Y = h };
        var srcPos = new POINT();
        var blend = new BLENDFUNCTION { SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
        UpdateLayeredWindow(_bars[i], screen, IntPtr.Zero, ref size, mem, ref srcPos, 0, ref blend, ULW_ALPHA);

        SelectObject(mem, old);
        DeleteObject(bmp);
        DeleteDC(mem);
        ReleaseDC(IntPtr.Zero, screen);
        _barW[i] = w; _barH[i] = h;
    }

    // ---- cursor/window queries for the drag state machine -----------------------------------

    public static bool LeftButtonDown() => (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

    public static Point CursorScreen()
    {
        GetCursorPos(out var p);
        return new Point(p.X, p.Y);
    }

    public static Point ToClient(IntPtr hwnd, Point screen)
    {
        var p = new POINT { X = screen.X, Y = screen.Y };
        ScreenToClient(hwnd, ref p);
        return new Point(p.X, p.Y);
    }

    public static Rectangle WindowBounds(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var r);
        return new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    public static void SetWindowBounds(IntPtr hwnd, Rectangle b)
        => SetWindowPos(hwnd, IntPtr.Zero, b.X, b.Y, b.Width, b.Height, SWP_NOZORDER | SWP_NOACTIVATE);

    public static uint DoubleClickMs() => GetDoubleClickTime();

    // ---- Win32 -------------------------------------------------------------------------------

    private const int VK_LBUTTON = 0x01;
    private const uint WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOPMOST = 0x8,
        WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
    private const uint WS_POPUP = 0x80000000;
    private const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    private const int SW_HIDE = 0;
    private const uint ULW_ALPHA = 2;
    private const byte AC_SRC_ALPHA = 1;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string name, uint style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dcDst, IntPtr posDst, ref POINT size,
        IntPtr dcSrc, ref POINT posSrc, uint colorKey, ref BLENDFUNCTION blend, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref POINT p);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO bmi, uint usage,
        out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? module);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);
}
