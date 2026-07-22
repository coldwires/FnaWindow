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
            MenuItemDef.Item("&Save Screenshot", null, () =>
            {
                var path = CaptureScreenshot();
                _status.Message = "Saved " + Path.GetFileName(path);
            }),
            MenuItemDef.Item("&About...", null, () => ShowAbout(frame)),
            MenuItemDef.Sep(),
            MenuItemDef.Item("E&xit", null, Exit),
        };

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
                    ThemeManager.Apply(pal);
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
