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
        foreach (var c in Children)
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
