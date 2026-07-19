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
}
