using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FnaWindow;

/// <summary>
/// Asset-driven Windows 3.1 skin: the raised/sunken bevels are authored 9-slice PNGs under
/// Content/skins/win31/, so the chrome is editable as art instead of procedural code. Any bevel
/// whose PNG is missing (or is drawn onto a rect too small to slice) falls back to the exact
/// procedural Win31 drawing, so a clean checkout still renders. Presentation only, no IDE logic -
/// it can move into a shared skins library later (same plan as the JRPG skins, see spec.md).
/// </summary>
public sealed class Win31PngSkin : Skin
{
    // The engine's procedural skin, used verbatim for any bevel we have no art for.
    private static readonly Win31Skin Proc = new();

    public override string Name => "Windows 3.1";

    public override bool DrawMenuSeparators => true; // Win 3.1 look: 1px black rules around the menu bar

    public override int Thickness(BevelStyle style)
    {
        // With sunken panels off, a SunkenThick well has no border, so it insets content by 0 -
        // elements push outward to fill. SunkenThin (pressed buttons, status cells) is unaffected.
        if (style == BevelStyle.SunkenThick && !Win31Png.DrawSunkenPanels) return 0;
        return style is BevelStyle.RaisedThick or BevelStyle.SunkenThick ? 2 : 1;
    }

    // Sizes come from the art, so a resized PNG is canonical everywhere (layout + click area), never
    // stretched. Fall back to the classic metric when the PNG is absent.
    public override int CaptionButtonSize(CaptionButton kind)
        => Win31Png.CaptionTex(kind, false)?.Width ?? base.CaptionButtonSize(kind);

    public override int ScrollBarThickness
        => Win31Png.ScrollTex(ScrollArrowDir.Up, false)?.Width ?? base.ScrollBarThickness;

    public override void DrawPanel(Win31Renderer r, Rectangle rect, BevelStyle style, Color bg)
    {
        int t = Thickness(style);
        r.Fill(new Rectangle(rect.X + t, rect.Y + t,
            Math.Max(0, rect.Width - 2 * t), Math.Max(0, rect.Height - 2 * t)), bg);
        DrawBevel(r, rect, style);
    }

    public override void DrawBevel(Win31Renderer r, Rectangle rect, BevelStyle style)
    {
        if (style == BevelStyle.SunkenThick && !Win31Png.DrawSunkenPanels) return; // no border when off
        var tex = Win31Png.Bevel(style);
        if (tex != null && rect.Width >= tex.Width && rect.Height >= tex.Height)
            Win31Png.NineSlice(r.Sb, tex, rect);
        else
            Proc.DrawBevel(r, rect, style); // missing art or too-small rect -> procedural
    }

    public override void DrawCaptionButton(Win31Renderer r, Rectangle rect, CaptionButton kind, bool pressed)
    {
        var tex = Win31Png.CaptionTex(kind, pressed);
        // Blit at the PNG's native size (anchored to the button's top-left) so resized art is not
        // squished to fit the layout rect. The rect still defines the click area.
        if (tex != null) r.Sb.Draw(tex, new Rectangle(rect.X, rect.Y, tex.Width, tex.Height), Color.White);
        else base.DrawCaptionButton(r, rect, kind, pressed);
    }

    public override void DrawScrollButton(Win31Renderer r, Rectangle rect, ScrollArrowDir dir, bool pressed)
    {
        var tex = Win31Png.ScrollTex(dir, pressed);
        // Native size (the scrollbar thickness already follows this art), anchored to the button rect.
        if (tex != null) r.Sb.Draw(tex, new Rectangle(rect.X, rect.Y, tex.Width, tex.Height), Color.White);
        else base.DrawScrollButton(r, rect, dir, pressed);
    }

    public override void DrawScrollCorner(Win31Renderer r, Rectangle rect)
    {
        var tex = Win31Png.ScrollCornerTex();
        if (tex != null) r.Sb.Draw(tex, new Rectangle(rect.X, rect.Y, tex.Width, tex.Height), Color.White);
        else base.DrawScrollCorner(r, rect);
    }

