using System;
using SDL3;

namespace FnaWindow;

/// <summary>
/// The borderless window away from Windows, through SDL alone: the OS frame is turned off and SDL's
/// hit test hands the compositor the caption and edges, so it moves and resizes the window itself
/// (on Wayland there is no other way to move one). Maximize, restore and minimize go through SDL,
/// so the desktop's own idea of the state is the one the caption shows.
///
/// Windows keeps <see cref="WindowChrome"/>, which does more there (snap, the window menu).
/// </summary>
internal static class SdlChrome
{
    public static bool Supported => !OperatingSystem.IsWindows();

    // Held for the life of the window: SDL keeps only the function pointer.
    private static SDL.SDL_HitTest? _hitTest;
    private static Func<int, int, int>? _classify;
    private static Action? _doubleClick;

    // SDL asks on every pointer move as well as on a press (Wayland, SDL 3.4). A move always arrives
    // at a new point; a press arrives at the point the last move left. Two presses on the caption at
    // that same spot inside DoubleClickMs are a double-click, which on Windows the OS turns into
    // maximize or restore. Checked against a trace from KDE Plasma: 12 repeats in 5320 calls, every
    // one a press.
    private const int DoubleClickMs = 400, DoubleClickDist = 4;
    private static int _lastX = int.MinValue, _lastY = int.MinValue, _pressX, _pressY;
    private static long _pressMs = long.MinValue;

    /// <summary>FNAWINDOW_HITLOG=1: every hit test SDL asks for goes to stderr (the point, the window
    /// size, the Win32 hit code answered), to see which part of the caption grabs.</summary>
    private static readonly bool HitLog = Environment.GetEnvironmentVariable("FNAWINDOW_HITLOG") == "1";

    /// <param name="classify">Window pixel to a Win32 hit code (WindowChrome.HT*), the same
    /// classifier the Windows path uses.</param>
    /// <param name="doubleClick">A double-click on the caption, posted to the game thread.</param>
    public static unsafe bool MakeBorderless(IntPtr window, Func<int, int, int> classify, Action doubleClick)
    {
        if (window == IntPtr.Zero) return false;
        try
        {
            _classify = classify;
            _doubleClick = doubleClick;
            _hitTest = HitTest;
            if (!SDL.SDL_SetWindowHitTest(window, _hitTest, IntPtr.Zero)) return false;
            SDL.SDL_SetWindowBordered(window, false);
            return true;
        }
        catch { return false; }
    }

    private static unsafe SDL.SDL_HitTestResult HitTest(IntPtr win, SDL.SDL_Point* area, IntPtr data)
    {
        if (_classify == null || area == null) return SDL.SDL_HitTestResult.SDL_HITTEST_NORMAL;
        // SDL reports window points; the classifier works in the drawable's pixels.
        float d = SDL.SDL_GetWindowPixelDensity(win);
        if (d <= 0) d = 1;
        int code;
        try { code = _classify((int)(area->x * d), (int)(area->y * d)); }
        catch { return SDL.SDL_HitTestResult.SDL_HITTEST_NORMAL; }
        bool press = area->x == _lastX && area->y == _lastY;
        _lastX = area->x; _lastY = area->y;
        bool twice = false;
        if (press && code == WindowChrome.HTCAPTION)
        {
            long now = Environment.TickCount64;
            twice = _pressMs != long.MinValue && now - _pressMs <= DoubleClickMs
                && Math.Abs(area->x - _pressX) <= DoubleClickDist && Math.Abs(area->y - _pressY) <= DoubleClickDist;
            _pressMs = twice ? long.MinValue : now;   // a third press starts a new pair
            _pressX = area->x; _pressY = area->y;
        }
        if (HitLog)
        {
            SDL.SDL_GetWindowSize(win, out int w, out int h);
            Console.Error.WriteLine($"hit {DateTime.Now:HH:mm:ss.fff} at {area->x},{area->y} in {w}x{h}: {code}{(press ? " press" : "")}{(twice ? " double" : "")}");
        }
        if (twice && _doubleClick is { } act)
        {
            MainThread.Post(act);
            return SDL.SDL_HitTestResult.SDL_HITTEST_NORMAL;   // no move for the second press
        }
        return code switch
        {
            WindowChrome.HTCAPTION => SDL.SDL_HitTestResult.SDL_HITTEST_DRAGGABLE,
            WindowChrome.HTLEFT => SDL.SDL_HitTestResult.SDL_HITTEST_RESIZE_LEFT,
            WindowChrome.HTRIGHT => SDL.SDL_HitTestResult.SDL_HITTEST_RESIZE_RIGHT,
            WindowChrome.HTTOP => SDL.SDL_HitTestResult.SDL_HITTEST_RESIZE_TOP,
            WindowChrome.HTBOTTOM => SDL.SDL_HitTestResult.SDL_HITTEST_RESIZE_BOTTOM,
            WindowChrome.HTTOPLEFT => SDL.SDL_HitTestResult.SDL_HITTEST_RESIZE_TOPLEFT,
            WindowChrome.HTTOPRIGHT => SDL.SDL_HitTestResult.SDL_HITTEST_RESIZE_TOPRIGHT,
            WindowChrome.HTBOTTOMLEFT => SDL.SDL_HitTestResult.SDL_HITTEST_RESIZE_BOTTOMLEFT,
            WindowChrome.HTBOTTOMRIGHT => SDL.SDL_HitTestResult.SDL_HITTEST_RESIZE_BOTTOMRIGHT,
            _ => SDL.SDL_HitTestResult.SDL_HITTEST_NORMAL,
        };
    }

    public static bool IsMaximized(IntPtr window)
    {
        try { return window != IntPtr.Zero && (SDL.SDL_GetWindowFlags(window) & SDL.SDL_WindowFlags.SDL_WINDOW_MAXIMIZED) != 0; }
        catch { return false; }
    }

    public static void Maximize(IntPtr window) { try { SDL.SDL_MaximizeWindow(window); } catch { } }
    public static void Restore(IntPtr window) { try { SDL.SDL_RestoreWindow(window); } catch { } }
}
