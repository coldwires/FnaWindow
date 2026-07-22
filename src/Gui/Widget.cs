using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>
/// Retained-mode widget base. Bounds are absolute screen coords,
/// recomputed in <see cref="Layout"/>. Concrete widgets poll <see cref="InputState"/>
/// in <see cref="Update"/> for hover/click; keyboard focus routes through OnChar/OnKey.
/// </summary>
public abstract class Widget
{
    public Rectangle Bounds;
    public Widget? Parent;
    public List<Widget> Children = new();
    public bool Visible = true;
    public bool Enabled = true;

    public void Add(Widget child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    public virtual void Layout()
    {
        foreach (var c in Children) c.Layout();
    }

    public virtual void Update(InputState input, GameTime t)
    {
        // Snapshot: a child's Update runs input handlers, and handlers legitimately restructure the
        // tree (a menu item that opens a panel, a close box that removes a window). Enumerating the
        // live list would throw the moment one did. Taking the children as they were when the pass
        // began also gives a clean rule: a widget added or removed during a frame takes effect on the
        // next one. (Costs nothing for a leaf - List.ToArray returns the shared empty array.)
        foreach (var c in Children.ToArray())
            if (c.Visible) c.Update(input, t);
    }

    public virtual void Draw(Win31Renderer r)
    {
        foreach (var c in Children)
            if (c.Visible) c.Draw(r);
    }

    /// <summary>Topmost visible child containing <paramref name="p"/>; else this if it contains p; else null.</summary>
    public virtual Widget? HitTest(Point p)
    {
        if (!Visible || !Bounds.Contains(p)) return null;
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            var hit = Children[i].HitTest(p);
            if (hit != null) return hit;
        }
        return this;
    }

    /// <summary>The cursor key this widget wants when the pointer is at <paramref name="p"/> (already
    /// hit-tested into it), or null to defer to its parent / the window default. Lets a widget show a
    /// region cursor (e.g. an I-beam over editor text) without any global wiring.</summary>
    public virtual string? CursorKey(Point p) => null;

    // Focus + input events (used from M2 onward; menus handle keys in Update for M1).
    public virtual bool WantsKeyboard => false;
    public virtual void OnKey(InputState input) { }
    public virtual void OnChar(char c) { }

    /// <summary>
    /// True when an open popup owns the input AND this widget is not part of that popup, which is
    /// the condition every interactive widget wants before it looks at the mouse.
    ///
    /// The "not part of it" half matters: a plain <c>Popup.BlocksInput</c> check is also true for
    /// the popup's own contents, so a shared widget used inside a modal would go dead exactly when
    /// the modal is up. That is why <see cref="RetroFileDialog"/> used to hand-draw its own
    /// scrollbar instead of using <see cref="ScrollBar"/>. Walking up to the popup fixes it for
    /// every widget at once.
    ///
    /// During the frame a popup closes in, <c>Current</c> is already null while
    /// <c>BlocksInput</c> is still true, so this reports blocked for everything - which is the
    /// intent: the click that chose a menu item must not also land on what was underneath.
    /// </summary>
    protected bool InputBlocked
    {
        get
        {
            var root = Root();
            if (root == null || !root.Popup.BlocksInput) return false;

            var popup = root.Popup.Current;
            if (popup == null) return true;
            for (Widget? w = this; w != null; w = w.Parent)
                if (ReferenceEquals(w, popup)) return false;
            return true;
        }
    }

    /// <summary>Walks up to the containing <see cref="RootDesktop"/>, if any.</summary>
    public RootDesktop? Root()
    {
        Widget? w = this;
        while (w != null)
        {
            if (w is RootDesktop rd) return rd;
            w = w.Parent;
        }
        return null;
    }
}