    public override void DrawMenuSeparator(Win31Renderer r, Rectangle rect)
    {
        var tex = Win31Png.MenuSepTex();
        if (tex == null) { base.DrawMenuSeparator(r, rect); return; }
        // Horizontal 3-slice strip at native height, vertically centered, spanning the FULL row width
        // (no side inset) so it reaches edge to edge.
        int y = rect.Y + (rect.Height - tex.Height) / 2;
        Win31Png.HStrip(r.Sb, tex, new Rectangle(rect.X, y, rect.Width, tex.Height));
    }

    public override void DrawWindowFrame(Win31Renderer r, Rectangle rect)
    {
        var tex = Win31Png.FrameTex();
        if (tex != null) Win31Png.NineSlice(r.Sb, tex, rect);
        else Proc.DrawWindowFrame(r, rect); // no art -> the procedural flat Win 3.1 frame
    }

    public override void DrawButton(Win31Renderer r, Rectangle rect, bool pressed)
    {
        var tex = Win31Png.ButtonTex(pressed);
        if (tex == null) { base.DrawButton(r, rect, pressed); return; } // no art (e.g. no pressed art) -> procedural
        // The button art is an asymmetric bevel: 1px highlight (top/left), thicker shadow (bottom/right).
        // A pressed variant inverts that, so the thick side is top/left.
        if (pressed) Win31Png.NineSliceFull(r.Sb, tex, Win31Png.BtnThick, Win31Png.BtnThick, Win31Png.BtnThin, Win31Png.BtnThin, rect);
        else Win31Png.NineSliceFull(r.Sb, tex, Win31Png.BtnThin, Win31Png.BtnThin, Win31Png.BtnThick, Win31Png.BtnThick, rect);
    }

    public override void DrawMenuCheck(Win31Renderer r, int cx, int cy, Color color)
    {
        var tex = Win31Png.MenuCheckTex();
        if (tex != null) r.Sb.Draw(tex, new Vector2(cx, cy - 2), color); // white art, tinted by row color
        else base.DrawMenuCheck(r, cx, cy, color);
    }
}

/// <summary>Loads the Win31 skin art once and applies the asset-driven skin. Mirrors JrpgBlue.</summary>
public static class Win31Png
{
    /// <summary>When false, SunkenThick wells draw no border and inset content by 0 (elements push
    /// outward). Toggled from the Theme menu. Off by default.</summary>
    public static bool DrawSunkenPanels;

    private static Texture2D? _raisedThin, _sunkenThin, _raisedThick, _sunkenThick, _windowFrame, _menuCheck, _scrollCorner, _menuSep;
    private static Texture2D? _button, _buttonDown;

    // Asymmetric slice margins for the button art: thin highlight side vs thick shadow side.
    public const int BtnThin = 1, BtnThick = 3;
    private static readonly Texture2D?[] _caption = new Texture2D?[12]; // [kind*2 + pressed], kind = CaptionButton
    private static readonly Texture2D?[] _scroll = new Texture2D?[8];  // [dir*2 + pressed], dir = ScrollArrowDir
    private static bool _loaded;

    /// <summary>Load the skin PNGs once. Call at content-load with the device.</summary>
    public static void LoadAssets(GraphicsDevice gd)
    {
        if (_loaded) return;
        _loaded = true;
        string dir = Path.Combine(AppContext.BaseDirectory, "Content", "skins", "win31");
        Texture2D? L(string f) => SkinArt.TryLoad(gd, Path.Combine(dir, f));

        _raisedThin = L("bevel-raised-thin.png");
        _sunkenThin = L("bevel-sunken-thin.png");
        _raisedThick = L("panel-raised.png");
        _sunkenThick = L("panel-sunken.png");
        _windowFrame = L("frame-window.png");
        _menuCheck = L("menu-check.png");
        _scrollCorner = L("scroll-corner.png");
        _menuSep = L("menu-sep.png");
        _button = L("btn-tool.png");
        _buttonDown = L("btn-tool-down.png");

        string[] caps = { "btn-sys", "btn-min", "btn-max", "btn-restore" }; // order = CaptionButton enum
        for (int k = 0; k < caps.Length; k++)
        {
            _caption[k * 2] = L(caps[k] + ".png");
            _caption[k * 2 + 1] = L(caps[k] + "-down.png");
        }
        string[] dirs = { "up", "down", "left", "right" }; // order = ScrollArrowDir enum
        for (int d = 0; d < dirs.Length; d++)
        {
            _scroll[d * 2] = L("scroll-" + dirs[d] + ".png");
            _scroll[d * 2 + 1] = L("scroll-" + dirs[d] + "-down.png");
        }
    }

