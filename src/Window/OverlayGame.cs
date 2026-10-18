using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FnaWindow;

/// <summary>
/// A window that floats over the desktop and shows only what it draws: always on top, no taskbar
/// button, never takes the focus, and see-through and click-through everywhere it draws nothing.
/// For desktop pets and the like.
///
/// Subclass it and draw in <see cref="DrawScene"/>; whatever is left untouched is not part of the
/// window. Mouse input arrives in <see cref="Input"/> while the pointer is over a drawn pixel, though
/// the window is never active.
///
/// It is a colour-keyed layered window: every frame starts filled with <see cref="Key"/>, and Windows
/// shows that colour as a hole, on each frame as it is shown, so the holes can never lag behind the
/// picture. Edges are hard, which is right for pixel art: a pixel blended part way with the key
/// shows as a tinted fringe, so draw opaque or not at all.
///
/// Clicks are a separate matter: with a GPU-drawn layered window the key does not let them through,
/// so the window is made transparent to the mouse altogether, and only while the pointer is over
/// something the subclass calls <see cref="Solid"/> (or a drag that began on it is held) does it
/// take the mouse.
/// </summary>
public abstract class OverlayGame : Game
{
    /// <summary>The colour that is a hole. Nothing drawn may be exactly this colour.</summary>
    public static readonly Color Key = new(255, 0, 254);

    protected readonly GraphicsDeviceManager Graphics;
    protected SpriteBatch Batch = null!;
    protected readonly InputState Input = new();
    protected IntPtr Hwnd { get; private set; }
    /// <summary>Where the window covers, in screen pixels.</summary>
    protected Rectangle Area { get; private set; }

    private double _topmostMs;
    private bool _takesMouse = true;   // so the first check sets it either way

    // SDL reads its hints from the environment when it starts, which happens in Game's constructor,
    // before any subclass code runs; a static constructor runs before all of that.
    static OverlayGame()
    {
        // Showing or raising the window must not take the focus from whatever the user is in.
        Environment.SetEnvironmentVariable("SDL_WINDOW_ACTIVATE_WHEN_SHOWN", "0");
        Environment.SetEnvironmentVariable("SDL_WINDOW_ACTIVATE_WHEN_RAISED", "0");
        // A layered window's colour key applies to what reaches the window's redirection surface;
        // Direct3D 11's flip-model swap chain bypasses it. OpenGL presents the classic way.
        if (Environment.GetEnvironmentVariable("FNA3D_FORCE_DRIVER") == null)
            Environment.SetEnvironmentVariable("FNA3D_FORCE_DRIVER", "OpenGL");
        // Physical pixels from the start: the subclass measures the screen before SDL, which would
        // make the process DPI aware, is up. Unaware, the work area comes back scaled down.
        try { SetProcessDpiAwarenessContext(new IntPtr(-4) /* per monitor v2 */); } catch { }
    }

