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
    protected WindowFrame Frame = null!;
    protected BitmapFont UiFont = null!, UiBoldFont = null!, EditorFont = null!;

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

    protected override void LoadContent()
    {
        Batch = new SpriteBatch(GraphicsDevice);
        TextInputEXT.StartTextInput(); // SDL3 needs this or no typed chars arrive

        string fonts = Path.Combine(AppContext.BaseDirectory, "Content", "fonts");
        EditorFont = BitmapFont.Load(GraphicsDevice, Path.Combine(fonts, "fixedsys_12"));
        UiFont = BitmapFont.Load(GraphicsDevice, Path.Combine(fonts, "sserife_11"));
        UiBoldFont = BitmapFont.Load(GraphicsDevice, Path.Combine(fonts, "sserife_11_bold"));
        Renderer = new Win31Renderer(GraphicsDevice, Batch, UiFont, UiBoldFont, EditorFont);

        Root = new RootDesktop();
        Frame = new WindowFrame();
        Frame.Title.Title = _titleText;
        Frame.Title.OnClose = Exit;
        Frame.Title.OnMinimize = () => { if (_borderless) WindowChrome.Minimize(_sdlWindow); };
        Frame.Title.OnMaximize = ToggleMaximize;
        Root.Add(Frame);

        BuildUi(Frame, UiFont);
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
        Input.Update(gameTime);
        Root.Update(Input, gameTime);              // widgets may call Root.RequestRedraw()
        if (_borderless) WindowChrome.EnsureBorderless(_hwnd);

        // Stay at full fps while there's activity or a redraw was requested; otherwise idle.
        if (Input.AnyActivity || Root.RedrawRequested) { _awakeMs = KeepAwakeMs; Root.ClearRedraw(); }
        else _awakeMs -= gameTime.ElapsedGameTime.TotalMilliseconds;
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
            GraphicsDevice.Clear(Theme.Face);
            Batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            try { Root.Draw(Renderer); }
            finally { Batch.End(); }
            base.Draw(gameTime);

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
        if (Frame.Title.IsOnDragArea(new Point(cx, cy))) return WindowChrome.HTCAPTION;
        return WindowChrome.HTCLIENT;
    }
}