    /// <summary>The 9-slice ring texture for a bevel style (null -> procedural fallback).</summary>
    public static Texture2D? Bevel(BevelStyle style) => style switch
    {
        BevelStyle.RaisedThin => _raisedThin,
        BevelStyle.SunkenThin => _sunkenThin,
        BevelStyle.RaisedThick => _raisedThick,
        BevelStyle.SunkenThick => _sunkenThick,
        _ => null,
    };

    /// <summary>Whole-button art for a caption button (null -> procedural fallback).</summary>
    public static Texture2D? CaptionTex(CaptionButton kind, bool pressed) => _caption[(int)kind * 2 + (pressed ? 1 : 0)];

    /// <summary>Whole-button art for a scrollbar arrow (null -> procedural fallback).</summary>
    public static Texture2D? ScrollTex(ScrollArrowDir dir, bool pressed) => _scroll[(int)dir * 2 + (pressed ? 1 : 0)];

    /// <summary>The 9-slice window-frame ring, or null -> procedural flat frame.</summary>
    public static Texture2D? FrameTex() => _windowFrame;

    /// <summary>The menu checkmark art (white; tinted by the row color), or null -> procedural tick.</summary>
    public static Texture2D? MenuCheckTex() => _menuCheck;

    /// <summary>The scrollbar corner-square art, or null -> flat Face fill.</summary>
    public static Texture2D? ScrollCornerTex() => _scrollCorner;

    /// <summary>The menu-separator strip art (horizontal 3-slice), or null -> procedural groove.</summary>
    public static Texture2D? MenuSepTex() => _menuSep;

    /// <summary>Push-button art for the raised/pressed state, or null -> procedural bevel.</summary>
    public static Texture2D? ButtonTex(bool pressed) => pressed ? _buttonDown : _button;

    /// <summary>Switch to the Win 3.1 look: restore the Win31 palette and the asset-driven skin. This
    /// is the canonical Win 3.1 (art) skin, so switching back to it from any theme keeps the PNG art.</summary>
    public static void Apply()
    {
        ThemeManager.Apply(ThemeManager.Win31);
        ThemeManager.ApplySkin(new Win31PngSkin());
    }

    /// <summary>9-slice slice margin derived from the texture: even size -> a 2px center bar, odd -> 1px.</summary>
    public static int SliceMargin(Texture2D t) => (t.Width - (t.Width % 2 == 0 ? 2 : 1)) / 2;

    // Border-only 9-slice: 4 fixed corners + 4 stretched edges; the center is left untouched so the
    // panel's interior fill shows through (the ring PNG has a transparent center).
    public static void NineSlice(SpriteBatch sb, Texture2D t, Rectangle d)
    {
        int s = t.Width, m = SliceMargin(t), mid = s - 2 * m;
        var w = Color.White;
        int rx = d.Right - m, by = d.Bottom - m, iw = d.Width - 2 * m, ih = d.Height - 2 * m;
        sb.Draw(t, new Rectangle(d.X, d.Y, m, m), new Rectangle(0, 0, m, m), w);            // top-left
        sb.Draw(t, new Rectangle(rx, d.Y, m, m), new Rectangle(s - m, 0, m, m), w);         // top-right
        sb.Draw(t, new Rectangle(d.X, by, m, m), new Rectangle(0, s - m, m, m), w);         // bottom-left
        sb.Draw(t, new Rectangle(rx, by, m, m), new Rectangle(s - m, s - m, m, m), w);      // bottom-right
        if (iw > 0)
        {
            sb.Draw(t, new Rectangle(d.X + m, d.Y, iw, m), new Rectangle(m, 0, mid, m), w);     // top
            sb.Draw(t, new Rectangle(d.X + m, by, iw, m), new Rectangle(m, s - m, mid, m), w);  // bottom
        }
        if (ih > 0)
        {
            sb.Draw(t, new Rectangle(d.X, d.Y + m, m, ih), new Rectangle(0, m, m, mid), w);     // left
            sb.Draw(t, new Rectangle(rx, d.Y + m, m, ih), new Rectangle(s - m, m, m, mid), w);  // right
        }
    }