    protected OverlayGame(Rectangle area)
    {
        Area = area;
        Graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = area.Width,
            PreferredBackBufferHeight = area.Height,
            SynchronizeWithVerticalRetrace = true,
        };
        IsFixedTimeStep = true;
        IsMouseVisible = true;
        Window.IsBorderlessEXT = true;
        Window.Title = "";
    }

    /// <summary>Draw everything; the batch is begun with point sampling and alpha blending.</summary>
    protected abstract void DrawScene(SpriteBatch batch);

    protected abstract void UpdateScene(GameTime t);

    /// <summary>Whether something that should take the mouse is at this point, in window pixels.</summary>
    protected abstract bool Solid(Point p);

    protected override void LoadContent()
    {
        Batch = new SpriteBatch(GraphicsDevice);
        Hwnd = WindowChrome.GetHwnd(Window.Handle);
        if (Hwnd != IntPtr.Zero)
        {
            // Hidden while the styles change, so no taskbar button is left behind.
            ShowWindow(Hwnd, SW_HIDE);
            SetWindowLongPtrW(Hwnd, GWL_STYLE, new IntPtr(WS_POPUP | WS_VISIBLE));
            long ex = GetWindowLongPtrW(Hwnd, GWL_EXSTYLE).ToInt64();
            ex = (ex & ~WS_EX_APPWINDOW) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_LAYERED;
            SetWindowLongPtrW(Hwnd, GWL_EXSTYLE, (IntPtr)ex);
            SetLayeredWindowAttributes(Hwnd, (uint)(Key.R | Key.G << 8 | Key.B << 16), 0, LWA_COLORKEY);
            TakeMouse(false);
            SetWindowPos(Hwnd, HWND_TOPMOST, Area.X, Area.Y, Area.Width, Area.Height,
                SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        }
        // SDL puts its windows back in the normal layer when it shows them unless it is told the
        // window stays on top; told, it keeps it there.
        try { SDL_SetWindowAlwaysOnTop(Window.Handle, true); } catch { }
        base.LoadContent();
    }

    /// <summary>Cover a different part of the screen (the work area changed, say).</summary>
    protected void MoveTo(Rectangle area)
    {
        if (area == Area || area.Width <= 0 || area.Height <= 0) return;
        bool resize = area.Width != Area.Width || area.Height != Area.Height;
        Area = area;
        if (resize)
        {
            Graphics.PreferredBackBufferWidth = area.Width;
            Graphics.PreferredBackBufferHeight = area.Height;
            Graphics.ApplyChanges();
        }
        if (Hwnd != IntPtr.Zero)
            SetWindowPos(Hwnd, HWND_TOPMOST, area.X, area.Y, area.Width, area.Height, SWP_NOACTIVATE);
    }

    protected override void Update(GameTime gameTime)
    {
        MainThread.Drain();
        // Never active, so the mouse is taken whenever it is over us (over a drawn pixel); keys never.
        Input.Update(gameTime, windowActive: true, WindowChrome.PointerIsOurs(Window.Handle));
        UpdateScene(gameTime);
        // Take the mouse only over something solid, or for the rest of a drag that started on us;
        // everywhere else clicks fall through to whatever is underneath.
        if (Hwnd != IntPtr.Zero && GetCursorPos(out var cur))
            TakeMouse(Solid(new Point(cur.X - Area.X, cur.Y - Area.Y)) || (_takesMouse && Input.LeftDown));
        // A window that opens can push itself above always-on-top windows for a moment; take the top
        // back the frame it happens, not up to a second later. Menus and tooltips keep their place.
        if (Hwnd != IntPtr.Zero && CoveredBy() is { } over)
        {
            ZLog?.Invoke(over);
            SetWindowPos(Hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE);
        }
        if ((_topmostMs += gameTime.ElapsedGameTime.TotalMilliseconds) > 1000)
        {
            _topmostMs = 0;
            try { SDL_SetWindowAlwaysOnTop(Window.Handle, true); } catch { }
        }
        base.Update(gameTime);
    }

    /// <summary>CREW_OVERLAY_ZLOG=FILE notes each window found above the overlay, for tracking down
    /// what covers it.</summary>
    private static readonly Action<string>? ZLog = Environment.GetEnvironmentVariable("CREW_OVERLAY_ZLOG") is { Length: > 0 } zf
        ? s => { try { System.IO.File.AppendAllText(zf, DateTime.Now.ToString("HH:mm:ss.fff ") + s + Environment.NewLine); } catch { } }
        : null;

    /// <summary>A visible window above ours that is not a menu, tooltip or part of the shell's own
    /// popups (the class name, for the log); null when nothing covers us.</summary>
    private string? CoveredBy()
    {
        uint self = (uint)Environment.ProcessId;
        var cls = new System.Text.StringBuilder(64);
        for (IntPtr h = GetWindow(Hwnd, GW_HWNDPREV); h != IntPtr.Zero; h = GetWindow(h, GW_HWNDPREV))
        {
            if (!IsWindowVisible(h)) continue;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == self) continue;
            if (DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out int cloaked, 4) == 0 && cloaked != 0) continue;
            cls.Clear();
            GetClassNameW(h, cls, cls.Capacity);
            string c = cls.ToString();
            if (c is "#32768" or "tooltips_class32" or "Xaml_WindowedPopupClass" or "Windows.UI.Core.CoreWindow"
                or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland" or "XamlExplorerHostIslandWindow"
                or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") continue;
            return c + " (pid " + pid + ")";
        }
        return null;
    }

    private void TakeMouse(bool take)
    {
        if (take == _takesMouse) return;
        _takesMouse = take;
        long ex = GetWindowLongPtrW(Hwnd, GWL_EXSTYLE).ToInt64();
        ex = take ? ex & ~WS_EX_TRANSPARENT : ex | WS_EX_TRANSPARENT;
        SetWindowLongPtrW(Hwnd, GWL_EXSTYLE, (IntPtr)ex);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Key);
        Batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        try { DrawScene(Batch); }
        finally { Batch.End(); }
        base.Draw(gameTime);
    }

    /// <summary>The desktop work area of the primary screen (the screen minus the taskbar).</summary>
    public static Rectangle WorkArea() => WindowChrome.WorkArea();

    /// <summary>True when the window in front fills its whole monitor (a game, a video): the time
    /// to keep out of the way.</summary>
    public static bool FullscreenInFront()
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == GetShellWindow() || fg == GetDesktopWindow()) return false;
        var cls = new System.Text.StringBuilder(64);
        GetClassNameW(fg, cls, cls.Capacity);
        if (cls.ToString() is "WorkerW" or "Progman" or "Shell_TrayWnd") return false;   // the desktop and taskbar
        if (!GetWindowRect(fg, out var wr)) return false;
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfoW(MonitorFromWindow(fg, 2 /* nearest */), ref mi)) return false;
        return wr.Left <= mi.rcMonitor.Left && wr.Top <= mi.rcMonitor.Top && wr.Right >= mi.rcMonitor.Right && wr.Bottom >= mi.rcMonitor.Bottom;
    }

    protected override void UnloadContent()
    {
        Batch?.Dispose();
        base.UnloadContent();
    }

    private const int GWL_STYLE = -16, GWL_EXSTYLE = -20, SW_HIDE = 0;
    private const long WS_POPUP = 0x80000000L, WS_VISIBLE = 0x10000000L;
    private const long WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x8000000;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40;
    private const uint LWA_COLORKEY = 0x1;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtrW(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtrW(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    private const uint GW_HWNDPREV = 3;
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfoW(IntPtr m, ref MONITORINFO mi);
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("SDL3")] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetWindowAlwaysOnTop(IntPtr window, [MarshalAs(UnmanagedType.I1)] bool onTop);
}
