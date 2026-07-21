using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>
/// The root of the widget tree. Owns the single <see cref="PopupLayer"/> (always
/// drawn last / hit-tested first) and tracks the single focused widget. Draw order:
/// content children first, then the popup layer on top.
/// </summary>
public sealed class RootDesktop : Widget
{
    public PopupLayer Popup { get; } = new();
    public Widget? Focused { get; private set; }

    /// <summary>A widget mid-drag (e.g. resizing a window) can claim the cursor so its shape stays put
    /// even if the pointer briefly leaves it. Set on drag start, cleared on release. Null = resolve by
    /// hit-test. The window's cursor resolve consults the captured widget first.</summary>
    public Widget? CursorCapture;

    public RootDesktop()
    {
        Popup.Parent = this;
    }

    public void SetFocus(Widget? w) => Focused = w;

    /// <summary>Any widget can call <c>Root()?.RequestRedraw()</c> when it changes what's on
    /// screen (animation, streaming content) to keep the idle-throttled render loop awake.</summary>
    public bool RedrawRequested { get; private set; }
    public void RequestRedraw() => RedrawRequested = true;
    public void ClearRedraw() => RedrawRequested = false;

    public override void Layout()
    {
        base.Layout();
        Popup.Bounds = Bounds;
    }

    public override void Update(InputState input, GameTime t)
    {
        // The content tree always updates; the widget that owns the open popup
        // (e.g. MenuBar) drives the popup itself. Other widgets consult
        // Popup.BlocksInput to know the mouse is captured and ignore it.
        Popup.BeginFrame(); // a popup closed last frame stops swallowing input now
        base.Update(input, t);

        // Route typed characters + keys to the focused widget, unless a popup owns input.
        if (!Popup.IsOpen && Focused is { WantsKeyboard: true })
        {
            foreach (char c in input.TypedChars) Focused.OnChar(c);
            Focused.OnKey(input);
        }
    }

    public override void Draw(Win31Renderer r)
    {
        base.Draw(r);
        Popup.Draw(r);
    }

    public override Widget? HitTest(Point p)
    {
        if (Popup.IsOpen)
        {
            var hit = Popup.HitTest(p);
            if (hit != null) return hit;
        }
        return base.HitTest(p);
    }
}
