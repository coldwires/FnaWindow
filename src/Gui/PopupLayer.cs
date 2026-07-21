using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>
/// A passive top-most container for one transient popup (menu or completion list),
/// drawn above everything and hit-tested first. It does not update its
/// content - the owner that opened the popup drives its interaction. Other widgets
/// treat <see cref="BlocksInput"/> as an input-capture signal and ignore mouse while set.
/// </summary>
public sealed class PopupLayer : Widget
{
    public Widget? Current { get; private set; }
    public bool IsOpen => Current != null;

    private bool _justClosed;

    /// <summary>
    /// True while the mouse belongs to the popup, INCLUDING the rest of the frame in which it closed.
    /// A menu closes partway through a frame, on the click that chose an item; widgets updated after
    /// it would otherwise see that same click as their own and act on it, so choosing a menu item
    /// also clicked whatever sat underneath the menu. Gate mouse handling on this, not on IsOpen.
    /// </summary>
    public bool BlocksInput => IsOpen || _justClosed;

    public void Open(Widget popup)
    {
        Current = popup;
        _justClosed = false;
        popup.Parent = this;
        popup.Layout();
    }

    public void Close()
    {
        if (Current != null) _justClosed = true;
        Current = null;
    }

    /// <summary>Called once per frame by <see cref="RootDesktop"/> before the tree updates, so a close
    /// keeps swallowing the mouse only until the end of the frame it happened in.</summary>
    internal void BeginFrame() => _justClosed = false;

    public override void Update(InputState input, GameTime t) { /* passive */ }

    public override void Draw(Win31Renderer r) => Current?.Draw(r);

    public override Widget? HitTest(Point p) => Current?.HitTest(p);
}
