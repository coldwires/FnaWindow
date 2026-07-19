using Microsoft.Xna.Framework;
using FnaWindow;

namespace FnaWindow.Demo;

/// <summary>
/// The demo's content area: a sunken white well with a welcome message. A trivial example of
/// a custom <see cref="Widget"/> - override <see cref="Draw"/> (and <see cref="Update"/> for
/// interaction) and add it to the frame with <c>frame.SetContent(...)</c>.
/// </summary>
public sealed class DemoContent : Widget
{
    private static readonly string[] Lines =
    {
        "FnaWindow",
        "",
        "A borderless, themeable, Windows 3.1-styled window engine on FNA.",
        "",
        "Try it:",
        "  -  Drag the title bar to move the window (Aero-snap works).",
        "  -  Drag any edge or the bottom-right grip to resize.",
        "  -  The title-bar buttons maximize, minimize, and close.",
        "  -  Open the Themes menu to reskin the whole UI at runtime.",
        "",
        "Subclass WindowGame and override BuildUi() to make your own app.",
    };

    public override void Draw(Win31Renderer r)
    {
        r.DrawPanel(Bounds, BevelStyle.SunkenThick, Theme.WindowBg);
        int x = Bounds.X + 16, y = Bounds.Y + 16;
        int lh = r.UiFont.LineHeight + 4;
        for (int i = 0; i < Lines.Length; i++)
        {
            var font = i == 0 ? r.UiBoldFont : r.UiFont;
            var color = i == 0 ? Theme.TitleActive : Theme.Text;
            font.Draw(r.Sb, Lines[i], x, y + i * lh, color);
        }
    }
}
