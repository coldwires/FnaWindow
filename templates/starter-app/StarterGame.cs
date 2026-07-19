using System.Collections.Generic;
using FnaWindow;

/// <summary>
/// A whole app. Subclass WindowGame, override BuildUi, fill the frame with a menu, some
/// content, and a status bar. The base class handles the borderless window, native
/// drag/resize/snap, the render loop, input, and fonts.
/// </summary>
public sealed class StarterGame : WindowGame
{
    public StarterGame() : base("My App", 900, 600) { }

    protected override void BuildUi(WindowFrame frame, BitmapFont uiFont)
    {
        frame.SetStatus(new StatusBar { Message = "Ready" });
        frame.SetContent(new Welcome());

        var file = new List<MenuItemDef> { MenuItemDef.Item("E&xit", null, Exit) };
        frame.SetMenu(new MenuBar(new List<TopMenu> { new("&File", file) })
        {
            MeasureTitleWidth = uiFont.MeasureWidth,
            MeasureItemWidth  = uiFont.MeasureWidth,
        });
    }
}

/// <summary>The content area - any Widget. Draw into Bounds with the Win31Renderer.</summary>
public sealed class Welcome : Widget
{
    public override void Draw(Win31Renderer r)
    {
        r.DrawPanel(Bounds, BevelStyle.SunkenThick, Theme.WindowBg);
        r.UiFont.Draw(r.Sb, "Hello from your own app.", Bounds.X + 16, Bounds.Y + 16, Theme.Text);
    }
}
