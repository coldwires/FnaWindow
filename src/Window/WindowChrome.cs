using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>
/// Native glue that hides the OS title bar and frame so only our Win 3.1 chrome shows, while
/// leaving the window a normal application window to Windows (see MakeBorderless for why that
/// distinction is load-bearing). Adapted from the fna-desktop-pet interop, but
/// without the layered/color-key/topmost bits (we're opaque). Also exposes SDL3 helpers to
/// move/resize the window and read the global cursor so our own title bar can drag it.
/// Windows-only; on other platforms <see cref="Supported"/> is false and the app keeps its
/// normal OS frame.
/// </summary>
internal static class WindowChrome
{
    public static bool Supported => OperatingSystem.IsWindows();

    // -- SDL3 --------------------------------------------------------------
    private const string SDL = "SDL3";
    private const string PROP_WIN32_HWND = "SDL.window.win32.hwnd";

    [DllImport(SDL)] private static extern uint SDL_GetWindowProperties(IntPtr window);
    [DllImport(SDL)] private static extern IntPtr SDL_CreateSurfaceFrom(
        int width, int height, uint format, IntPtr pixels, int pitch);
    [DllImport(SDL)] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowIcon(IntPtr window, IntPtr surface);
    [DllImport(SDL)] private static extern void SDL_DestroySurface(IntPtr surface);
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

