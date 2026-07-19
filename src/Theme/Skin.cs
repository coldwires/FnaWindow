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
    public virtual int TitleBarHeight => 20;
    public virtual int MenuBarHeight => 19;
    public virtual int MenuItemHeight => 17;
    public virtual int ToolbarHeight => 26;
    public virtual int ToolButtonSize => 22;
    public virtual int StatusBarHeight => 20;
    public virtual int MdiChildTitleHeight => 18;
    public virtual int ScrollBarThickness => 16;
}
