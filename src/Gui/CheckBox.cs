using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>
/// A Win 3.1 check box: a small sunken well with a tick when set, and a label beside it. Clicking
/// either the box or the label toggles, on release, matching <see cref="PushButton"/>.
/// <para>Like PushButton this is a value the owner lays out and pumps, not a <see cref="Widget"/>,
/// so a dialog can arrange a column of them without a child tree.</para>
/// </summary>
public sealed class CheckBox
{
    public const int BoxSize = 13;

    /// <summary>The whole clickable row: box plus label.</summary>
    public Rectangle Bounds;
    public string Label;
    public bool Checked;
    public bool Enabled = true;

    private bool _armed;

    public CheckBox(string label = "", bool isChecked = false)
    {
        Label = label;
        Checked = isChecked;
    }

    /// <summary>Pump it. Returns true on the frame it toggles, with <see cref="Checked"/> updated.</summary>
    public bool Update(InputState input)
    {
        if (!Enabled) { _armed = false; return false; }

        bool over = Bounds.Contains(input.Mouse);
        if (input.LeftPressed && over) _armed = true;

        if (input.LeftReleased)
        {
            bool toggled = _armed && over;
            _armed = false;
            if (toggled) Checked = !Checked;
            return toggled;
        }
        return false;
    }

    public void Draw(Win31Renderer r)
    {
        int by = Bounds.Y + (Bounds.Height - BoxSize) / 2;
        var box = new Rectangle(Bounds.X, by, BoxSize, BoxSize);
        r.DrawPanel(box, BevelStyle.SunkenThin, Enabled ? Theme.WindowBg : Theme.Face);

        if (Checked)
        {
            // A chunky tick, drawn from two strokes so it reads at this size.
            var ink = Enabled ? Theme.Text : Theme.TextDisabled;
            int x = box.X + 3, y = box.Y + 6;
            for (int i = 0; i < 3; i++) r.Fill(x + i, y + i, 2, 2, ink);       // down-right
            for (int i = 0; i < 4; i++) r.Fill(x + 4 + i, y + 2 - i, 2, 2, ink); // up-right
        }

        if (Label.Length == 0) return;
        r.DrawText(r.UiFont, Label, box.Right + 6,
            Bounds.Y + (Bounds.Height - r.UiFont.LineHeight) / 2,
            Enabled ? Theme.Text : Theme.TextDisabled);
    }
}