    // Horizontal 3-slice: fixed-width end caps, stretched middle, at the source's full height. For a
    // strip that stretches only in width (menu separators, rules). Cap = min(4, width/2) source px.
    public static void HStrip(SpriteBatch sb, Texture2D t, Rectangle d)
    {
        // Cap must leave a >=1px source middle to stretch, else only the end caps draw (no span).
        int cap = Math.Min(4, (t.Width - 1) / 2), mid = t.Width - 2 * cap;
        var w = Color.White;
        sb.Draw(t, new Rectangle(d.X, d.Y, cap, d.Height), new Rectangle(0, 0, cap, t.Height), w);            // left cap
        sb.Draw(t, new Rectangle(d.Right - cap, d.Y, cap, d.Height), new Rectangle(t.Width - cap, 0, cap, t.Height), w); // right cap
        int iw = d.Width - 2 * cap;
        if (iw > 0 && mid > 0)
            sb.Draw(t, new Rectangle(d.X + cap, d.Y, iw, d.Height), new Rectangle(cap, 0, mid, t.Height), w);  // stretched middle
    }

    // Full 9-slice with independent per-side margins (l/tp/rg/bt), CENTER included (opaque art like a
    // button, whose interior fill is drawn, unlike the transparent-center rings above). Corners are
    // fixed; edges stretch along their axis; the center stretches both ways.
    public static void NineSliceFull(SpriteBatch sb, Texture2D t, int l, int tp, int rg, int bt, Rectangle d)
    {
        int sw = t.Width, sh = t.Height;
        int smw = sw - l - rg, smh = sh - tp - bt;      // source middle
        int dmw = d.Width - l - rg, dmh = d.Height - tp - bt; // dest middle
        var w = Color.White;
        int drx = d.Right - rg, dby = d.Bottom - bt, srx = sw - rg, sby = sh - bt;

        sb.Draw(t, new Rectangle(d.X, d.Y, l, tp), new Rectangle(0, 0, l, tp), w);            // top-left
        sb.Draw(t, new Rectangle(drx, d.Y, rg, tp), new Rectangle(srx, 0, rg, tp), w);        // top-right
        sb.Draw(t, new Rectangle(d.X, dby, l, bt), new Rectangle(0, sby, l, bt), w);          // bottom-left
        sb.Draw(t, new Rectangle(drx, dby, rg, bt), new Rectangle(srx, sby, rg, bt), w);      // bottom-right
        if (dmw > 0)
        {
            sb.Draw(t, new Rectangle(d.X + l, d.Y, dmw, tp), new Rectangle(l, 0, smw, tp), w);      // top
            sb.Draw(t, new Rectangle(d.X + l, dby, dmw, bt), new Rectangle(l, sby, smw, bt), w);    // bottom
        }
        if (dmh > 0)
        {
            sb.Draw(t, new Rectangle(d.X, d.Y + tp, l, dmh), new Rectangle(0, tp, l, smh), w);      // left
            sb.Draw(t, new Rectangle(drx, d.Y + tp, rg, dmh), new Rectangle(srx, tp, rg, smh), w);  // right
        }
        if (dmw > 0 && dmh > 0)
            sb.Draw(t, new Rectangle(d.X + l, d.Y + tp, dmw, dmh), new Rectangle(l, tp, smw, smh), w); // center
    }

}
