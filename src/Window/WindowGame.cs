using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace FnaWindow;

/// <summary>
/// The reusable base window. Subclass it and override <see cref="BuildUi"/> to populate the
/// <see cref="WindowFrame"/> (menu / content / status). It handles the rest:
///  • a borderless OS window (Win32) whose own navy title bar is the caption,
///  • native drag / resize / Aero-snap via WM_NCHITTEST,
///  • a capped, re-entrancy-safe render loop,
///  • the widget tree, input snapshot, and PointClamp renderer.
/// On non-Windows platforms it falls back to the normal OS frame.
/// </summary>
public class WindowGame : Game
{
    protected readonly GraphicsDeviceManager Graphics;
    protected SpriteBatch Batch = null!;
    protected Win31Renderer Renderer = null!;
    protected readonly InputState Input = new();
    protected RootDesktop Root = null!;
    protected Widget Frame = null!;          // the root frame widget (a WindowFrame by default)
    protected TitleBar Caption = null!;      // its title bar: drives close/min/max + the drag hit-test
    protected BitmapFont UiFont = null!, UiBoldFont = null!, EditorFont = null!;

    private MouseCursor? _cursor;
    /// <summary>Software mouse pointer, or null (default) to use the native OS cursor. Assigning a
    /// non-null cursor hides the OS cursor and draws this sprite at the mouse each frame; set it
    /// back to null to restore the native cursor. Apps opt in via <see cref="BuildCursor"/>.</summary>
    protected MouseCursor? Cursor
    {
        get => _cursor;
        set { _cursor = value; IsMouseVisible = value == null; }
    }

    private readonly string _titleText;
    private IntPtr _sdlWindow, _hwnd;
    private bool _borderless, _maximized, _inDraw;
    private Rectangle _restore;

    // Optional: set env var FNAWINDOW_SHOT=<path> to save a PNG of the window and exit (docs/CI).
    private readonly string? _shotPath = Environment.GetEnvironmentVariable("FNAWINDOW_SHOT");
    private int _frames;

    // Idle-redraw throttle: keep drawing for this long after the last activity, then stop until
    // something happens again. Saves CPU/GPU when the window just sits there.
    private const double KeepAwakeMs = 350;
    private double _awakeMs = KeepAwakeMs;

    /// <summary>Keep the render loop awake. Call when your content changes or while animating /
    /// streaming (e.g. per token) so the throttle doesn't sleep through it.</summary>
    public void RequestRedraw() => _awakeMs = KeepAwakeMs;

    // Interactive screenshot: grab a clean frame, then a white flash + shutter sound.
    private ShutterSound? _shutter;
    private string? _captureTo;
    private int _captureDelay;
    private double _flashMs;
    private const double FlashMs = 220;
    private const float FlashMaxAlpha = 0.85f;

    /// <summary>
    /// Save a PNG of the current window, then play the screenshot flash and shutter the way a
    /// phone does. Pass a path, or leave it null to write a timestamped file into a "Screenshots"
    /// folder next to the executable. Returns the path it will write. Safe to call from a menu item.
    /// </summary>
    public string CaptureScreenshot(string? path = null)
    {
        path ??= DefaultShotPath();
        _captureTo = path;
        _captureDelay = 2;      // let a closing menu or popup clear before the grab
        RequestRedraw();
        return path;
    }