    // -- Win32 (hide the frame, keep the window) ---------------------------
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
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);

    private const int SW_MAXIMIZE = 3, SW_RESTORE = 9;
    private const int SM_CXSIZEFRAME = 32, SM_CYSIZEFRAME = 33, SM_CXPADDEDBORDER = 92;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>
    /// Hides the OS frame while leaving the window a NORMAL, fully framed window as far as Windows
    /// is concerned. The frame bits stay ON and <c>WM_NCCALCSIZE</c> (below) collapses the
    /// non-client area to nothing, so the caption and border are never drawn but the window keeps
    /// everything the shell attaches to those styles.
    ///
    /// That distinction is the whole point. Stripping to a bare <c>WS_POPUP</c> - the obvious way,
    /// and what this used to do - silently costs every behaviour Windows grants a real window:
    /// <list type="bullet">
    /// <item><c>WS_THICKFRAME</c> is what makes a window snappable. Without it, dragging to a screen
    /// edge or corner does nothing, and Win+Arrow does nothing.</item>
    /// <item><c>WS_MAXIMIZEBOX</c> drives Snap Layouts (the grid that appears over the maximize
    /// button) and Win+Up.</item>
    /// <item><c>WS_MINIMIZEBOX</c> is what lets clicking the taskbar button minimize an active
    /// window, and gives the minimize/restore animation.</item>
    /// <item><c>WS_SYSMENU</c> is the Alt+Space menu and the taskbar right-click window menu.</item>
    /// </list>
    /// Answering <c>WM_NCHITTEST</c> with <c>HTCAPTION</c> gets you dragging and edge-resizing, which
    /// is why the old approach looked complete, but dragging is not snapping and none of the above
    /// comes back on its own.
    /// </summary>
    public static void MakeBorderless(IntPtr hwnd, int clientW, int clientH)
    {
        if (hwnd == IntPtr.Zero) return;
        ApplyFrameStyles(hwnd);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, clientW, clientH, SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    /// <summary>Re-applies the styles if something (SDL/FNA) changed them. Cheap; call per-frame.</summary>
    public static void EnsureBorderless(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        long style = (long)GetWindowLongPtr(hwnd, GWL_STYLE);
        if ((style & FrameBits) == FrameBits && (style & WS_POPUP) == 0) return; // already right
        ApplyFrameStyles(hwnd);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    private static void ApplyFrameStyles(IntPtr hwnd)
    {
        long style = (long)GetWindowLongPtr(hwnd, GWL_STYLE);
        style &= ~WS_POPUP;      // a popup is not a normal window and cannot snap
        style |= FrameBits | WS_VISIBLE;
        SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));
    }

    /// <summary>Maximize / restore through the OS, so the shell, snap and the taskbar all agree
    /// about the state instead of the app keeping a private idea of it.</summary>
    public static void Maximize(IntPtr hwnd) => ShowWindow(hwnd, SW_MAXIMIZE);
    public static void Restore(IntPtr hwnd) => ShowWindow(hwnd, SW_RESTORE);
    public static bool IsMaximized(IntPtr hwnd) => hwnd != IntPtr.Zero && IsZoomed(hwnd);

    /// <summary>The desktop work area (screen minus taskbar), for maximize.</summary>
    public static Rectangle WorkArea()
    {
        var r = new RECT();
        if (SystemParametersInfoW(SPI_GETWORKAREA, 0, ref r, 0))
            return new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        return new Rectangle(0, 0, 1024, 768);
    }

    // -- Subclassing: WM_NCCALCSIZE hides the frame, WM_NCHITTEST drives drag/resize ----
    // Instead of moving the window from Update (which fights WM_PAINT timing and greys the
    // window), we answer WM_NCHITTEST: HTCAPTION over the title bar -> Windows drags it (with
    // snap); HTLEFT/HTBOTTOMRIGHT/... near edges -> Windows resizes it. FNA/SDL still gets
    // every other message via the chained original WndProc.
    public const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12,
        HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    private const int GWLP_WNDPROC = -4;
    private const uint WM_NCHITTEST = 0x0084;
    private const uint WM_NCCALCSIZE = 0x0083;

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // Kept alive for the process lifetime so the native callback isn't GC'd.
    private static WndProc? _proc;
    private static IntPtr _origProc;
    private static Func<int, int, int>? _hitTest;

    [DllImport("user32.dll")] private static extern IntPtr CallWindowProcW(IntPtr prev, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINTL pt);

    [StructLayout(LayoutKind.Sequential)] private struct POINTL { public int X, Y; }

    /// <summary>Subclasses the window so <paramref name="hitTest"/>(clientX, clientY) -> a HT* code
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
        // The non-client area collapses to nothing, so the window keeps every frame style (and
        // everything the shell hangs off them) while none of the frame is ever drawn. Returning 0
        // with the proposed rectangle untouched means "the client area is the whole window".
        if (msg == WM_NCCALCSIZE && wParam != IntPtr.Zero)
        {
            // Maximized is the exception. Windows sizes a maximized window so its frame hangs off
            // all four edges, which is invisible on a normal window because the frame is where the
            // overhang goes. With no non-client area the overhang eats the content instead: the top
            // rows vanish and the bottom runs under the taskbar. Inset by the frame it assumed.
            if (IsZoomed(hWnd))
            {
                int pad = GetSystemMetrics(SM_CXPADDEDBORDER);
                int cx = GetSystemMetrics(SM_CXSIZEFRAME) + pad;
                int cy = GetSystemMetrics(SM_CYSIZEFRAME) + pad;

                var r = Marshal.PtrToStructure<RECT>(lParam); // first field of NCCALCSIZE_PARAMS
                r.Left += cx; r.Top += cy; r.Right -= cx; r.Bottom -= cy;
                Marshal.StructureToPtr(r, lParam, false);
            }
            return IntPtr.Zero;
        }

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

    // SDL_PIXELFORMAT_ABGR8888, which is byte order R,G,B,A in memory on a little-endian machine -
    // exactly how FNA lays out Color. Derived from SDL_DEFINE_PIXELFORMAT(PACKED32, ABGR, 8888, 32, 4):
    // (1<<28) | (6<<24) | (7<<20) | (6<<16) | (32<<8) | 4.
    private const uint PIXELFORMAT_ABGR8888 = 0x16762004;

    /// <summary>
    /// Sets the window's icon - what the taskbar and Alt+Tab show. This is separate from the exe
    /// icon the shell displays, which comes from the build (ApplicationIcon); an app usually wants
    /// both, from the same artwork.
    /// </summary>
    public static bool SetWindowIcon(IntPtr window, Color[] pixels, int width, int height)
    {
        if (window == IntPtr.Zero || pixels.Length < width * height) return false;
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            IntPtr surface = SDL_CreateSurfaceFrom(
                width, height, PIXELFORMAT_ABGR8888, pin.AddrOfPinnedObject(), width * 4);
            if (surface == IntPtr.Zero) return false;
            bool ok = SDL_SetWindowIcon(window, surface);
            SDL_DestroySurface(surface);   // SDL keeps its own copy of the icon
            return ok;
        }
        catch { return false; }
        finally { pin.Free(); }
    }
}
