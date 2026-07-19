using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>
/// Native glue to turn the FNA window into a true borderless window - no OS title bar or
/// frame, so only our Win 3.1 chrome shows. Adapted from the fna-desktop-pet interop, but
/// without the layered/color-key/topmost bits (we're opaque). Also exposes SDL3 helpers to
/// move/resize the window and read the global cursor so our own title bar can drag it.
/// Windows-only; on other platforms <see cref="Supported"/> is false and the app keeps its
/// normal OS frame.
/// </summary>
internal static class WindowChrome
{
    public static bool Supported => OperatingSystem.IsWindows();

    // ── SDL3 ──────────────────────────────────────────────────────────────
    private const string SDL = "SDL3";
    private const string PROP_WIN32_HWND = "SDL.window.win32.hwnd";

    [DllImport(SDL)] private static extern uint SDL_GetWindowProperties(IntPtr window);
    [DllImport(SDL)] private static extern IntPtr SDL_GetPointerProperty(
        uint props, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr defaultValue);
    [DllImport(SDL)] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowPosition(IntPtr window, int x, int y);
    [DllImport(SDL)] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GetWindowPosition(IntPtr window, out int x, out int y);
    [DllImport(SDL)] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowSize(IntPtr window, int w, int h);
    [DllImport(SDL)] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GetWindowSize(IntPtr window, out int w, out int h);
    [DllImport(SDL)] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_MinimizeWindow(IntPtr window);
    [DllImport(SDL)] private static extern uint SDL_GetGlobalMouseState(out float x, out float y);

    public static IntPtr GetHwnd(IntPtr sdlWindow)
    {
        uint props = SDL_GetWindowProperties(sdlWindow);
        return SDL_GetPointerProperty(props, PROP_WIN32_HWND, IntPtr.Zero);
    }

    public static void SetWindowPosition(IntPtr w, int x, int y) => SDL_SetWindowPosition(w, x, y);
    public static void GetWindowPosition(IntPtr w, out int x, out int y) => SDL_GetWindowPosition(w, out x, out y);
    public static void SetWindowSize(IntPtr w, int cw, int ch) => SDL_SetWindowSize(w, cw, ch);
    public static void GetWindowSize(IntPtr w, out int cw, out int ch) => SDL_GetWindowSize(w, out cw, out ch);
    public static void Minimize(IntPtr w) => SDL_MinimizeWindow(w);

    public static Point GetGlobalMouse()
    {
        SDL_GetGlobalMouseState(out float x, out float y);
        return new Point((int)x, (int)y);
    }

    // ── Win32 (strip the frame) ───────────────────────────────────────────
    private const int GWL_STYLE = -16;
    private const long WS_POPUP = 0x80000000L;
    private const long WS_VISIBLE = 0x10000000L;
    private const long WS_CAPTION = 0x00C00000L;
    private const long WS_THICKFRAME = 0x00040000L;
    private const long WS_MINIMIZEBOX = 0x00020000L;
    private const long WS_MAXIMIZEBOX = 0x00010000L;
    private const long WS_SYSMENU = 0x00080000L;
    private const long FrameBits = WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU;

    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SPI_GETWORKAREA = 0x0030;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>Strips the OS frame to a bare WS_POPUP and sizes the client to the backbuffer
    /// (client == backbuffer keeps FNA3D rendering straight to the swapchain - no blit blur).</summary>
    public static void MakeBorderless(IntPtr hwnd, int clientW, int clientH)
    {
        if (hwnd == IntPtr.Zero) return;
        long style = (long)GetWindowLongPtr(hwnd, GWL_STYLE);
        style &= ~FrameBits;
        style |= WS_POPUP | WS_VISIBLE;
        SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, clientW, clientH, SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    /// <summary>Re-strips the caption if something (SDL/FNA) re-added it. Cheap; call per-frame.</summary>
    public static void EnsureBorderless(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        long style = (long)GetWindowLongPtr(hwnd, GWL_STYLE);
        if ((style & FrameBits) == 0) return;
        style &= ~FrameBits;
        style |= WS_POPUP | WS_VISIBLE;
        SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    /// <summary>The desktop work area (screen minus taskbar), for maximize.</summary>
    public static Rectangle WorkArea()
    {
        var r = new RECT();
        if (SystemParametersInfoW(SPI_GETWORKAREA, 0, ref r, 0))
            return new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        return new Rectangle(0, 0, 1024, 768);
    }

    // ── WM_NCHITTEST subclassing (native drag / resize / Aero Snap) ───────
    // Instead of moving the window from Update (which fights WM_PAINT timing and greys the
    // window), we answer WM_NCHITTEST: HTCAPTION over the title bar → Windows drags it (with
    // snap); HTLEFT/HTBOTTOMRIGHT/... near edges → Windows resizes it. FNA/SDL still gets
    // every other message via the chained original WndProc.
    public const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12,
        HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    private const int GWLP_WNDPROC = -4;
    private const uint WM_NCHITTEST = 0x0084;

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // Kept alive for the process lifetime so the native callback isn't GC'd.
    private static WndProc? _proc;
    private static IntPtr _origProc;
    private static Func<int, int, int>? _hitTest;

    [DllImport("user32.dll")] private static extern IntPtr CallWindowProcW(IntPtr prev, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINTL pt);

    [StructLayout(LayoutKind.Sequential)] private struct POINTL { public int X, Y; }

    /// <summary>Subclasses the window so <paramref name="hitTest"/>(clientX, clientY) → a HT* code
    /// drives native move/resize. Returning <see cref="HTCLIENT"/> keeps normal widget behavior.</summary>
    public static void InstallHitTest(IntPtr hwnd, Func<int, int, int> hitTest)
    {
        if (hwnd == IntPtr.Zero) return;
        _hitTest = hitTest;
        _proc = HitTestProc;
        _origProc = GetWindowLongPtr(hwnd, GWLP_WNDPROC);
        SetWindowLongPtr(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_proc));
    }

    private static IntPtr HitTestProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_NCHITTEST && _hitTest != null)
        {
            long lp = lParam.ToInt64();
            var pt = new POINTL { X = (short)(lp & 0xFFFF), Y = (short)((lp >> 16) & 0xFFFF) };
            ScreenToClient(hWnd, ref pt);
            int ht = _hitTest(pt.X, pt.Y);
            if (ht != HTCLIENT) return new IntPtr(ht);
        }
        return CallWindowProcW(_origProc, hWnd, msg, wParam, lParam);
    }
}
