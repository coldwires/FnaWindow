using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>
/// One place that lays out and draws a window caption (title bar), used by EVERY window - the
/// top-level <see cref="TitleBar"/> and the app's MDI child windows alike - so they are drawn
/// identically. Button size, positions, the centered title, and the three caption buttons live here
/// once; a change here changes every window. Input/behavior stays with each window; this is drawing.
/// </summary>
public static class WindowCaption
{
    /// <summary>The square caption button size (matches the 18x18 button art 1:1).</summary>
    public const int ButtonSize = 18;

    /// <summary>System box on the left; minimize then maximize on the right; all vertically centered.
    /// Each button's size comes from the skin, so authored art of any size lays out un-stretched.</summary>
    public static void LayoutButtons(Rectangle title, out Rectangle sys, out Rectangle min, out Rectangle max)
        => LayoutButtons(title, out sys, out min, out max, out _);

    public static void LayoutButtons(Rectangle title, out Rectangle sys, out Rectangle min, out Rectangle max, out Rectangle close)
    {
        var skin = ThemeManager.Skin;
        int ss = skin.CaptionButtonSize(CaptionButton.System);
        int ms = skin.CaptionButtonSize(CaptionButton.Minimize);
        int xs = skin.CaptionButtonSize(CaptionButton.Maximize);
        int Cy(int s) => title.Y + (title.Height - s) / 2;
        sys = skin.ShowSystemButton
            ? new Rectangle(title.X, Cy(ss) - 1, ss, ss) // system box nudged left + 1px up
            : Rectangle.Empty;                           // no slot: no hit area, no draw
        int cs = skin.CaptionButtonSize(CaptionButton.Close);
        int gap = skin.CaptionButtonGap;
        int edge = title.Right - skin.CaptionRightPad;
        close = skin.ShowCloseButton ? new Rectangle(edge - cs, Cy(cs), cs, cs) : Rectangle.Empty;
        int right = skin.ShowCloseButton ? close.X - gap : edge;
        max = new Rectangle(right - xs, Cy(xs), xs, xs);
        min = new Rectangle(max.X - gap - ms, Cy(ms), ms, ms);
    }

    /// <summary>Which caption button a point is over: 0 sys, 1 min, 2 max, or -1 none. Shared so every
    /// window hit-tests its caption buttons the same way.</summary>
    public static int HitButton(Point p, Rectangle sys, Rectangle min, Rectangle max)
        => HitButton(p, sys, min, max, Rectangle.Empty);

    public static int HitButton(Point p, Rectangle sys, Rectangle min, Rectangle max, Rectangle close)
        => sys.Contains(p) ? 0 : min.Contains(p) ? 1 : max.Contains(p) ? 2 : close.Contains(p) ? 3 : -1;

    /// <summary>
    /// Fill the caption, draw the centered title in <paramref name="font"/>, and the three buttons via
    /// the skin. <paramref name="pressed"/>: -1 none, 0 sys, 1 min, 2 max. When <paramref name="maximized"/>
    /// the maximize button shows the restore glyph.
    /// </summary>
    public static void Draw(Win31Renderer r, Rectangle title, string text, Color bg, Color textColor,
        Rectangle sys, Rectangle min, Rectangle max, int pressed, bool maximized, BitmapFont font)
        => Draw(r, title, text, bg, textColor, sys, min, max, Rectangle.Empty, pressed, maximized, font);

    public static void Draw(Win31Renderer r, Rectangle title, string text, Color bg, Color textColor,
        Rectangle sys, Rectangle min, Rectangle max, Rectangle close, int pressed, bool maximized, BitmapFont font)
    {
        var skin = ThemeManager.Skin;
        skin.DrawCaptionFill(r, title, bg);

        int tx = skin.CenterCaptionText
            ? title.X + (title.Width - font.MeasureWidth(text)) / 2
            : title.X + 8;
        int ty = title.Y + (title.Height - font.LineHeight) / 2;
        r.DrawText(font, text, tx, ty, textColor);

        if (sys.Width > 0) skin.DrawCaptionButton(r, sys, CaptionButton.System, pressed == 0);
        skin.DrawCaptionButton(r, min, CaptionButton.Minimize, pressed == 1);
        skin.DrawCaptionButton(r, max, maximized ? CaptionButton.Restore : CaptionButton.Maximize, pressed == 2);
        if (close.Width > 0) skin.DrawCaptionButton(r, close, CaptionButton.Close, pressed == 3);
    }
}
