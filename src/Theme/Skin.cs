using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>
/// A skin owns HOW chrome is drawn - panels and their borders - so the whole look can be swapped at
/// runtime, not just the palette colors. Widgets always call <see cref="Win31Renderer.DrawPanel"/> /
/// <see cref="Win31Renderer.DrawBevel"/>; the renderer forwards to the active skin
/// (<see cref="ThemeManager.Skin"/>). The default is <see cref="Win31Skin"/>, which reproduces the
/// classic Windows 3.1 bevels exactly; a richer skin can draw gradients, 9-slice frames, and so on.
/// Layout, hit regions, and the widget tree are skin-invariant - a skin only changes drawing.
/// </summary>
public abstract class Skin
{
    public abstract string Name { get; }

    /// <summary>Border thickness of a panel style, used by callers to inset content.</summary>
    public abstract int Thickness(BevelStyle style);

    /// <summary>Draw a filled, bordered panel of <paramref name="style"/> with interior <paramref name="bg"/>.</summary>
    public abstract void DrawPanel(Win31Renderer r, Rectangle rect, BevelStyle style, Color bg);

    /// <summary>Draw just the border/bevel of <paramref name="style"/>.</summary>
    public abstract void DrawBevel(Win31Renderer r, Rectangle rect, BevelStyle style);

    /// <summary>Draw a UI text run. Default is a plain draw; a skin may add a drop-shadow, etc.
    /// Editor/code text draws directly (not through here), so it stays crisp regardless of skin.</summary>
    public virtual void DrawText(Win31Renderer r, BitmapFont font, string s, int x, int y, Color color)
        => font.Draw(r.Sb, s, x, y, color);

    /// <summary>Draw the selection background of a menu/list row. <paramref name="showArrow"/> hints a
    /// vertical list row (a skin may add a selector arrow); default fills the navy selection color.</summary>
    public virtual void DrawSelection(Win31Renderer r, Rectangle rect, bool showArrow)
        => r.Fill(rect, Theme.TitleActive);

    /// <summary>The skin's own UI font, or null to use the app's default. When non-null the renderer
    /// returns it from <see cref="Win31Renderer.UiFont"/>, so widgets both measure and draw with it.</summary>
    public virtual BitmapFont? UiFont => null;

    // Chrome heights/sizes (Theme forwards to these). Defaults are the classic Win 3.1 values; a
    // skin can enlarge them - e.g. a bigger font needs taller rows and bars. Non-size metrics
    // (paddings, editor cell) stay Theme consts.
    public virtual int TitleBarHeight => 19;
    public virtual int MenuBarHeight => 24;
    public virtual int MenuItemHeight => 17;
    public virtual int ToolbarHeight => 22;
    public virtual int ToolButtonSize => 22;
    public virtual int StatusBarHeight => 20;
    public virtual int MdiChildTitleHeight => 19;
    public virtual int ScrollBarThickness => 16;

    // -- Small glyph-bearing buttons (title caption + scrollbar) -----------
    // A button plus its glyph. Routed through the skin (not drawn in the widget) so a skin can swap
    // the whole button for authored art; the default is the classic Win 3.1 procedural drawing.

    /// <summary>The caption button's edge length. Default is the classic 18px; an art skin returns
    /// the button PNG's native size so the layout (and click area) follow the art, never stretched.</summary>
    public virtual int CaptionButtonSize(CaptionButton kind) => WindowCaption.ButtonSize;

    /// <summary>Fill the caption strip behind the title text and buttons. The 3.1 look is the flat
    /// palette color; an art skin can draw a gradient instead. <paramref name="bg"/> is the
    /// active/inactive caption color the window chose, so a skin keys its art off
    /// <see cref="Theme.TitleActive"/> / <see cref="Theme.TitleInactive"/> and falls back to the
    /// flat fill for any other color a caller passes.</summary>
    public virtual void DrawCaptionFill(Win31Renderer r, Rectangle rect, Color bg) => r.Fill(rect, bg);

    /// <summary>An MDI child's frame. Defaults to the window frame; a skin whose outer frame
    /// rounds (paired with a window region) squares this one - an inner window has no region,
    /// so a rounded ring would expose the child's own fill at the corners.</summary>
    public virtual void DrawChildWindowFrame(Win31Renderer r, Rectangle rect) => DrawWindowFrame(r, rect);

    public virtual void DrawCaptionButton(Win31Renderer r, Rectangle rect, CaptionButton kind, bool pressed)
    {
        r.DrawPanel(rect, pressed ? BevelStyle.SunkenThin : BevelStyle.RaisedThin, Theme.Face);
        var b = rect; if (pressed) b.Offset(1, 1);
        switch (kind)
        {
            case CaptionButton.System: DrawSysGlyph(r, b); break;
            case CaptionButton.Minimize: DrawMinGlyph(r, b); break;
            case CaptionButton.Maximize: DrawMaxGlyph(r, b); break;
            case CaptionButton.Restore: DrawRestoreGlyph(r, b); break;
        }
    }

    /// <summary>A menu row's checkmark at (cx, cy) in <paramref name="color"/>. Default is the 5px tick.</summary>
    public virtual void DrawMenuCheck(Win31Renderer r, int cx, int cy, Color color)
    {
        r.Fill(cx, cy, 1, 1, color);
        r.Fill(cx + 1, cy + 1, 1, 1, color);
        r.Fill(cx + 2, cy, 1, 1, color);
        r.Fill(cx + 3, cy - 1, 1, 1, color);
        r.Fill(cx + 4, cy - 2, 1, 1, color);
    }