    private static string DefaultShotPath()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "Screenshots");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "shot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
    }

    public WindowGame(string title = "FNA Custom Window", int width = 1024, int height = 768)
    {
        _titleText = title;
        Graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = width,
            PreferredBackBufferHeight = height,
            SynchronizeWithVerticalRetrace = true, // cap to refresh rate
        };
        IsFixedTimeStep = true;                    // ~60fps update; no runaway CPU
        Window.AllowUserResizing = true;
        Window.Title = title;
        Window.ClientSizeChanged += (_, _) => Relayout();
        IsMouseVisible = true;
        Content.RootDirectory = "Content";
    }

    /// <summary>Populate the frame: set <c>frame.Title.Title</c>, and SetMenu / SetContent / SetStatus.</summary>
    protected virtual void BuildUi(WindowFrame frame, BitmapFont uiFont) { }

    /// <summary>
    /// Build the root frame and return it with the title bar that drives close/minimize/maximize
    /// and the drag hit-test. The default builds a <see cref="WindowFrame"/> and calls
    /// <see cref="BuildUi"/>; override to supply a custom frame (any Widget with its own TitleBar).
    /// </summary>
    protected virtual (Widget frame, TitleBar caption) BuildFrame(BitmapFont uiFont)
    {
        var wf = new WindowFrame();
        wf.Title.Title = _titleText;
        BuildUi(wf, uiFont);
        return (wf, wf.Title);
    }

    /// <summary>
    /// Supply a software mouse pointer, or null (default) to keep the native OS cursor. Override
    /// and return a <see cref="MouseCursor"/> loaded from your app's own Content to draw a custom
    /// pointer. Called once during load. Note: a software pointer replaces the native resize/move
    /// cursors on the window's edges (resizing still works; the shape hint just doesn't change).
    /// </summary>
    protected virtual MouseCursor? BuildCursor() => null;

    protected override void LoadContent()
    {
        Batch = new SpriteBatch(GraphicsDevice);
        TextInputEXT.StartTextInput(); // SDL3 needs this or no typed chars arrive

        string fonts = Path.Combine(AppContext.BaseDirectory, "Content", "fonts");
        EditorFont = BitmapFont.Load(GraphicsDevice, Path.Combine(fonts, "fixedsys_12"));
        UiFont = BitmapFont.Load(GraphicsDevice, Path.Combine(fonts, "sserife_11"));
        UiBoldFont = BitmapFont.Load(GraphicsDevice, Path.Combine(fonts, "sserife_11_bold"));
        Renderer = new Win31Renderer(GraphicsDevice, Batch, UiFont, UiBoldFont, EditorFont);

        // Optional software pointer. Default is null (native OS cursor); an app opts in by
        // overriding BuildCursor. The Cursor setter hides the OS cursor when non-null.
        Cursor = BuildCursor();

        Root = new RootDesktop();
        (Frame, Caption) = BuildFrame(UiFont);
        Caption.OnClose = Exit;
        Caption.OnMinimize = () => { if (_borderless) WindowChrome.Minimize(_sdlWindow); };
        Caption.OnMaximize = ToggleMaximize;
        Root.Add(Frame);
        Relayout();

        // Strip the OS frame so only our chrome shows; drag/resize handled natively via WM_NCHITTEST.
        if (WindowChrome.Supported)
        {
            _sdlWindow = Window.Handle;
            _hwnd = WindowChrome.GetHwnd(_sdlWindow);
            if (_hwnd != IntPtr.Zero)
            {
                WindowChrome.MakeBorderless(_hwnd, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight);
                WindowChrome.InstallHitTest(_hwnd, WindowHitTest);
                _borderless = true;
            }
        }
        base.LoadContent();
    }

    private void Relayout()
    {
        if (Root == null) return;
        var vp = GraphicsDevice.Viewport;
        Root.Bounds = new Rectangle(0, 0, vp.Width, vp.Height);
        Frame.Bounds = Root.Bounds;
        Root.Layout();
    }

    protected override void Update(GameTime gameTime)
    {
        if (MainThread.Drain()) RequestRedraw();   // apply background-thread work on the UI thread
        Input.Update(gameTime);
        Root.Update(Input, gameTime);              // widgets may call Root.RequestRedraw()
        if (_borderless) WindowChrome.EnsureBorderless(_hwnd);

        double dt = gameTime.ElapsedGameTime.TotalMilliseconds;
        if (_flashMs > 0) _flashMs = Math.Max(0, _flashMs - dt);
        bool capturing = _captureTo != null || _flashMs > 0;

        // Stay at full fps while there's activity, a redraw was requested, or a capture/flash is
        // in flight; otherwise idle.
        if (Input.AnyActivity || Root.RedrawRequested || capturing) { _awakeMs = KeepAwakeMs; Root.ClearRedraw(); }
        else _awakeMs -= dt;
        if (_awakeMs <= 0 && _shotPath == null) SuppressDraw(); // skip this frame's Draw when idle

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        // Windows sends WM_PAINT synchronously during move/resize → a re-entrant Draw. Guard it,
        // and always balance Begin/End even if a widget throws.
        if (_inDraw) return;
        _inDraw = true;
        try
        {
            var vp = GraphicsDevice.Viewport;
            GraphicsDevice.Clear(Theme.Face);
            Batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            try
            {
                Root.Draw(Renderer);
                if (_flashMs > 0)
                    Renderer.Fill(new Rectangle(0, 0, vp.Width, vp.Height),
                        Color.White * (FlashMaxAlpha * (float)(_flashMs / FlashMs)));
                Cursor?.Draw(Batch, Input.Mouse);   // software pointer sits on top of everything
            }
            finally { Batch.End(); }
            base.Draw(gameTime);

            // Interactive capture: wait a couple of frames so a closing menu is gone, then grab a
            // clean frame (the flash is not part of it) and kick off the flash + shutter.
            if (_captureTo != null && --_captureDelay < 0)
            {
                SaveShot(_captureTo);
                _captureTo = null;
                _flashMs = FlashMs;
                (_shutter ??= new ShutterSound()).Play();
            }

            if (_shotPath != null && ++_frames >= 10) { SaveShot(_shotPath); Exit(); }
        }
        finally { _inDraw = false; }
    }

    private void SaveShot(string path)
    {
        var vp = GraphicsDevice.Viewport;
        using var rt = new RenderTarget2D(GraphicsDevice, vp.Width, vp.Height);
        GraphicsDevice.SetRenderTarget(rt);
        GraphicsDevice.Clear(Theme.Face);
        Batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
            SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        try { Root.Draw(Renderer); }
        finally { Batch.End(); }
        GraphicsDevice.SetRenderTarget(null);
        using var fs = File.Create(path);
        rt.SaveAsPng(fs, vp.Width, vp.Height);
    }

    // ── Native window management ──────────────────────────────────────────
    private void ToggleMaximize()
    {
        if (!_borderless) { Graphics.ToggleFullScreen(); Relayout(); return; }
        if (!_maximized)
        {
            WindowChrome.GetWindowPosition(_sdlWindow, out int wx, out int wy);
            WindowChrome.GetWindowSize(_sdlWindow, out int ww, out int wh);
            _restore = new Rectangle(wx, wy, ww, wh);
            var wa = WindowChrome.WorkArea();
            ApplyWindow(wa.X, wa.Y, wa.Width, wa.Height, move: true);
            _maximized = true;
        }
        else
        {
            ApplyWindow(_restore.X, _restore.Y, _restore.Width, _restore.Height, move: true);
            _maximized = false;
        }
    }

    private void ApplyWindow(int x, int y, int w, int h, bool move)
    {
        Graphics.PreferredBackBufferWidth = w;
        Graphics.PreferredBackBufferHeight = h;
        Graphics.ApplyChanges();
        WindowChrome.SetWindowSize(_sdlWindow, w, h);
        if (move) WindowChrome.SetWindowPosition(_sdlWindow, x, y);
        Relayout();
    }

    /// <summary>Maps a client point to a non-client hit code so Windows drags/resizes natively.</summary>
    private int WindowHitTest(int cx, int cy)
    {
        if (_maximized) return WindowChrome.HTCLIENT;
        const int E = 4; // resize border
        var vp = GraphicsDevice.Viewport;
        int w = vp.Width, h = vp.Height;
        bool l = cx < E, r = cx >= w - E, tp = cy < E, bt = cy >= h - E;
        if (tp && l) return WindowChrome.HTTOPLEFT;
        if (tp && r) return WindowChrome.HTTOPRIGHT;
        if (bt && l) return WindowChrome.HTBOTTOMLEFT;
        if (bt && r) return WindowChrome.HTBOTTOMRIGHT;
        if (l) return WindowChrome.HTLEFT;
        if (r) return WindowChrome.HTRIGHT;
        if (tp) return WindowChrome.HTTOP;
        if (bt) return WindowChrome.HTBOTTOM;
        if (Caption.IsOnDragArea(new Point(cx, cy))) return WindowChrome.HTCAPTION;
        return WindowChrome.HTCLIENT;
    }
}
