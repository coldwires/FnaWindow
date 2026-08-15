using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace FnaWindow;

/// <summary>
/// The reusable base window. Subclass it and override <see cref="BuildUi"/> to populate the
/// <see cref="WindowFrame"/> (menu / content / status). It handles the rest:
///  - a borderless OS window (Win32) whose own navy title bar is the caption. It stays a NORMAL
///    framed window to Windows - the frame styles are kept and WM_NCCALCSIZE collapses the
///    non-client area - so snap, Snap Layouts, Win+Arrow, the taskbar and Alt+Space all work,
///  - native drag / resize via WM_NCHITTEST,
///  - a capped, re-entrancy-safe render loop,
///  - the widget tree, input snapshot, and PointClamp renderer.
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

    /// <summary>The bold face used for window chrome (title bar, menu bar, and an app's own child
    /// captions), or null if the atlas is missing - callers then fall back to <see cref="UiBoldFont"/>.
    /// Applied to the frame automatically; an app with its own captions can read it.</summary>
    protected BitmapFont? ChromeFont;

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
        Window.ClientSizeChanged += (_, _) => { _resizeSettle = ResizeSettleFrames; Relayout(); RequestRedraw(); };
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

    /// <summary>
    /// The PNG used as the window icon - what the taskbar and Alt+Tab show. Relative to the app's
    /// output folder; null skips it. The default convention is <c>Content/appicon.png</c>, so an app
    /// gets an icon by dropping the file in, with no code. This is NOT the exe icon the shell shows
    /// in Explorer - that one is baked at build time by the csproj's ApplicationIcon. Point both at
    /// the same artwork.
    /// </summary>
    protected virtual string? WindowIconPath => Path.Combine("Content", "appicon.png");

    /// <summary>
    /// Use the machine's own Windows 3.1 raster fonts instead of the atlases shipped with the
    /// engine. Off by default, so nothing changes for an app that does not ask.
    ///
    /// Worth turning on for two reasons. It is the genuine article - MS Sans Serif, Fixedsys and
    /// Courier are the faces this whole look imitates, they ship with every Windows, and being
    /// bitmaps they render identically on every machine with no hinting or antialiasing in the way.
    /// And nothing is redistributed: those fonts are licensed to the user who already has them,
    /// whereas an atlas baked from them and shipped in a product is a copy of Microsoft's work.
    ///
    /// Any face that is missing leaves the shipped atlas in place, so this can never leave an app
    /// without a font.
    /// </summary>
    /// <summary>Rounded window corners, in pixels; 0 (default) leaves the window rectangular.
    /// Pair with skin frame art whose corners round at the same radius - the OS clips the
    /// corner pixels, so the desktop shows through the arc instead of a square artifact.</summary>
    protected virtual int WindowCornerRadius => 0;

    /// <summary>When true, dragging the caption or a resize edge moves a dithered outline
    /// instead of the live window (the Win 3.1 move/size), and the window follows on release. Off by default. The
    /// native extras that live inside the OS move loop (drag-to-edge snap, Snap Layouts) do not
    /// apply to an outline drag; Win+Arrow still works. Maximized windows keep the native drag,
    /// so drag-down restore behaves as always.</summary>
    protected virtual bool OutlineWindowDrag => false;

    protected virtual bool UseSystemFonts => false;

    private void ApplySystemFonts()
    {
        if (!UseSystemFonts || !BitmapFont.Supported) return;

        // Editor: raster Courier is 8px wide at its 13px size, which is the only size that fits the
        // 8x15 editor cell - the 16px one is 9 wide and would break the monospace grid. It is two
        // pixels short of the cell, so it is reported at 15 and nudged down one to sit centred.
        Swap("Courier", 13, false, lineHeight: Theme.EditorCellH, yOffset: 1, f => EditorFont = f);

        Swap("MS Sans Serif", 13, false, 0, 0, f => UiFont = f);
        Swap("MS Sans Serif", 13, true,  0, 0, f => UiBoldFont = f);
        // The chrome trick, kept: the caption and menu bar take the same face a size up and bold.
        Swap("MS Sans Serif", 16, true,  0, 0, f => ChromeFont = f);

        void Swap(string family, int px, bool bold, int lineHeight, int yOffset, Action<BitmapFont> assign)
        {
            if (!BitmapFont.HasFamily(family)) return;   // GDI substitutes silently; do not let it
            var f = BitmapFont.FromSystemFont(GraphicsDevice, family, px, bold, lineHeight, yOffset);
            if (f != null) assign(f);
        }
    }

    private void ApplyWindowIcon()
    {
        if (WindowIconPath is not { Length: > 0 } rel) return;
        string path = Path.Combine(AppContext.BaseDirectory, rel);
        if (!File.Exists(path)) return;   // no icon shipped is a fine answer
        try
        {
            using var fs = File.OpenRead(path);
            // The texture is a PNG decoder and nothing more - the pixels are copied straight out and
            // handed to SDL, so it must be disposed rather than left for the finalizer.
            using var tex = Texture2D.FromStream(GraphicsDevice, fs);
            var px = new Color[tex.Width * tex.Height];
            tex.GetData(px);
            WindowChrome.SetWindowIcon(Window.Handle, px, tex.Width, tex.Height);
        }
        catch { /* a bad PNG just leaves the default icon */ }
    }

    /// <summary>Give the frame's caption and menu bar the chrome font. Called after BuildFrame, so
    /// an app that builds its own captions can also read <see cref="ChromeFont"/> directly.</summary>
    private void ApplyChromeFont()
    {
        if (ChromeFont == null) return;
        Caption.Font = ChromeFont;
        if (Frame is WindowFrame wf && wf.Menu != null) wf.Menu.BarFont = ChromeFont;
    }

    // The Win 3.1 pointer set: each PNG under Content/cursors carries the hotspot from its original
    // .cur. Widgets ask for one by key (a text area -> "ibeam", a window edge -> "size*"); absent
    // art simply leaves the OS cursor in place, since CursorDefault stays null.
    private void LoadWin31Cursors()
    {
        (string key, int hx, int hy)[] set =
        {
            ("arrow", 0, 0), ("ibeam", 15, 16), ("cross", 15, 16), ("wait", 16, 16),
            ("sizewe", 17, 8), ("sizens", 14, 10), ("sizenwse", 7, 6), ("sizenesw", 24, 6),
        };
        string dir = Path.Combine(AppContext.BaseDirectory, "Content", "cursors");
        bool any = false;
        foreach (var (key, hx, hy) in set)
        {
            string path = Path.Combine(dir, key + ".png");
            if (!File.Exists(path)) continue;
            try
            {
                using var fs = File.OpenRead(path);
                // Same as the window icon: decode, copy the pixels to SDL, drop the texture. Without
                // the using, all eight cursors leak a Texture2D and FNA reports each one at exit.
                using var tex = Texture2D.FromStream(GraphicsDevice, fs);
                var px = new Color[tex.Width * tex.Height];
                tex.GetData(px);
                Cursors.Define(key, px, tex.Width, tex.Height, hx, hy);
                any = true;
            }
            catch { /* skip a bad cursor; the OS one still applies */ }
        }

        // "hand" comes from the OS rather than from art. 3.1 had no hand pointer, so there is no
        // period-correct one to draw, and a link is a modern affordance anyway - the cursor people
        // already recognise for it is their own system's.
        Cursors.DefineSystem("hand", Cursors.SystemPointer);

        if (any) CursorDefault = "arrow";
    }

    /// <summary>The default cursor key (see <see cref="Cursors"/>), used wherever no widget asks for a
    /// specific one. Null (default) means the app registered no cursors, so the OS cursor is left alone.</summary>
    protected string? CursorDefault;

    // Each frame, pick the cursor: a mid-drag widget that captured it wins; else arrow over an open
    // popup; else the topmost hit widget's CursorKey (walking up to a parent that has an opinion);
    // else the default. No-op until an app sets CursorDefault and registers cursors.
    private void ResolveCursor()
    {
        if (CursorDefault == null || !Cursors.Any) return;
        string key = CursorDefault;
        if (Root.CursorCapture is { } cap)
            key = cap.CursorKey(Input.Mouse) ?? key;
        else if (!Root.Popup.IsOpen)
        {
            // A resize edge/corner is the strongest affordance, so its size cursor wins over any
            // widget's: hovering the border hints the drag WindowHitTest already allows. Maximized
            // returns no edge codes, so no size cursor shows there (nothing to resize).
            if (EdgeCursor(WindowHitTest(Input.Mouse.X, Input.Mouse.Y)) is { } edge) key = edge;
            else
                for (var w = Root.HitTest(Input.Mouse); w != null; w = w.Parent)
                    if (w.CursorKey(Input.Mouse) is { } k) { key = k; break; }
        }
        Cursors.Set(key);
    }

    protected override void LoadContent()
    {
        Batch = new SpriteBatch(GraphicsDevice);
        TextInputEXT.StartTextInput(); // SDL3 needs this or no typed chars arrive

        string fonts = Path.Combine(AppContext.BaseDirectory, "Content", "fonts");
        EditorFont = BitmapFont.Load(GraphicsDevice, Path.Combine(fonts, "fixedsys_12"));
        UiFont = BitmapFont.Load(GraphicsDevice, Path.Combine(fonts, "sserife_11"));
        UiBoldFont = BitmapFont.Load(GraphicsDevice, Path.Combine(fonts, "sserife_11_bold"));
        // Window chrome (title bar, menu bar, child captions) uses a larger bold face: in 3.1 the
        // caption and the menus are the bold System font, not the same size as body text.
        try { ChromeFont = BitmapFont.Load(GraphicsDevice, Path.Combine(fonts, "sserife_13_bold")); }
        catch { ChromeFont = null; }

        // Then, if the app asked for it, replace those with the machine's own Windows 3.1 faces.
        // Anything missing simply leaves the atlas above in place.
        ApplySystemFonts();
        Renderer = new Win31Renderer(GraphicsDevice, Batch, UiFont, UiBoldFont, EditorFont);

        // The default look: authored Win 3.1 art for the chrome, and the 3.1 cursor set. Both fall
        // back cleanly - a missing PNG drops to the exact procedural drawing, and missing cursor art
        // leaves the OS pointer alone. An app that wants the procedural look calls
        // ThemeManager.ApplySkin(new Win31Skin()) after base.LoadContent().
        Win31Png.LoadAssets(GraphicsDevice);
        Win31Png.Apply();
        LoadWin31Cursors();

        // Optional software pointer. Default is null (native OS cursor); an app opts in by
        // overriding BuildCursor. The Cursor setter hides the OS cursor when non-null.
        Cursor = BuildCursor();
        ApplyWindowIcon();

        Root = new RootDesktop();
        (Frame, Caption) = BuildFrame(UiFont);
        ApplyChromeFont();
        Caption.OnClose = Exit;
        Caption.OnMinimize = () => { if (_borderless) WindowChrome.Minimize(_sdlWindow); };
        Caption.OnMaximize = ToggleMaximize;
        Root.Add(Frame);
        Relayout();

        // A skin change can swap the font (and thus measured widths), so re-lay-out the whole tree
        // when it happens - otherwise font-dependent layout (e.g. menu-bar title widths) goes stale.
        ThemeManager.Changed += () => { Relayout(); RequestRedraw(); };

        // Strip the OS frame so only our chrome shows; drag/resize handled natively via WM_NCHITTEST.
        if (WindowChrome.Supported)
        {
            _sdlWindow = Window.Handle;
            _hwnd = WindowChrome.GetHwnd(_sdlWindow);
            if (_hwnd != IntPtr.Zero)
            {
                // Subclass FIRST. MakeBorderless changes the styles and asks for a frame change,
                // which makes Windows send WM_NCCALCSIZE straight away - and that message is where
                // the frame is collapsed. Installed the other way round, the original wndproc
                // answers it and the client area keeps a full frame's worth of inset for good,
                // since nothing sends another one until the window is next resized.
                // In outline mode the caption and edges must NOT reach Windows as caption/edge
                // codes (that starts the native live move/size loop); UpdateOutlineDrag runs
                // those drags instead. WindowHitTest itself stays a pure classifier, because
                // the resize cursors and the drag state machine both read it. Maximized keeps
                // the native caption so drag-down restore still works.
                WindowChrome.InstallHitTest(_hwnd, (cx, cy) =>
                {
                    int code = WindowHitTest(cx, cy);
                    return OutlineWindowDrag && !_maximized && code != WindowChrome.HTCLIENT
                        ? WindowChrome.HTCLIENT : code;
                });
                WindowChrome.MakeBorderless(_hwnd, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight);
                _borderless = true;
            }
        }
        base.LoadContent();
    }

    /// <summary>
    /// Keeps the backbuffer exactly the size of the client area, then re-lays-out.
    ///
    /// Without this the two drift apart on any resize the app did not perform itself - a snap,
    /// Win+Arrow, maximize, or a drag - because nothing else updates the preferred backbuffer size.
    /// FNA then scales its old surface to fit the new client, and everything goes soft: at 800x600
    /// in a maximized 1920x1032 window, a quarter of the menu-bar pixels turn into grey fringes.
    /// For a look built on 1-bit glyphs and single-pixel bevels that is the difference between
    /// crisp and wrong.
    ///
    /// This replaces the old per-path fix. Maximize used to set the backbuffer itself, which
    /// covered the app's own maximize button and nothing else; hanging it off ClientSizeChanged
    /// covers every way a window can change size, including the ones Windows performs for us.
    /// </summary>
    // Frames of quiet required after the last size change before the surface is resynced. A drag
    // raises ClientSizeChanged every frame, so the counter keeps resetting and this only fires once
    // the user lets go.
    private const int ResizeSettleFrames = 8;
    private int _resizeSettle;

    /// <summary>
    /// Re-points the render surface at the current client size, ONCE a resize has finished.
    ///
    /// Never call this mid-resize. <c>ApplyChanges</c> does not only resize the backbuffer, it also
    /// resizes the WINDOW to match - so running it while the user is dragging an edge fights the
    /// drag and the window will not resize properly. That is why it is debounced rather than
    /// checked every frame.
    ///
    /// It is needed at all because nothing else updates the preferred backbuffer after startup:
    /// without it, a resized window keeps rendering at the old size and FNA scales the result, which
    /// turns every 1-bit glyph and single-pixel bevel soft.
    /// </summary>
    private void SyncBackBuffer()
    {
        if (_syncingBackBuffer) return;   // ApplyChanges raises ClientSizeChanged again

        int w = Window.ClientBounds.Width, h = Window.ClientBounds.Height;
        if (w <= 0 || h <= 0) return;     // minimized: nothing to size to

        // Repaint unconditionally. FNA sometimes resizes the backbuffer itself (it does on a grow),
        // and then the check below finds nothing to do - but the last frame DRAWN is still the
        // pre-resize one, and the idle throttle is happy to leave it there, stretched. The redraw
        // is the point as much as the resize is.
        Relayout();
        RequestRedraw();

        // Compare against the ACTUAL render surface, not the preferred size, which can already
        // claim a size the device never took.
        var vp = GraphicsDevice.Viewport;
        if (vp.Width == w && vp.Height == h) return;

        _syncingBackBuffer = true;
        try
        {
            Graphics.PreferredBackBufferWidth = w;
            Graphics.PreferredBackBufferHeight = h;
            Graphics.ApplyChanges();
        }
        catch { /* a transient device state during a resize is not worth crashing over */ }
        finally { _syncingBackBuffer = false; }

        Relayout();
        RequestRedraw();   // the idle throttle would otherwise leave the pre-resize frame on screen
    }

    private bool _syncingBackBuffer;

    private void Relayout()
    {
        if (WindowCornerRadius > 0 && _hwnd != IntPtr.Zero)
        {
            // Maximized windows go square (as Windows itself does) - and the region must never
            // be smaller than the true maximized bounds, or an edge sliver gets clipped.
            WindowShape.Apply(_hwnd, Window.ClientBounds.Width, Window.ClientBounds.Height,
                _maximized ? 0 : WindowCornerRadius);
        }

        if (Root == null) return;
        var vp = GraphicsDevice.Viewport;
        Root.Bounds = new Rectangle(0, 0, vp.Width, vp.Height);
        Frame.Bounds = Root.Bounds;
        Root.Layout();
    }

    protected override void Update(GameTime gameTime)
    {
        if (MainThread.Drain()) RequestRedraw();   // apply background-thread work on the UI thread
        UpdateOutlineDrag();                       // the 3.1-style caption/edge drag, when opted in
        Input.Update(gameTime, IsActive && !_odDragging); // unfocused or outline-dragging -> widgets see no input
        Root.Update(Input, gameTime);              // widgets may call Root.RequestRedraw()
        ResolveCursor();
        if (_borderless) WindowChrome.EnsureBorderless(_hwnd);
        // Resync the surface once the resize has SETTLED, never during it - see SyncBackBuffer.
        if (_resizeSettle > 0 && --_resizeSettle == 0) SyncBackBuffer();
        SyncMaximized();                          // snap/Win+Up change it behind our back

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
        // Windows sends WM_PAINT synchronously during move/resize -> a re-entrant Draw. Guard it,
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

    // -- Native window management ------------------------------------------
    private void ToggleMaximize()
    {
        if (!_borderless) { Graphics.ToggleFullScreen(); Relayout(); return; }

        // Through the OS, not by moving ourselves to the work area. The window is a normal framed
        // window now, so Windows can maximize it too - snap, Win+Up, the taskbar menu - and an
        // app-private "am I maximized" flag would immediately disagree. The OS owns the answer.
        if (WindowChrome.IsMaximized(_hwnd)) WindowChrome.Restore(_hwnd);
        else WindowChrome.Maximize(_hwnd);
        // No Relayout here: the resize raises ClientSizeChanged, which does it. SyncMaximized picks
        // the caption glyph up on the next frame.
    }

    /// <summary>Keeps the caption's restore/maximize glyph honest when something other than our own
    /// button changed the state - a snap, Win+Up, or a double-click on the caption.</summary>
    private void SyncMaximized()
    {
        if (!_borderless) return;
        bool now = WindowChrome.IsMaximized(_hwnd);
        if (now == _maximized) return;
        _maximized = now;
        Caption.Maximized = now;
        RequestRedraw();
    }

    /// <summary>Maps a client point to a non-client hit code so Windows drags/resizes natively.</summary>
    private int WindowHitTest(int cx, int cy)
    {
        // Maximized has no resize edges (Windows overhangs the frame off every side), but the caption
        // must still report HTCAPTION so a drag-down or double-click restores the window natively,
        // mirroring the maximize gestures that work while restored.
        if (!_maximized)
        {
            // The border band tracks the skin's frame thickness; corners reach C px along each
            // edge (as Windows itself does), because a band-by-band corner is a 4x4 target
            // nobody can hit.
            int E = ThemeManager.Skin.WindowFrameThickness;
            const int C = 20;
            var vp = GraphicsDevice.Viewport;
            int w = vp.Width, h = vp.Height;
            bool l = cx < E, r = cx >= w - E, tp = cy < E, bt = cy >= h - E;
            bool lc = cx < C, rc = cx >= w - C, tc = cy < C, bc = cy >= h - C;
            if (tp && lc || l && tc) return WindowChrome.HTTOPLEFT;
            if (tp && rc || r && tc) return WindowChrome.HTTOPRIGHT;
            if (bt && lc || l && bc) return WindowChrome.HTBOTTOMLEFT;
            if (bt && rc || r && bc) return WindowChrome.HTBOTTOMRIGHT;
            if (l) return WindowChrome.HTLEFT;
            if (r) return WindowChrome.HTRIGHT;
            if (tp) return WindowChrome.HTTOP;
            if (bt) return WindowChrome.HTBOTTOM;
        }
        if (Caption.IsOnDragArea(new Point(cx, cy))) return WindowChrome.HTCAPTION;
        return WindowChrome.HTCLIENT;
    }

    // ---- outline drag (see OutlineWindowDrag) ------------------------------------------------
    // Polled off the OS cursor, not InputState: mid-drag the cursor leaves the client area (the
    // window is not moving with it), where in-window mouse state cannot follow.
    private bool _odWasDown, _odDragging, _odShown;
    private int _odCode;                    // HTCAPTION for a move, an edge code for a resize
    private Point _odCursorStart, _odLastPress;
    private Rectangle _odWindowStart;
    private long _odLastPressAt;

    private void UpdateOutlineDrag()
    {
        if (!OutlineWindowDrag || _hwnd == IntPtr.Zero) return;
        bool down = DragOutline.LeftButtonDown();

        if (!_odDragging)
        {
            if (down && !_odWasDown && IsActive && !_maximized)
            {
                var s = DragOutline.CursorScreen();
                var c = DragOutline.ToClient(_hwnd, s);
                var vp = GraphicsDevice.Viewport;
                int code = c.X >= 0 && c.Y >= 0 && c.X < vp.Width && c.Y < vp.Height
                    ? WindowHitTest(c.X, c.Y) : WindowChrome.HTCLIENT;
                if (code == WindowChrome.HTCAPTION)
                {
                    // Double-click on the caption maximizes, which HTCAPTION used to provide.
                    long now = Environment.TickCount64;
                    bool dbl = now - _odLastPressAt <= DragOutline.DoubleClickMs()
                        && Math.Abs(s.X - _odLastPress.X) <= 4 && Math.Abs(s.Y - _odLastPress.Y) <= 4;
                    _odLastPressAt = now; _odLastPress = s;
                    if (dbl) { WindowChrome.Maximize(_hwnd); code = WindowChrome.HTCLIENT; }
                }
                if (code != WindowChrome.HTCLIENT)
                {
                    _odDragging = true; _odShown = false;
                    _odCode = code;
                    _odCursorStart = s;
                    _odWindowStart = DragOutline.WindowBounds(_hwnd);
                }
            }
        }
        else if (down)
        {
            var s = DragOutline.CursorScreen();
            if (_odShown)
                DragOutline.Show(OdProposed(s));
            else if (Math.Abs(s.X - _odCursorStart.X) > 2 || Math.Abs(s.Y - _odCursorStart.Y) > 2)
            {
                DragOutline.Show(OdProposed(s)); // a sloppy click is not a drag
                _odShown = true;
            }
        }
        else
        {
            _odDragging = false;
            if (_odShown)
            {
                _odShown = false;
                DragOutline.Hide();
                var b = OdProposed(DragOutline.CursorScreen());
                if (b != _odWindowStart) DragOutline.SetWindowBounds(_hwnd, b);
            }
        }
        _odWasDown = down;
    }

    // The bounds the outline proposes for the cursor at s: the whole frame shifted for a move,
    // or the grabbed edges pulled (and clamped to a sane minimum) for a resize.
    private Rectangle OdProposed(Point s)
    {
        int dx = s.X - _odCursorStart.X, dy = s.Y - _odCursorStart.Y;
        var w = _odWindowStart;
        if (_odCode == WindowChrome.HTCAPTION)
            return new Rectangle(w.X + dx, w.Y + dy, w.Width, w.Height);

        const int MinW = 320, MinH = 200;
        int l = w.X, tp = w.Y, rt = w.Right, bt = w.Bottom;
        bool left = _odCode is WindowChrome.HTLEFT or WindowChrome.HTTOPLEFT or WindowChrome.HTBOTTOMLEFT;
        bool right = _odCode is WindowChrome.HTRIGHT or WindowChrome.HTTOPRIGHT or WindowChrome.HTBOTTOMRIGHT;
        bool top = _odCode is WindowChrome.HTTOP or WindowChrome.HTTOPLEFT or WindowChrome.HTTOPRIGHT;
        bool bottom = _odCode is WindowChrome.HTBOTTOM or WindowChrome.HTBOTTOMLEFT or WindowChrome.HTBOTTOMRIGHT;
        if (left) l = Math.Min(l + dx, rt - MinW);
        if (right) rt = Math.Max(rt + dx, l + MinW);
        if (top) tp = Math.Min(tp + dy, bt - MinH);
        if (bottom) bt = Math.Max(bt + dy, tp + MinH);
        return new Rectangle(l, tp, rt - l, bt - tp);
    }

    // The resize cursor key for a hit-test code, or null for a non-edge (caption/client) code.
    private static string? EdgeCursor(int ht) => ht switch
    {
        WindowChrome.HTLEFT or WindowChrome.HTRIGHT => "sizewe",
        WindowChrome.HTTOP or WindowChrome.HTBOTTOM => "sizens",
        WindowChrome.HTTOPLEFT or WindowChrome.HTBOTTOMRIGHT => "sizenwse",
        WindowChrome.HTTOPRIGHT or WindowChrome.HTBOTTOMLEFT => "sizenesw",
        _ => null,
    };
}
