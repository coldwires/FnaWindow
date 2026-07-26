using System;
using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>How a <see cref="TitledPanel"/> frames its body.</summary>
public enum PanelStyle
{
    /// <summary>Caption strip across the top edge, flat body underneath. The panel is a container:
    /// child widgets or the subclass's own drawing fill <see cref="TitledPanel.Body"/>, and anything
    /// that wants a well border draws its own.</summary>
    Flush,

    /// <summary>Raised outer bevel, caption inset inside it, and a sunken well around the body. The
    /// panel is a self-contained box, which is what a dockable tool panel wants.</summary>
    Welled,
}

/// <summary>
/// A titled Win 3.1 panel: a navy caption strip over a body, matching the window's own title bar so
/// docks and tool panels read as part of the same window rather than as loose boxes.
///
/// This is the single home for that chrome. It exists because three apps wanted the same widget:
/// two independent copies had already been written for docks and tool panels and were beginning to
/// drift on caption height and caption font; a third copy would have made that permanent. Change
/// how a panel is framed here and every consumer moves together.
///
/// A subclass supplies <see cref="Caption"/> (or overrides <see cref="CaptionText"/>), then either
/// lays child widgets into <see cref="Body"/> from <see cref="LayoutBody"/>, or draws into it from
/// <see cref="DrawBody"/>, or both.
/// </summary>
public abstract class TitledPanel : Widget
{
    /// <summary>Caption text. A subclass whose title is computed should override
    /// <see cref="CaptionText"/> instead of writing to this.</summary>
    public string Caption = "";

    /// <summary>What the caption strip actually draws. Defaults to <see cref="Caption"/>.</summary>
    protected virtual string CaptionText => Caption;

    /// <summary>False when something else already supplies the frame and caption - a floating window
    /// hosting this panel as its content. The panel then draws only its body. One panel class, two
    /// presentations; the presenter sets this.</summary>
    public bool ShowCaption = true;

    /// <summary>How the body is framed. See <see cref="PanelStyle"/>.</summary>
    public PanelStyle Style = PanelStyle.Flush;

    /// <summary>Caption strip height. Defaults to the skin's MDI child caption height so a panel
    /// caption and a child window caption are the same size in the same window.</summary>
    protected virtual int CaptionHeight => Theme.MdiChildTitleHeight;

    private int CaptionStrip => ShowCaption ? Math.Min(CaptionHeight, Bounds.Height) : 0;

    /// <summary>The body area including any well border. Equal to <see cref="Body"/> in
    /// <see cref="PanelStyle.Flush"/>. Computed from <see cref="Widget.Bounds"/> on every read rather
    /// than cached in Layout, so it is never stale for an input handler that runs after a resize.</summary>
    protected Rectangle Well
    {
        get
        {
            int ch = CaptionStrip;
            if (Style == PanelStyle.Welled)
                return ShowCaption
                    ? new Rectangle(Bounds.X + 2, Bounds.Y + ch,
                        Math.Max(0, Bounds.Width - 4), Math.Max(0, Bounds.Height - ch - 2))
                    : Bounds;
            return new Rectangle(Bounds.X, Bounds.Y + ch, Bounds.Width, Math.Max(0, Bounds.Height - ch));
        }
    }

    /// <summary>The content rectangle a subclass should fill: under the caption, and inside the well
    /// border when there is one.</summary>
    protected Rectangle Body => Style == PanelStyle.Welled ? Win31Renderer.Inset(Well, 2) : Well;

    public override void Layout()
    {
        LayoutBody();
        base.Layout();
    }

    /// <summary>Place any child widgets into <see cref="Body"/>. Called before children lay out.</summary>
    protected virtual void LayoutBody() { }

    public override void Draw(Win31Renderer r)
    {
        if (ShowCaption)
        {
            if (Style == PanelStyle.Welled) r.DrawPanel(Bounds, BevelStyle.RaisedThick, Theme.Face);

            int ch = Math.Min(CaptionHeight, Bounds.Height);
            var cap = Style == PanelStyle.Welled
                ? new Rectangle(Bounds.X + 2, Bounds.Y + 2, Math.Max(0, Bounds.Width - 4), Math.Max(0, ch - 2))
                : new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, ch);

            r.Fill(cap, Theme.TitleActive);
            var font = r.UiBoldFont;
            r.DrawText(font, font.Fit(CaptionText, Math.Max(0, cap.Width - 8)),
                cap.X + 4, cap.Y + (cap.Height - font.LineHeight) / 2, Theme.TitleText);
        }

        if (Style == PanelStyle.Welled) r.DrawPanel(Well, BevelStyle.SunkenThick, Theme.WindowBg);

        DrawBody(r);
        base.Draw(r);
    }

    /// <summary>Draw the panel's own contents into <see cref="Body"/>. Children draw after this.</summary>
    protected virtual void DrawBody(Win31Renderer r) { }
}
