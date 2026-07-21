using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>
/// The top-level window chrome: a raised outer frame with a draggable <see cref="TitleBar"/>,
/// an optional <see cref="MenuBar"/>, an app-supplied content widget that fills the middle, an
/// optional <see cref="StatusBar"/>, and a bottom-right resize grip. Add your content with
/// <see cref="SetContent"/>. <see cref="WindowGame"/> wires the title-bar buttons and native
/// drag/resize.
/// </summary>
public sealed class WindowFrame : Widget
{
    /// <summary>Outer raised-frame thickness (classic Win 3.1 look).</summary>
    public const int FrameB = 2;

    public TitleBar Title { get; } = new();
    public MenuBar? Menu { get; private set; }
    public StatusBar? Status { get; private set; }
    public Widget? Content { get; private set; }

    private InputDialog? _dialog, _pending;

    public WindowFrame() { Add(Title); }

    public void SetMenu(MenuBar menu) { Menu = menu; Add(menu); }
    public void SetStatus(StatusBar status) { Status = status; Add(status); }
    public void SetContent(Widget content) { Content = content; Add(content); }

    /// <summary>Show a modal dialog (centered, driven here). Safe to call from a menu item - it
    /// opens after the update loop so the closing menu can't clobber it. Use OnOk/OnCancel to close.</summary>
    public void ShowDialog(InputDialog dialog) => _pending = dialog;

    public void CloseDialog()
    {
        Root()?.Popup.Close();
        _dialog = null;
    }

    public override void Update(InputState input, GameTime t)
    {
        // A popup (menu or dialog) is modal for the whole frame: while one is open, only the
        // chrome (title/menu) updates - content is skipped so a menu-item click can't leak
        // through to the content once the menu closes mid-frame.
        bool popupOpen = Root()?.Popup.BlocksInput == true;
        foreach (var c in Children.ToArray())
        {
            if (!c.Visible) continue;
            bool chrome = ReferenceEquals(c, Title) || ReferenceEquals(c, Menu);
            if (popupOpen && !chrome) continue;
            c.Update(input, t);
        }
        _dialog?.Update(input, t); // drive the open modal, if any

        // Open a queued dialog now that the child (menu) update is done.
        if (_pending != null) { _dialog = _pending; _pending = null; Root()?.Popup.Open(_dialog); }
    }

    public override void Layout()
    {
        int x = Bounds.X + FrameB, w = Bounds.Width - 2 * FrameB, y = Bounds.Y + FrameB;

        Title.Bounds = new Rectangle(x, y, w, Theme.TitleBarHeight);
        y += Theme.TitleBarHeight;

        if (Menu != null)
        {
            Menu.Bounds = new Rectangle(x, y, w, Theme.MenuBarHeight);
            y += Theme.MenuBarHeight;
        }

        int bottom = Bounds.Bottom - FrameB;
        if (Status != null)
        {
            bottom -= Theme.StatusBarHeight;
            Status.Bounds = new Rectangle(x, bottom, w, Theme.StatusBarHeight);
        }

        if (Content != null) Content.Bounds = new Rectangle(x, y, w, bottom - y);

        base.Layout();
        _dialog?.Layout(); // keep a modal centered across resizes
    }

    public override void Draw(Win31Renderer r)
    {
        r.Fill(Bounds, Theme.Face);                    // frame + body background
        base.Draw(r);                                   // inset children draw over it
        r.DrawBevel(Bounds, BevelStyle.RaisedThick);    // raised outer window frame
        DrawSizeGrip(r);
    }

    private void DrawSizeGrip(Win31Renderer r)
    {
        for (int i = 0; i < 3; i++)
            for (int k = 0; k <= i; k++)
            {
                int px = Bounds.Right - 5 - k * 4;
                int py = Bounds.Bottom - 5 - (i - k) * 4;
                r.Fill(px + 1, py + 1, 2, 2, Theme.DarkEdge);
                r.Fill(px, py, 2, 2, Theme.LightEdge);
            }
    }
}
