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
 +- RootDesktop            <- root of the widget tree; owns the PopupLayer + focus
 |   +- WindowFrame        <- the window chrome
 |       +- TitleBar       <- draggable caption + min/max/close
 |       +- MenuBar        <- (optional) top menus -> dropdown popups
 |       +- <your Content> <- any Widget, fills the middle
 |       +- StatusBar      <- (optional)
 +- WindowChrome           <- Win32/SDL interop (borderless + WM_NCHITTEST)
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
- **`Root()`** walks up to the `RootDesktop`. To ignore input while a menu or dialog is up, check **`InputBlocked`**, not `Popup.IsOpen` - see [Gotchas](#gotchas) for why the difference matters.

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
        if (InputBlocked) return;                        // let menus/dialogs be modal
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
r.Fill(rect, color);                       // solid fill (1x1 white pixel, tinted)
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

`frame.ShowDialog(...)` takes **any** widget that lays itself out over the frame, and drives it for you. It is **safe to call from a menu item**: the dialog opens after the update loop, so the closing menu can't clobber it. Two dialogs ship with the engine, `InputDialog` (prompt, confirm, message) and `RetroFileDialog` (Open / Save As).

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

Set `AltLabel` and `OnAlt` for a third button, which is what a Save / Discard / Cancel prompt needs:

```csharp
frame.ShowDialog(new InputDialog("MyApp", "Save changes to " + name + "?", "")
{
    NoField = true, OkLabel = "Save", AltLabel = "Discard", CancelLabel = "Cancel",
    Bounds = frame.Bounds,
    OnOk = _ => { frame.CloseDialog(); Save(); },
    OnAlt = () => { frame.CloseDialog(); Discard(); },
    OnCancel = frame.CloseDialog,
});
```

The dialog auto-sizes its height to the prompt and centers itself.

`RetroFileDialog` picks a path. It browses directories and drives, filters by pattern, and reads each directory on a background thread so a slow network path cannot freeze the window:

```csharp
var dlg = new RetroFileDialog(save: false, pattern: "*.txt;*.*", initialDir: null, defaultName: null)
{
    Bounds = frame.Bounds,
};
dlg.OnOk = path => { frame.CloseDialog(); Open(path); };
dlg.OnCancel = frame.CloseDialog;
frame.ShowDialog(dlg);
```

It is assembled from two pieces you can use directly, which is worth knowing if you are building anything that browses files rather than just picking one.

---

## Lists

`ListBox` is the Win 3.1 list: a sunken well of fixed-height rows, one selection, and a real `ScrollBar` when the rows do not fit.

```csharp
var list = new ListBox();
list.SetItems(new[] { "alpha", "beta", "gamma" });
list.SelectionChanged = i => status.Message = "Row " + i;
list.Activated = i => Open(i);          // double-click, or Enter
frame.SetContent(list);
```

Set `HandleKeys = false` when something else in the window needs the arrow keys (a command prompt, say) and drive it with `MoveSelection` and `Select` instead.

For rows that are not plain strings, override two members and keep your own row data:

```csharp
sealed class FileList : ListBox
{
    public readonly List<FsEntry> Rows = new();
    protected override int RowCount => Rows.Count;

    protected override void DrawRow(Win31Renderer r, int i, Rectangle rect, bool selected)
    {
        var fg = selected ? Theme.TitleText : Theme.Text;   // the band is already painted
        r.DrawText(r.UiFont, Rows[i].Display, rect.X + 4, rect.Y + 2, fg);
        r.DrawText(r.UiFont, Rows[i].Size.ToString(), rect.Right - 80, rect.Y + 2, fg);
    }
}
```

Scrolling, selection, keys, the wheel and double-click are the same either way.

---

## Reading directories

`DirectoryListing.Read` returns a directory as `FsEntry` rows - a parent link, subdirectories, files matching a pattern, and the drive roots - with the name, display form (`[..]`, `[subdir]`, `[-c-]`), full path, size and timestamp. Every filesystem call inside is wrapped, because enumerating a directory fails for ordinary reasons and a browser has to show what it can.

It does real I/O, so UI code should not call it directly. Use `DirectoryLoader`, which runs it off the game thread and applies the result back on it:

```csharp
private readonly DirectoryLoader _loader = new();

void Navigate(string dir)
{
    _loader.Begin(dir, entries => { _rows = entries; list.Layout(); }, pattern: "*.*");
}
```

The generation stamping is the reason to use it rather than a bare `Task.Run`: hold Down through a directory tree and you start a read per folder, and they finish out of order. A result that has been superseded is dropped instead of overwriting the folder you are now looking at. Use one loader per list. `DirectoryListing.Sort` re-orders by name, size or date while keeping the parent link first and directories above files.

---

## Text areas

`TextArea` is a complete multi-line editor: a sunken well over a fixed monospace grid, with a caret, selection, both scrollbars, undo/redo, the system clipboard, word motion, and word wrap. Give it a `TextBuffer` and put it in the frame:

```csharp
var buffer = new TextBuffer(File.ReadAllText(path));
var text = new TextArea(buffer) { WordWrap = true };
text.CaretMoved += p => status.RightCells = new[] { $"Ln {p.Line + 1}, Col {p.Col + 1}" };
frame.SetContent(text);
```

`TextBuffer` is the model and is usable on its own: line-based, edits are range replacements, every edit bumps `Version` and raises `Changed` with the range that changed, and undo/redo coalesces typing into word-sized groups. `Tabs.Expand` turns hard tabs into spaces at tab stops, which is worth doing on load and on paste since the grid is one cell per character.

### Read-only

`ReadOnly` stops the **user** editing: typing, Enter, Tab, Backspace, Delete, Cut, Paste, Undo and Redo all do nothing, and no caret is drawn. Moving, selecting, scrolling, Select All and Copy still work, so the text stays readable and copyable rather than inert.

The **program** can still edit, deliberately - you write through `Buf` as usual. That is what makes it useful for an output pane, where a log is appended to while the reader may only look and copy:

```csharp
var output = new TextArea(buffer) { ReadOnly = true, HighlightCurrentLine = false };
```

Turn the current-line band off with it, as above: it tracks a caret the reader cannot see or move.

### Subclassing it

Anything language-specific (syntax coloring, error squiggles, completion popups) is added by subclassing rather than by copying the widget. Each hook has a plain default, so you override only what you need:

| Hook | For |
|---|---|
| `ColorLine(line, text)` | Per-character colors for one line; null means plain `Theme.Text`. |
| `DrawLineBackground` / `DrawLineOverlay` | Behind and over one row's text. The overlay gets the row's visible column span, so clip to it. |
| `DrawOverlays` | Over the whole widget, after the scrollbars: popups and tooltips. |
| `MouseIntercept` / `ClickIntercept` | Take the mouse before the caret does. |
| `OnBufferChanged` / `OnCaretMoved` | React to an edit or a caret move. |
| `EnterKey` / `Backspace` / `DeleteKey` | Smarter versions; override `OnKey`/`OnChar` and call `base` for everything else. |

One thing to know before you position anything yourself: **the widget works in visual rows, not lines.** With wrap off each line is exactly one row, so the two modes share one code path, but `ScrollLine` is a row index either way. Use `PointFor(position)` and `RowIndexOf(position)` to place your own popups instead of computing `line - ScrollLine`, and your code keeps working when wrap is switched on.

---

## Theming

A `Palette` is a record of the chrome colors, plus optional syntax and squiggle colors for apps
that render code (keyword, type, string, comment, error, warning). `ThemeManager.Apply(palette)`
writes them into `Theme`'s mutable statics and raises `ThemeManager.Changed`. Because every widget
reads `Theme.*` at draw time, the whole UI reskins instantly. The syntax/squiggle fields are
optional; leave them out on a palette that does not render code, and those colors are left as-is.

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

Add your own palettes to the shared set with `ThemeManager.Register(palette)`, then build a Themes
menu by iterating `ThemeManager.All` (see `DemoGame`). If a widget caches colors (for example
pre-highlighted text), subscribe to `ThemeManager.Changed` and invalidate.

---

## How the borderless window works

On Windows, `WindowChrome`:

1. **Subclasses the window proc first**, so the two messages below are ours before anything triggers them.
2. **Keeps every frame style and hides the frame instead.** The window keeps `WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU` and is *not* a `WS_POPUP`. **`WM_NCCALCSIZE`** then collapses the non-client area to nothing, so the caption and border are never drawn while the window stays, to Windows, an ordinary application window.
3. **Answers `WM_NCHITTEST`**: it maps a client point to a non-client code, `HTCAPTION` over the title bar's drag area, `HTLEFT`/`HTBOTTOMRIGHT`/etc. near the edges, otherwise `HTCLIENT`. Windows then handles dragging and resizing itself.

**Why not just set `WS_POPUP`?** Because that is what the engine used to do, and it silently costs everything the shell hangs off those styles. `WS_THICKFRAME` is what makes a window *snappable* - without it, dragging to a screen edge or corner does nothing at all, and neither does Win+Arrow. `WS_MAXIMIZEBOX` drives Snap Layouts and Win+Up, `WS_MINIMIZEBOX` is what lets a taskbar click minimize an active window (and gives the minimize animation), and `WS_SYSMENU` is Alt+Space and the taskbar's right-click window menu. Answering `WM_NCHITTEST` with `HTCAPTION` gets you *dragging*, which looks like enough until you try to snap.

**Order matters.** `WM_NCCALCSIZE` arrives the moment the styles change, so the subclass must be installed *before* the styles are applied. The other way round, the original window proc answers it and the client area keeps a full frame's worth of inset permanently, because nothing sends another one until the next resize.

**Maximize goes through the OS.** `ShowWindow(SW_MAXIMIZE/SW_RESTORE)` and `IsZoomed`, not a private "am I maximized" flag - Windows can maximize the window too (snap, Win+Up, the taskbar menu), so there is one answer and the OS owns it. `WindowGame` re-reads it each frame to keep the caption's restore glyph honest. Maximized is also the one case where `WM_NCCALCSIZE` does *not* return the full rect: Windows sizes a maximized window so its frame overhangs every edge, so the client is inset by that frame or the top rows vanish and the bottom runs under the taskbar.

`WindowGame` supplies the hit-test (`WindowHitTest`) using `Frame.Title.IsOnDragArea(pt)` plus a 4px edge border, drives min/maximize/close from the `TitleBar` buttons, and re-asserts the styles each frame (SDL can change them). To change the drag region, override the title bar or the hit-test.

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
- **Guard input while popups are open.** Early-return on `InputBlocked` in custom widgets, or let `WindowFrame` do it for content you host in the frame. Do **not** test `Popup.IsOpen` for this: a menu closes partway through the frame, on the click that chose an item, so `IsOpen` is already false while that click is still being delivered - and it then also lands on whatever the menu was covering. `InputBlocked` uses `BlocksInput`, which stays true for the rest of that frame, and additionally returns false for the popup's own contents, so a widget you put inside a dialog is not made dead by its own dialog.
- **Dispose a `Texture2D` you only used to decode a PNG.** `Texture2D.FromStream` followed by `GetData`, to hand the pixels somewhere else, leaves a graphics resource for the finalizer, and FNA prints "A resource of type Texture2D was not Disposed" once per leak when the app exits. Wrap it in `using`. Textures you keep and draw with are fine as they are.
- **Read colors from `Theme` at draw time** so themes work; if you must cache, invalidate on `ThemeManager.Changed`.
- **Clip long content yourself.** There's no automatic scissor; use `Win31Renderer`'s scissor helpers or truncate/scroll (see `ScrollBar`).
