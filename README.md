# FnaWindow

Build desktop apps with fully custom chrome on FNA. The OS title bar and border are removed, but the window still drags, resizes from any edge, maximizes, and snaps like a normal one.

It comes with a complete Windows 3.1 look as the default: authored 9-slice chrome art, the 3.1 cursor set, and the bold chrome font, all applied for you. That look is a palette plus a swappable skin, so you can recolour it, replace the art, or draw the window however you like. What sits underneath is an ordinary resizable OS window with no system frame, which is enough to build a text editor, a file explorer, a chat client, dev tooling, or a small utility on top of. You write one subclass to get a working app.

![demo](docs/demo.png)

## Why

Building a custom-chrome desktop window normally means fighting the OS. This engine handles that part for you:

- **Borderless with native behavior.** The OS frame is stripped (`WS_POPUP`), and drag, edge-resize, snap, and maximize still work because the window answers `WM_NCHITTEST` (the title bar acts as the caption). There is no moving-the-window-from-the-game-loop workaround.
- **Retained-mode widget toolkit.** A small `Widget` tree with a from-scratch Win 3.1 renderer (a two-function bevel system), bitmap fonts, menus, scrollbars, modal dialogs, popups, and an Open / Save As file dialog.
- **Text editing included.** `TextArea` is a complete editing widget: caret and selection, both scrollbars, undo/redo, the system clipboard, word wrap, and the standard key and mouse model. It knows nothing about languages, and it leaves hooks so a code editor can add coloring, squiggles and completion popups by subclassing rather than by reimplementing any of it.
- **Runtime theming.** Swap the whole palette while the app runs. Windows 3.1, Midnight, and Slate ship with it, and adding one is a single record literal.
- **Self-contained.** FNA and its native libraries are vendored under `lib/`, so you can clone and build with no extra setup.

The borderless path is Windows-only. On macOS and Linux the window falls back to the normal OS frame, and the rest of the toolkit works the same.

## Quick start

```sh
git clone https://github.com/coldwires/FnaWindow fna-custom-window
cd fna-custom-window
dotnet run --project Demo
```

No setup step: FNA and the native libraries are vendored here, and the engine has no submodules of
its own. (An app that *consumes* the engine does need one - see below.)

You'll get the demo window above: drag it, resize the edges, and try the **Themes** menu.

## Repository layout

```
FnaWindow.csproj     the engine, a class library (this is what you reference)
src/                 engine source: Gui/ Window/ Theme/ Editor/
Content/  lib/       vendored fonts, FNA, and its native libs
Demo/                a runnable example app that references the library
templates/           copy-paste starter for a new build-on (git-submodule wiring)
```

`FnaWindow.csproj` is a library. The demo is a separate exe (`Demo/Demo.csproj`) that references it the same way your own app would. The engine's fonts and native libraries are marked as Content, so they copy into any project that references it. A consumer gets a working window with its assets in place and nothing to copy by hand.

## Use it in your own app (git submodule)

Add the engine as a git submodule and reference the library project. Base fixes then reach your app with a `git submodule update`, and each app stays pinned to the exact engine commit it was built against.

```sh
# in your new app's repo
git submodule add https://github.com/coldwires/FnaWindow engine
```

```xml
<!-- YourApp.csproj -->
<ItemGroup>
  <ProjectReference Include="engine\FnaWindow.csproj" />
</ItemGroup>
```

