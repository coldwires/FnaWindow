using System;
using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>
/// The default skin: the classic Windows 3.1 raised/sunken bevels. This is exactly the drawing that
/// used to live in <see cref="Win31Renderer"/>, moved behind the <see cref="Skin"/> seam so it can
/// be swapped without touching any widget. Convention: draw the shadow L (bottom/right) first, then
/// the highlight L (top/left), so the highlight wins the contested corners. (See win31-theme.md.)
/// </summary>
public sealed class Win31Skin : Skin
{
    public override string Name => "Windows 3.1";

    public override int Thickness(BevelStyle style)
        => style is BevelStyle.RaisedThick or BevelStyle.SunkenThick ? 2 : 1;

    public override void DrawPanel(Win31Renderer r, Rectangle rect, BevelStyle style, Color bg)
    {
        int t = Thickness(style);
        r.Fill(new Rectangle(rect.X + t, rect.Y + t, Math.Max(0, rect.Width - 2 * t), Math.Max(0, rect.Height - 2 * t)), bg);
        DrawBevel(r, rect, style);
    }

    public override void DrawBevel(Win31Renderer r, Rectangle rect, BevelStyle style)
    {
        switch (style)
        {
            case BevelStyle.RaisedThin:
                Edges(r, rect, Theme.LightEdge, Theme.DarkEdge);
                break;
            case BevelStyle.SunkenThin: // inverse of RaisedThin
                Edges(r, rect, Theme.MidEdge, Theme.LightEdge);
                break;
            case BevelStyle.RaisedThick:
                Edges(r, rect, Theme.LightEdge, Theme.DarkEdge);          // outer
                Edges(r, Win31Renderer.Inset(rect, 1), Theme.Face, Theme.MidEdge); // inner
                break;
            case BevelStyle.SunkenThick:
                Edges(r, rect, Theme.MidEdge, Theme.LightEdge);           // outer
                Edges(r, Win31Renderer.Inset(rect, 1), Theme.DarkEdge, Theme.Face); // inner
                break;
        }
    }

    // 1px ring: shadow L (bottom+right) first, highlight L (top+left) second (wins the corners).
    private static void Edges(Win31Renderer r, Rectangle rect, Color tl, Color br)
    {
        r.HLine(rect.Left, rect.Bottom - 1, rect.Width, br); // bottom
        r.VLine(rect.Right - 1, rect.Top, rect.Height, br);  // right
        r.HLine(rect.Left, rect.Top, rect.Width, tl);        // top
        r.VLine(rect.Left, rect.Top, rect.Height, tl);       // left
    }
}