    /// <summary>A push button's background + bevel (e.g. toolbar buttons). Default is the classic
    /// raised/sunken thin bevel; an art skin can supply authored button art.</summary>
    public virtual void DrawButton(Win31Renderer r, Rectangle rect, bool pressed)
        => DrawPanel(r, rect, pressed ? BevelStyle.SunkenThin : BevelStyle.RaisedThin, Theme.Face);

    /// <summary>A window's outer frame (top-level window and MDI children). Default is this skin's own
    /// thick raised bevel, so every skin frames its windows in its own style; Win 3.1 overrides it with
    /// the flat black frame, and an art skin with a 9-slice frame PNG.</summary>
    public virtual void DrawWindowFrame(Win31Renderer r, Rectangle rect)
        => DrawBevel(r, rect, BevelStyle.RaisedThick);

    /// <summary>Whether the menu bar gets the flat 1px black rules above/below it (a Win 3.1 detail).
    /// Off by default so other skins don't inherit that look.</summary>
    public virtual bool DrawMenuSeparators => false;

    /// <summary>The little square where a vertical and horizontal scrollbar meet. Default is a flat
    /// Face fill; an art skin can blit a corner PNG.</summary>
    public virtual void DrawScrollCorner(Win31Renderer r, Rectangle rect) => r.Fill(rect, Theme.Face);

    /// <summary>A separator row in a popup menu, drawn within <paramref name="rect"/> (the full row).
    /// Default is the classic 2px engraved groove (grey over white); an art skin can blit a strip.</summary>
    public virtual void DrawMenuSeparator(Win31Renderer r, Rectangle rect)
    {
        int gy = rect.Y + rect.Height / 2;
        r.HLine(rect.X + 2, gy, rect.Width - 4, Theme.MidEdge);
        r.HLine(rect.X + 2, gy + 1, rect.Width - 4, Theme.LightEdge);
    }

    public virtual void DrawScrollButton(Win31Renderer r, Rectangle rect, ScrollArrowDir dir, bool pressed)
    {
        r.DrawPanel(rect, pressed ? BevelStyle.SunkenThin : BevelStyle.RaisedThin, Theme.Face);
        var b = rect; if (pressed) b.Offset(1, 1);
        int cx = b.X + b.Width / 2, cy = b.Y + b.Height / 2;
        for (int i = 0; i < 4; i++)
        {
            int w = 1 + 2 * i;
            switch (dir)
            {
                case ScrollArrowDir.Up: r.Fill(cx - i, cy + 1 - i, w, 1, Theme.Text); break;
                case ScrollArrowDir.Down: r.Fill(cx - i, cy - 2 + i, w, 1, Theme.Text); break;
                case ScrollArrowDir.Left: r.Fill(cx + 1 - i, cy - i, 1, w, Theme.Text); break;
                case ScrollArrowDir.Right: r.Fill(cx - 2 + i, cy - i, 1, w, Theme.Text); break;
            }
        }
    }

    // Win 3.1 system box: a black-outlined white slot with a 1px bottom/right drop shadow.
    private static void DrawSysGlyph(Win31Renderer r, Rectangle b)
    {
        int w = 12, h = 3;
        int x = b.X + (b.Width - w) / 2, y = b.Y + (b.Height - h) / 2;
        r.Fill(x + 1, y + h, w, 1, Theme.MidEdge);
        r.Fill(x + w, y + 1, 1, h, Theme.MidEdge);
        r.Fill(x, y, w, h, Theme.Text);
        r.Fill(x + 1, y + 1, w - 2, 1, Theme.WindowBg);
    }

    private static void DrawMinGlyph(Win31Renderer r, Rectangle b)
    {
        int cx = b.X + b.Width / 2, cy = b.Y + b.Height / 2 + 1;
        for (int i = 0; i < 4; i++) r.Fill(cx - 3 + i, cy - 3 + i, 7 - 2 * i, 1, Theme.Text);
    }

    private static void DrawMaxGlyph(Win31Renderer r, Rectangle b)
    {
        int cx = b.X + b.Width / 2, cy = b.Y + b.Height / 2 - 2;
        for (int i = 0; i < 4; i++) r.Fill(cx - i, cy + i, 1 + 2 * i, 1, Theme.Text);
    }

    // Restore glyph: two overlapping little frames (shown when a window is maximized).
    private static void DrawRestoreGlyph(Win31Renderer r, Rectangle b)
    {
        int mx = b.X + b.Width / 2, my = b.Y + b.Height / 2;
        r.FrameRect(new Rectangle(mx - 1, my - 3, 5, 5), Theme.Text);
        r.FrameRect(new Rectangle(mx - 3, my - 1, 5, 5), Theme.Face);
        r.FrameRect(new Rectangle(mx - 3, my - 1, 5, 5), Theme.Text);
    }
}

/// <summary>Which caption button (drives the glyph the skin draws).</summary>
public enum CaptionButton { System, Minimize, Maximize, Restore }

/// <summary>Scroll-arrow direction (drives the triangle the skin draws).</summary>
public enum ScrollArrowDir { Up, Down, Left, Right }