That single reference pulls in the engine, FNA, the native libraries, and the fonts. See **[templates/starter-app/](templates/starter-app/)** for a short starter you can copy, and **[docs/EXTENDING.md](docs/EXTENDING.md#consuming-the-engine-as-a-git-submodule)** for the full workflow (updating, pinning, and the non-submodule option).

**Building 32-bit.** The engine ships both sets of native libraries and defaults to 64-bit. A
32-bit app sets `<PlatformTarget>x86</PlatformTarget>` on itself and asks for the matching natives
through the reference, which is the only way a property reliably reaches a referenced project:

```xml
<ProjectReference Include="engine\FnaWindow.csproj" AdditionalProperties="FnaWindowNativeArch=x86" />
```

A 32-bit process cannot load 64-bit natives, and the failure does not say so. The 32-bit set has no
`D3D12Core.dll` because the Direct3D 12 Agility SDK is 64-bit only; FNA3D uses D3D11 there.

**Give your app a setup step.** A submodule is a pointer, not a copy: anyone who clones your app the
normal way gets an empty `engine/` folder and a build that fails with confusing errors about
`Microsoft.Xna` not existing. Either clone with `--recurse-submodules`, or ship a one-line script so
nobody has to remember:

```sh
git submodule update --init --recursive
```

Setting `git config --global submodule.recurse true` once per machine makes every clone and pull do
this automatically, for every repo.

## Build your own app

Subclass `WindowGame`, override `BuildUi`, and fill the frame:

```csharp
using System.Collections.Generic;
using FnaWindow;

public sealed class MyApp : WindowGame
{
    public MyApp() : base("My App", 900, 600) { }

    protected override void BuildUi(WindowFrame frame, BitmapFont uiFont)
    {
        // Status bar
        frame.SetStatus(new StatusBar { Message = "Hello" });

        // Your content (any Widget)
        frame.SetContent(new MyContent());

        // A menu
        var file = new List<MenuItemDef>
        {
            MenuItemDef.Item("&Quit", null, Exit),
        };
        frame.SetMenu(new MenuBar(new List<TopMenu> { new("&File", file) })
        {
            MeasureTitleWidth = uiFont.MeasureWidth,
            MeasureItemWidth  = uiFont.MeasureWidth,
        });
    }
}

// Program.cs
using var game = new MyApp();
game.Run();
```

The base class handles the borderless window, native drag and resize, the render loop, input, fonts, and the title-bar buttons.

## What's in the box

| Piece | What it is |
|---|---|
| `WindowGame` | The base FNA `Game`: borderless window, `WM_NCHITTEST` drag/resize, capped and re-entrancy-safe render loop, input, fonts. |
| `WindowFrame` | The window chrome: raised border, title bar, optional menu and status bar, content area, size grip, modal-dialog host. |
| `WindowChrome` | Win32/SDL interop: strip the frame, answer hit-tests, move/resize helpers. |
| `Win31Renderer` | Fills, bevels (`Raised/SunkenThin/Thick`), panels, text, dither, mnemonics. |
| `Widget` / `RootDesktop` / `PopupLayer` | Retained-mode tree, focus, top-most popups. |
| `TitleBar` `MenuBar` `Toolbar` `ScrollBar` `StatusBar` `ListBox` | The stock widgets. `ListBox` is a scrollable, selectable row list; override `DrawRow` for columns. |
| `DirectoryListing` / `DirectoryLoader` | Read a directory into rows (parent link, folders, filtered files, drives) with sizes and timestamps; the loader does it off the render thread and drops results the user has already navigated away from. |
| `InputDialog` / `FormDialog` / `RetroFileDialog` | Modal prompt, confirm and message boxes; a multi-field form (labelled fields + check boxes) for Find and Replace and the like; a Win 3.1 Open / Save As dialog that browses directories off the render thread. |
| `TextField` | One line of editable text in a sunken well: caret, selection, word motion, clipboard, horizontal scrolling. The dialogs above are built on it; a widget that needs an editable box owns one the way it owns a `PushButton`. |
| `TextArea` / `TextBuffer` | Multi-line text editing: caret, selection, undo/redo, clipboard, word wrap, key and mouse model, with seams for a richer editor to subclass. `ReadOnly` gives a pane the program writes and the user can only read and copy. |
| `Clipboard` / `Tabs` | System clipboard text with an in-process fallback; tab-to-space expansion at tab stops. |
| `Theme` / `ThemeManager` / `Palette` | Mutable palette plus runtime theme switching. |
| `Win31PngSkin` / `Win31Skin` | The default look: authored 9-slice art, tinted by the palette so themes still work. Any missing PNG falls back to the procedural drawing. |
| `MainThread` | Post background-thread results back to the render thread. |

## Docs

See **[docs/EXTENDING.md](docs/EXTENDING.md)** for the full guide: custom widgets, custom themes, menus, dialogs, the renderer primitives, and how the borderless window works.

## Fonts

Four bitmap-font atlases live in `Content/fonts/`: a proportional MS Sans Serif style UI font in regular and bold, a larger bold cut for chrome, and a Fixedsys style monospace font for text areas. Each is a PNG plus a JSON glyph map. Swap in your own by matching that format.

## License

**MIT.** See [LICENSE](LICENSE). You can build closed-source, commercial apps on it.

Vendored third-party components keep their own permissive licenses. FNA (`lib/FNA`) is the Microsoft Public License (Ms-PL); the native libraries in `lib/fnalibs/x64` and `lib/fnalibs/x86` (SDL3, FAudio, FNA3D, Theorafile) are zlib or similar. All of them allow commercial, closed-source distribution as long as you preserve their notices when you ship.
