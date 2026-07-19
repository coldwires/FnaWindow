# Extending FnaWindow

A practical guide to building on the engine. Everything lives in one namespace, `FnaWindow`.

- [Consuming the engine as a git submodule](#consuming-the-engine-as-a-git-submodule)
- [Architecture](#architecture)
- [The Widget model](#the-widget-model)
- [Building a custom widget](#building-a-custom-widget)
- [The renderer (drawing primitives)](#the-renderer)
- [Menus](#menus)
- [Modal dialogs](#modal-dialogs)
- [Theming](#theming)
- [How the borderless window works](#how-the-borderless-window-works)
- [Fonts](#fonts)
- [Gotchas](#gotchas)

---

## Consuming the engine as a git submodule

The engine ships as a class library, `FnaWindow.csproj`. The runnable `Demo/` project references
it the same way your own app should. The recommended setup for a build-on is a **git submodule**:

```sh
# in your app's repo
git submodule add <engine-repo-url> engine
```

Reference the library project from your app's `.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="engine\FnaWindow.csproj" />
</ItemGroup>
```

That one reference is the whole dependency. It brings in:

- the engine assembly (`FnaWindow.dll`),
- FNA (referenced transitively by the engine),
- the native libs (`SDL3`, `FNA3D`, `FAudio`, `libtheorafile`), and
- the bundled bitmap fonts under `Content/fonts/`.

The native libs and fonts are declared as `Content` in the engine, so MSBuild copies them into
**your** app's output directory automatically. A fresh `dotnet run` produces a runnable window
with the assets already in place. There's a ready-to-copy starter under
[`templates/starter-app/`](../templates/starter-app/).

**Pinning and updating.** A submodule records the exact engine commit your app was built against,
so a base change never moves your app underneath you. Pull base fixes when you choose to:

```sh
cd engine && git pull origin main && cd ..
git add engine            # records the new engine commit in your app
git commit -m "Update engine"
```

Anyone cloning your app gets the pinned engine with `git clone --recurse-submodules`, or
`git submodule update --init` after a plain clone.

**Not using submodules?** A sibling checkout works too. Reference the library by relative path
(`..\fna-custom-window\FnaWindow.csproj`). You lose the per-app commit pin, so the submodule route
is better once you ship more than one app on the engine.

---

## Architecture

```
WindowGame (FNA Game)
 ├─ RootDesktop            ← root of the widget tree; owns the PopupLayer + focus
 │   └─ WindowFrame        ← the window chrome
 │       ├─ TitleBar       ← draggable caption + min/max/close
 │       ├─ MenuBar        ← (optional) top menus → dropdown popups
 │       ├─ <your Content> ← any Widget, fills the middle
 │       └─ StatusBar      ← (optional)
 └─ WindowChrome           ← Win32/SDL interop (borderless + WM_NCHITTEST)
```

The loop is simple and close to immediate mode: every frame `WindowGame` calls `Root.Update(input, time)` then `Root.Draw(renderer)`. There are no dirty rects; the whole tree redraws each frame, which is cheap at this scale and capped to 60 fps.

`WindowGame` owns the FNA plumbing. You almost never touch it. You override **`BuildUi(frame, uiFont)`** and put widgets in the `frame`.

---

## The Widget model

```csharp
public abstract class Widget
{
    public Rectangle Bounds;          // absolute screen pixels, set in Layout
    public Widget? Parent;
    public List<Widget> Children;
    public bool Visible, Enabled;

    public virtual void Layout();                          // position children
    public virtual void Update(InputState input, GameTime t);
    public virtual void Draw(Win31Renderer r);

    public virtual bool WantsKeyboard => false;            // opt into key/char routing when focused
    public virtual void OnKey(InputState e);
    public virtual void OnChar(char c);
}
```

- **`Layout`** sets `Bounds` on children (top-down). Call `base.Layout()` to recurse.
- **`Update`** reads `InputState` (edge-detected mouse/keyboard, `TypedChars`, wheel, double-click).
- **`Draw`** paints with the `Win31Renderer`.
- **`Root()`** walks up to the `RootDesktop`; check `Root()?.Popup.IsOpen` to ignore input while a menu/dialog is up.

---

## Building a custom widget

```csharp
public sealed class Counter : Widget
{
    private int _n;
    private Rectangle _btn;

    public override void Layout()
        => _btn = new Rectangle(Bounds.X + 8, Bounds.Y + 8, 80, 24);

    public override void Update(InputState input, GameTime t)
    {
        if (Root()?.Popup.IsOpen == true) return;         // let menus/dialogs be modal
        if (input.LeftPressed && _btn.Contains(input.Mouse)) _n++;
    }

    public override void Draw(Win31Renderer r)
    {
        r.DrawPanel(Bounds, BevelStyle.SunkenThick, Theme.WindowBg);
        bool down = /* track your own pressed state if you want the offset */ false;
        r.DrawPanel(_btn, down ? BevelStyle.SunkenThin : BevelStyle.RaisedThin, Theme.Face);
        r.DrawText(r.UiFont, $"Count: {_n}", _btn.X + 8, _btn.Y + 6, Theme.Text);
    }
}
```

Add it with `frame.SetContent(new Counter())`, or nest it: `Add(child)` in a container's constructor and set the child's `Bounds` in `Layout`.

---

## The renderer

`Win31Renderer` provides the drawing primitives the whole look is built from:

```csharp
r.Fill(rect, color);                       // solid fill (1×1 white pixel, tinted)
r.FrameRect(rect, color);                  // 1px outline
r.HLine(x, y, len, color); r.VLine(...);
r.DrawBevel(rect, style);                  // border only
r.DrawPanel(rect, style, bgColor);         // bevel + fill
r.DrawText(font, "hi", x, y, color);
r.DrawTextMnemonic(font, "&File", ...);    // underlines the accelerator letter
r.DrawDither(rect);                        // scrollbar-track checker
```

Bevel styles (`BevelStyle`): `RaisedThin` (buttons), `RaisedThick` (windows/panels), `SunkenThin` (status cells), `SunkenThick` (text wells). For a pressed look, swap to sunken and offset content by (+1,+1).

Fonts on the renderer: `r.UiFont`, `r.UiBoldFont`, `r.EditorFont` (monospace). `font.Draw(r.Sb, text, x, y, color)` and `font.MeasureWidth(text)`. **The atlases are ASCII-only.** Non-ASCII glyphs (bullets, arrows, box-drawing, em dashes) render as blanks, so draw shapes procedurally instead, as the title-bar buttons do.

Colors always come from `Theme` (never hardcode) so theming works: `Theme.Face`, `Theme.Text`, `Theme.TitleActive`, `Theme.WindowBg`, `Theme.MidEdge`, `Theme.LightEdge`, `Theme.DarkEdge`, etc.

---

## Menus

```csharp
var file = new List<MenuItemDef>
{
    MenuItemDef.Item("&New",  "Ctrl+N", () => { /* ... */ }),
    MenuItemDef.Item("&Open...", null, () => { /* ... */ }),
    MenuItemDef.Sep(),
    new MenuItemDef { Label = "&Wrap", Checked = true, OnClick = ToggleWrap },
    MenuItemDef.Sep(),
    MenuItemDef.Item("E&xit", null, Exit),
};

frame.SetMenu(new MenuBar(new List<TopMenu> { new("&File", file), new("&Help", help) })
{
    MeasureTitleWidth = uiFont.MeasureWidth,   // crisp widths (optional but recommended)
    MeasureItemWidth  = uiFont.MeasureWidth,
});
```

- `&` marks the mnemonic (Alt+letter opens it); `&&` is a literal `&`.
- Set `Checked` for a checkmark; toggle it in your handler.
- To rebuild a menu's items later, assign `menu.Menus[i].Items = newList`.

`WindowFrame` makes menus **modal for the whole frame**: while a dropdown is open, content widgets don't receive input, so a menu-item click won't reach whatever is behind it.

---

## Modal dialogs

`InputDialog` is a Win 3.1 message/prompt box. Use `frame.ShowDialog(...)`. It is **safe to call from a menu item**: it opens after the update loop, so the closing menu can't clobber it.

```csharp
// Text prompt
var dlg = new InputDialog("Rename", "New name:", currentName)
{
    OnOk = text => { Rename(text); frame.CloseDialog(); },
    OnCancel = frame.CloseDialog,
};
frame.ShowDialog(dlg);

// Confirm / info box (no text field, multi-line prompt with \n)
frame.ShowDialog(new InputDialog("About", "MyApp\nv1.0", "")
{
    NoField = true, OkLabel = "OK", CancelLabel = "Close",
    OnOk = _ => frame.CloseDialog(), OnCancel = frame.CloseDialog,
});
```

The dialog auto-sizes its height to the prompt and centers itself.

---

## Theming

A `Palette` is a record of chrome colors. `ThemeManager.Apply(palette)` writes them into `Theme`'s mutable statics and raises `ThemeManager.Changed`. Because every widget reads `Theme.*` at draw time, the whole UI reskins instantly.

```csharp
var Amber = new Palette("Amber CRT",
    Face:        new Color(0x20,0x18,0x08), LightEdge: new Color(0x40,0x30,0x10),
    DarkEdge:    new Color(0x08,0x06,0x02), MidEdge:   new Color(0x60,0x48,0x18),
    TitleActive: new Color(0x40,0x30,0x10), TitleInactive: new Color(0x30,0x24,0x0C),
    TitleText:   new Color(0xFF,0xC0,0x40), WindowBg:  new Color(0x18,0x10,0x04),
    Text:        new Color(0xFF,0xB0,0x30), TextDisabled: new Color(0x80,0x60,0x20),
    Desktop:     new Color(0x10,0x0A,0x02));

ThemeManager.Apply(Amber);
```

Build a Themes menu by iterating `ThemeManager.All` (see `DemoGame`). If a widget caches colors (for example pre-highlighted text), subscribe to `ThemeManager.Changed` and invalidate.

---

## How the borderless window works

On Windows, `WindowChrome`:

1. **Strips the frame.** `SetWindowLongPtr(GWL_STYLE)` removes `WS_CAPTION | WS_THICKFRAME | ...` and sets `WS_POPUP`, then sizes the client to the backbuffer (client == backbuffer keeps FNA3D rendering crisp).
2. **Subclasses the window proc** and answers **`WM_NCHITTEST`**: it maps a client point to a non-client code, `HTCAPTION` over the title bar's drag area, `HTLEFT`/`HTBOTTOMRIGHT`/etc. near the edges, otherwise `HTCLIENT`. Windows then handles the dragging, resizing, and Aero snap itself.

`WindowGame` supplies the hit-test (`WindowHitTest`) using `Frame.Title.IsOnDragArea(pt)` plus a 4px edge border, drives min/maximize/close from the `TitleBar` buttons, and re-asserts the borderless style each frame (SDL can re-add it). To change the drag region, override the title bar or the hit-test.

On non-Windows platforms `WindowChrome.Supported` is `false` and you get the normal OS frame. The rest of the toolkit is identical.

---

## Fonts

Each font is a PNG atlas of white glyphs (tinted at draw time) plus a JSON glyph map:

```json
{ "name": "...", "lineHeight": 13, "cellW": 6,
  "glyphs": [ { "c": 65, "x": 0, "y": 0, "w": 6, "h": 11, "advance": 7 }, ... ] }
```

`BitmapFont.Load(gd, "Content/fonts/sserife_11")` loads `.png` plus `.json`. To add a font, produce that pair (any glyph-atlas tool works) and load it. There's no kerning, and text is never fractionally scaled.

---

## Idle rendering

`WindowGame` runs a game loop, but it **stops drawing when nothing is happening** so a background tool doesn't spin a CPU core. It stays at full frame rate while there's input (mouse/keyboard/wheel) and for a short grace period after; when it goes quiet it calls `SuppressDraw()` each frame until something wakes it. Update still runs, so input stays responsive; only the GPU render is skipped.

**If your content changes without user input (animation, live or streaming data), call `RequestRedraw()`** so the loop doesn't sleep through it:

```csharp
// from the game:
RequestRedraw();

// from any widget:
Root()?.RequestRedraw();
```

For example, a token-streaming view calls `Root()?.RequestRedraw()` whenever new data arrives (or every frame while streaming); a focused text field does it so its caret keeps blinking (the built-in `InputDialog` already does). This throttle does not interfere with continuous output: as long as the producing code requests a redraw, every update renders. The loop only idles when the screen is genuinely static.

## Screenshots

`WindowGame.CaptureScreenshot(path = null)` saves a PNG of the current window and plays a white
flash and a shutter click, the way a phone does. The grab is a clean frame: the flash is drawn
only to the screen, never into the file. Call it from a menu item or a shortcut:

```csharp
MenuItemDef.Item("&Save Screenshot", null, () =>
{
    var path = CaptureScreenshot();          // returns the path it will write
    status.Message = "Saved " + Path.GetFileName(path);
});
```

With no argument it writes a timestamped file into a `Screenshots/` folder next to the
executable. The call waits a couple of frames first so a closing menu isn't in the shot. The
shutter comes from `ShutterSound`, a procedural click generated at runtime; if there's no audio
device it stays silent instead of failing. For headless or CI capture without the flash or
sound, set the `FNAWINDOW_SHOT` environment variable to a path and the window saves that file
and exits after a few frames.

## Background work

The game loop and all rendering run on one thread, and FNA requires it: create textures, touch
widgets, and draw only on the game thread. To keep the UI responsive during slow work (a
subprocess, file scans, parsing, indexing, network), do that work on your own thread and marshal
the result back with `MainThread`:

```csharp
// on a worker thread
var result = DoSomethingSlow();
MainThread.Post(() =>
{
    // back on the game thread: safe to touch widgets and upload textures
    view.SetData(result);
    RequestRedraw();          // wake the idle throttle so the change shows
});
```

`WindowGame` drains the queue at the start of every `Update`, so posted actions run before the
next frame. `MainThread.Post` is safe to call from any thread; the loop never blocks on a worker.

## Gotchas

- **ASCII-only fonts.** Unicode glyphs render as blanks. Draw shapes procedurally (see `TitleBar`'s arrow and bar glyphs).
- **Don't mutate `Children` during `Update`.** If a click needs to add/remove widgets, queue it and apply after the update loop (as `WindowFrame` does for dialogs).
- **Guard input while popups are open.** Early-return on `Root()?.Popup.IsOpen == true` in custom widgets, or let `WindowFrame` do it for content you host in the frame.
- **Read colors from `Theme` at draw time** so themes work; if you must cache, invalidate on `ThemeManager.Changed`.
- **Clip long content yourself.** There's no automatic scissor; use `Win31Renderer`'s scissor helpers or truncate/scroll (see `ScrollBar`).
