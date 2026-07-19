using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>
/// A passive top-most container for one transient popup (menu or completion list),
/// drawn above everything and hit-tested first. It does not update its
/// content - the owner that opened the popup drives its interaction. Other widgets
/// treat <see cref="IsOpen"/> as an input-capture signal and ignore mouse while set.
/// </summary>
public sealed class PopupLayer : Widget
{
    public Widget? Current { get; private set; }
    public bool IsOpen => Current != null;

    public void Open(Widget popup)
    {
        Current = popup;
        popup.Parent = this;
        popup.Layout();
    }

    public void Close() => Current = null;

    public override void Update(InputState input, GameTime t) { /* passive */ }

    public override void Draw(Win31Renderer r) => Current?.Draw(r);

    public override Widget? HitTest(Point p) => Current?.HitTest(p);
}
