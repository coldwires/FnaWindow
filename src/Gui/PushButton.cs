using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>
/// A Win 3.1 push button with the standard press-and-release behaviour: pressing shows it sunken,
/// releasing over it fires, releasing anywhere else cancels. This is how the title-bar and toolbar
/// buttons already behave, and dialogs should match rather than firing on the press.
/// <para>Deliberately not a <see cref="Widget"/>: dialogs lay their own buttons out and draw them in
/// order, so this is a small value the owner keeps and pumps from its own Update/Draw.</para>
/// </summary>
public sealed class PushButton
{
    public Rectangle Bounds;
    public string Label;

    /// <summary>Drawn sunken right now: the press started here and the mouse is still on it.</summary>
    public bool Pressed { get; private set; }

    /// <summary>The cursor is over the button. Skins that react to hover read it via Draw.</summary>
    public bool Hover { get; private set; }

    private bool _armed;   // the press began on this button

    public PushButton(string label = "") => Label = label;

    /// <summary>
    /// Pump the button. Returns true on the single frame it fires, which is the release over it.
    /// Dragging off while held lifts it back up and coming back presses it again, and releasing off
    /// it cancels, which is what every other Win 3.1 button does.
    /// </summary>
    public bool Update(InputState input)
    {
        bool over = Bounds.Contains(input.Mouse);
        Hover = over;

        if (input.LeftPressed && over) _armed = true;

        if (input.LeftReleased)
        {
            bool fired = _armed && over;
            _armed = false;
            Pressed = false;
            return fired;
        }

        Pressed = _armed && input.LeftDown && over;
        return false;
    }

    /// <summary>Cancel any held state (the owner closing, losing input, and so on).</summary>
    public void Reset() { _armed = false; Pressed = false; Hover = false; }

    public void Draw(Win31Renderer r)
    {
        ThemeManager.Skin.DrawButton(r, Bounds, Pressed, Hover);
        if (Label.Length == 0) return;

        int off = Pressed ? 1 : 0;   // label rides down with the button
        int tw = r.UiFont.MeasureWidth(Label);
        r.DrawText(r.UiFont, Label,
            Bounds.X + (Bounds.Width - tw) / 2 + off,
            Bounds.Y + (Bounds.Height - r.UiFont.LineHeight) / 2 + off,
            Theme.Text);
    }
}
