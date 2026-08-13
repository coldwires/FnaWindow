using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using FnaWindow;

namespace FnaWindow.Demo;

/// <summary>
/// A minimal app built on <see cref="WindowGame"/>: a File + Themes menu, a content panel, and
/// a status bar. That's all a new app needs: subclass WindowGame, override BuildUi, and fill
/// the frame.
/// </summary>
public sealed class DemoGame : WindowGame
{
    private MenuBar _menu = null!;
    private StatusBar _status = null!;

    public DemoGame() : base("FnaWindow Demo") { }

    // Use the machine's real Windows 3.1 faces (MS Sans Serif / Courier) rather than the shipped
    // atlases. Nothing is redistributed this way - see WindowGame.UseSystemFonts.
    protected override bool UseSystemFonts => true;

    // A software pointer is opt-in; the Demo uses the native OS cursor by default. To use one,
    // override BuildCursor and swap the PNG to taste, e.g.:
    //   protected override MouseCursor? BuildCursor()
    //       => MouseCursor.Load(GraphicsDevice,
    //           Path.Combine(System.AppContext.BaseDirectory, "Content", "cursors", "block.png"),
    //           hotspot: new Point(4, 4), scale: 2);

    protected override void BuildUi(WindowFrame frame, BitmapFont uiFont)
    {
        _status = new StatusBar { Message = "Ready. Drag the title bar, resize the edges, try the Themes menu" };
        frame.SetStatus(_status);
        frame.SetContent(new DemoContent());

        var file = new List<MenuItemDef>
        {
            MenuItemDef.Item("&Open...", null, () => ShowOpen(frame)),
            MenuItemDef.Item("&Find and Replace...", null, () => ShowForm(frame)),
            MenuItemDef.Item("&Rename...", null, () => ShowInput(frame)),
            MenuItemDef.Item("&Save Screenshot", null, () =>
            {
                var path = CaptureScreenshot();
                _status.Message = "Saved " + Path.GetFileName(path);
            }),
            MenuItemDef.Item("&About...", null, () => ShowAbout(frame)),
            MenuItemDef.Sep(),
            MenuItemDef.Item("E&xit", null, Exit),
        };

        // The Vista look pairs its palette with its own art skin, so picking it (or leaving it)
        // swaps the skin too; the plain palettes keep the Win 3.1 art skin.
        ThemeManager.Register(VistaPng.Palette);

        var themes = new List<MenuItemDef>();
        foreach (var p in ThemeManager.All)
        {
            var pal = p;
            themes.Add(new MenuItemDef
            {
                Label = pal.Name,
                Checked = ThemeManager.Current == pal.Name,
                OnClick = () =>
                {
                    ApplyTheme(pal);
                    _status.Message = "Theme: " + pal.Name;
                    RefreshThemeChecks();
                },
            });
        }

        _menu = new MenuBar(new List<TopMenu>
        {
            new("&File", file),
            new("&Themes", themes),
        })
        {
            MeasureTitleWidth = uiFont.MeasureWidth,
            MeasureItemWidth = uiFont.MeasureWidth,
        };
        frame.SetMenu(_menu);

        // FNAWINDOW_DIALOG=open|form|input opens a modal at startup, so FNAWINDOW_SHOT can capture
        // one. A headless shot draws the window and exits, so without this no screenshot in this
        // repo has ever contained a dialog and the modals could only be checked by hand.
        switch (System.Environment.GetEnvironmentVariable("FNAWINDOW_DIALOG"))
        {
            case "open": ShowOpen(frame); break;
            case "form": ShowForm(frame); break;
            case "input": ShowInput(frame); break;
        }

        // FNAWINDOW_THEME=<palette name> starts on that theme, so FNAWINDOW_SHOT can capture a
        // non-default skin headlessly ("vista" and "Windows Vista" both work).
        var want = System.Environment.GetEnvironmentVariable("FNAWINDOW_THEME");
        if (!string.IsNullOrEmpty(want))
        {
            var pal = ThemeManager.ByName(want) ?? (want == "vista" ? VistaPng.Palette : null);
            if (pal != null) { ApplyTheme(pal); RefreshThemeChecks(); }
        }
    }

    private void ApplyTheme(Palette pal)
    {
        if (pal.Name == VistaPng.Palette.Name)
        {
            VistaPng.LoadAssets(GraphicsDevice); // idempotent; Apply needs the art already loaded
            VistaPng.Apply();
        }
        else
        {
            if (ThemeManager.Skin is VistaSkin) ThemeManager.ApplySkin(new Win31PngSkin());
            ThemeManager.Apply(pal);
        }
    }

    private void RefreshThemeChecks()
    {
        foreach (var it in _menu.Menus[1].Items) // Themes
            if (!it.IsSeparator) it.Checked = it.Label == ThemeManager.Current;
    }

    // Exercises RetroFileDialog, which is the engine's own dialog and was previously not reachable
    // from the Demo at all - so nothing here ever proved it worked.
    private void ShowOpen(WindowFrame frame)
    {
        var dlg = new RetroFileDialog(save: false, "*.*", System.AppContext.BaseDirectory, null)
        {
            OnOk = path => { _status.Message = "Chose " + Path.GetFileName(path); frame.CloseDialog(); },
            OnCancel = frame.CloseDialog,
        };
        frame.ShowDialog(dlg);
    }

    // Exercises FormDialog - the multi-field modal. Like RetroFileDialog before it, nothing else in
    // this repo runs it, so without this entry it would go untested.
    private void ShowForm(WindowFrame frame)
    {
        var dlg = new FormDialog("Find and Replace")
            .AddField("Find what:", "grafix")
            .AddField("Replace with:", "graphics")
            .AddCheck("Match case")
            .AddCheck("Whole word only", true);
        dlg.OkLabel = "Replace All";
        dlg.OnOk = v =>
        {
            _status.Message = $"Replace '{v.Text(0)}' with '{v.Text(1)}' (case={v.Check(0)}, whole={v.Check(1)})";
            frame.CloseDialog();
        };
        dlg.OnCancel = frame.CloseDialog;
        frame.ShowDialog(dlg);
    }

    // Exercises InputDialog WITH its text field. The About box below sets NoField, so until this
    // existed the one-field modal was never opened from this repo either.
    private void ShowInput(WindowFrame frame)
    {
        var dlg = new InputDialog("Rename", "New name:", "a name long enough to scroll the field")
        {
            OnOk = s => { _status.Message = "Renamed to " + s; frame.CloseDialog(); },
            OnCancel = frame.CloseDialog,
        };
        frame.ShowDialog(dlg);
    }

    private void ShowAbout(WindowFrame frame)
    {
        var dlg = new InputDialog("About",
            "FnaWindow\nA Win 3.1-styled borderless window engine on FNA.\nSubclass WindowGame to build your own.", "")
        {
            NoField = true, OkLabel = "OK", CancelLabel = "Close",
            OnOk = _ => frame.CloseDialog(),
            OnCancel = frame.CloseDialog,
        };
        frame.ShowDialog(dlg);
    }
}
