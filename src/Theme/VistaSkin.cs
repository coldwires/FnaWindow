using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FnaWindow;

/// <summary>
/// Asset-driven Windows Vista (Basic) skin: aero-blue chrome authored as PNGs under
/// Content/skins/vista/, the same piece contract as the Win 3.1 art skin (ring pieces are square
/// with a transparent centre; buttons blit at native size). Any missing PNG falls back to the
/// procedural Win31 drawing, so a partial set still renders. The caption gradient goes through
/// <see cref="Skin.DrawCaptionFill"/>; the 9-slice mechanics are Win31Png's shared helpers.
/// Opaque "Vista Basic" only: no desktop glass, and the frame's rounded corners composite over
/// the window's own fill, not the real window edge.
/// </summary>
public sealed class VistaSkin : Skin
{
    // The engine's procedural skin, used verbatim for any piece we have no art for.
    private static readonly Win31Skin Proc = new();

    public override string Name => "Windows Vista";

    public override bool DrawMenuSeparators => false; // Vista has no 3.1 black rules around the menu bar

    public override int Thickness(BevelStyle style)
        => style is BevelStyle.RaisedThick or BevelStyle.SunkenThick ? 2 : 1; // matches the 5x5 / 3x3 ring art

    // Vista's taller caption; the 4x30 gradient strips were authored for exactly this.
    public override int TitleBarHeight => 30;
    public override int MdiChildTitleHeight => 30;

    public override int CaptionButtonSize(CaptionButton kind)
        => VistaPng.CaptionTex(kind, false)?.Width ?? base.CaptionButtonSize(kind);

    public override int ScrollBarThickness
        => VistaPng.ScrollTex(ScrollArrowDir.Up, false)?.Width ?? base.ScrollBarThickness;

    public override void DrawPanel(Win31Renderer r, Rectangle rect, BevelStyle style, Color bg)
    {
        int t = Thickness(style);
        r.Fill(new Rectangle(rect.X + t, rect.Y + t,
            Math.Max(0, rect.Width - 2 * t), Math.Max(0, rect.Height - 2 * t)), bg);
        DrawBevel(r, rect, style);
    }

    public override void DrawBevel(Win31Renderer r, Rectangle rect, BevelStyle style)
    {
        var tex = VistaPng.Bevel(style);
        if (tex != null && rect.Width >= tex.Width && rect.Height >= tex.Height)
            Win31Png.NineSlice(r.Sb, tex, rect);
        else
            Proc.DrawBevel(r, rect, style);
    }

    public override void DrawCaptionFill(Win31Renderer r, Rectangle rect, Color bg)
    {
        // The window passes its active or inactive caption color; that color doubles as the key
        // for which gradient strip to draw. Any other color (a custom caller) stays a flat fill.
        var tex = bg == Theme.TitleActive ? VistaPng.CaptionActiveTex()
                : bg == Theme.TitleInactive ? VistaPng.CaptionInactiveTex() : null;
        if (tex == null || rect.Height < 3) { base.DrawCaptionFill(r, rect, bg); return; }
        // Vertical 3-slice: the strip's top and bottom rows are 1px hairlines (highlight and
        // shadow) that point-scaling would drop, so they draw 1:1 and only the middle stretches.
        int w = tex.Width, h = tex.Height;
        r.Sb.Draw(tex, new Rectangle(rect.X, rect.Y + 1, rect.Width, rect.Height - 2),
                  new Rectangle(0, 1, w, h - 2), Color.White);
        r.Sb.Draw(tex, new Rectangle(rect.X, rect.Y, rect.Width, 1),
                  new Rectangle(0, 0, w, 1), Color.White);
        r.Sb.Draw(tex, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1),
                  new Rectangle(0, h - 1, w, 1), Color.White);
    }

    public override void DrawCaptionButton(Win31Renderer r, Rectangle rect, CaptionButton kind, bool pressed)
    {
        var tex = VistaPng.CaptionTex(kind, pressed);
        if (tex != null) r.Sb.Draw(tex, new Rectangle(rect.X, rect.Y, tex.Width, tex.Height), Color.White);
        else base.DrawCaptionButton(r, rect, kind, pressed);
    }

    public override void DrawScrollButton(Win31Renderer r, Rectangle rect, ScrollArrowDir dir, bool pressed)
    {
        var tex = VistaPng.ScrollTex(dir, pressed);
        if (tex != null) r.Sb.Draw(tex, new Rectangle(rect.X, rect.Y, tex.Width, tex.Height), Color.White);
        else base.DrawScrollButton(r, rect, dir, pressed);
    }

    public override void DrawScrollCorner(Win31Renderer r, Rectangle rect)
    {
        var tex = VistaPng.ScrollCornerTex();
        if (tex != null) r.Sb.Draw(tex, new Rectangle(rect.X, rect.Y, tex.Width, tex.Height), Color.White);
        else base.DrawScrollCorner(r, rect);
    }

    public override void DrawMenuSeparator(Win31Renderer r, Rectangle rect)
    {
        var tex = VistaPng.MenuSepTex();
        if (tex == null) { base.DrawMenuSeparator(r, rect); return; }
        int y = rect.Y + (rect.Height - tex.Height) / 2;
        Win31Png.HStrip(r.Sb, tex, new Rectangle(rect.X, y, rect.Width, tex.Height));
    }

    public override void DrawWindowFrame(Win31Renderer r, Rectangle rect)
    {
        var tex = VistaPng.FrameTex();
        if (tex != null) Win31Png.NineSlice(r.Sb, tex, rect);
        else Proc.DrawWindowFrame(r, rect);
    }

    public override void DrawChildWindowFrame(Win31Renderer r, Rectangle rect)
    {
        var tex = VistaPng.ChildFrameTex() ?? VistaPng.FrameTex();
        if (tex != null) Win31Png.NineSlice(r.Sb, tex, rect);
        else Proc.DrawWindowFrame(r, rect);
    }

    public override void DrawButton(Win31Renderer r, Rectangle rect, bool pressed)
    {
        var tex = VistaPng.ButtonTex(pressed);
        if (tex == null) { base.DrawButton(r, rect, pressed); return; }
        // Same asymmetric margins as the Win31 button art: the vista btn-tool PNGs are authored to
        // the identical 1px/3px contract so both skins share the slicing constants.
        if (pressed) Win31Png.NineSliceFull(r.Sb, tex, Win31Png.BtnThick, Win31Png.BtnThick, Win31Png.BtnThin, Win31Png.BtnThin, rect);
        else Win31Png.NineSliceFull(r.Sb, tex, Win31Png.BtnThin, Win31Png.BtnThin, Win31Png.BtnThick, Win31Png.BtnThick, rect);
    }

    public override void DrawMenuCheck(Win31Renderer r, int cx, int cy, Color color)
    {
        var tex = VistaPng.MenuCheckTex();
        if (tex != null) r.Sb.Draw(tex, new Vector2(cx, cy - 2), color); // white art, tinted by row color
        else base.DrawMenuCheck(r, cx, cy, color);
    }
}

/// <summary>Loads the Vista skin art once, carries the Vista palette, and applies both. Mirrors
/// Win31Png so the two art skins are used the same way.</summary>
public static class VistaPng
{
    private static Texture2D? _raisedThin, _sunkenThin, _raisedThick, _sunkenThick, _windowFrame, _childFrame, _menuCheck, _scrollCorner, _menuSep;
    private static Texture2D? _button, _buttonDown, _capActive, _capInactive;
    private static readonly Texture2D?[] _caption = new Texture2D?[8]; // [kind*2 + pressed], kind = CaptionButton
    private static readonly Texture2D?[] _scroll = new Texture2D?[8];  // [dir*2 + pressed], dir = ScrollArrowDir
    private static bool _loaded;

    private static Color C(int rgb) => new((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    /// <summary>The Vista palette. TitleActive/TitleInactive are the caption strips' mid colors, so
    /// they are both the skin's art-selection key and an honest flat fallback. Syntax colors are the
    /// classic Visual Studio 2008 scheme, the editor of the Vista era.</summary>
    public static readonly Palette Palette = new(
        "Windows Vista",
        Face: C(0xF0F0F0), LightEdge: C(0xFFFFFF), DarkEdge: C(0x64788C), MidEdge: C(0xA0B0C0),
        TitleActive: C(0xA1C4E2), TitleInactive: C(0xD5DDE6), TitleText: C(0x1E3C5A),
        WindowBg: C(0xFFFFFF), Text: C(0x000000), TextDisabled: C(0x6D6D6D), Desktop: C(0x2B5A87),
        SyntaxKeyword: C(0x0000FF), SyntaxTypeName: C(0x2B91AF), SyntaxString: C(0xA31515),
        SyntaxComment: C(0x008000), SquiggleError: C(0xE51400), SquiggleWarn: C(0xE8A000));

    /// <summary>Load the skin PNGs once. Call at content-load (or before Apply) with the device.</summary>
    public static void LoadAssets(GraphicsDevice gd)
    {
        if (_loaded) return;
        _loaded = true;
        string dir = Path.Combine(AppContext.BaseDirectory, "Content", "skins", "vista");
        Texture2D? L(string f) => SkinArt.TryLoad(gd, Path.Combine(dir, f));

        _raisedThin = L("bevel-raised-thin.png");
        _sunkenThin = L("bevel-sunken-thin.png");
        _raisedThick = L("panel-raised.png");
        _sunkenThick = L("panel-sunken.png");
        _windowFrame = L("frame-window.png");
        _childFrame = L("frame-child.png");
        _menuCheck = L("menu-check.png");
        _scrollCorner = L("scroll-corner.png");
        _menuSep = L("menu-sep.png");
        _button = L("btn-tool.png");
        _buttonDown = L("btn-tool-down.png");
        _capActive = L("caption-active.png");
        _capInactive = L("caption-inactive.png");

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

    public static Texture2D? Bevel(BevelStyle style) => style switch
    {
        BevelStyle.RaisedThin => _raisedThin,
        BevelStyle.SunkenThin => _sunkenThin,
        BevelStyle.RaisedThick => _raisedThick,
        BevelStyle.SunkenThick => _sunkenThick,
        _ => null,
    };

    public static Texture2D? CaptionTex(CaptionButton kind, bool pressed) => _caption[(int)kind * 2 + (pressed ? 1 : 0)];
    public static Texture2D? ScrollTex(ScrollArrowDir dir, bool pressed) => _scroll[(int)dir * 2 + (pressed ? 1 : 0)];
    public static Texture2D? FrameTex() => _windowFrame;
    public static Texture2D? ChildFrameTex() => _childFrame;
    public static Texture2D? MenuCheckTex() => _menuCheck;
    public static Texture2D? ScrollCornerTex() => _scrollCorner;
    public static Texture2D? MenuSepTex() => _menuSep;
    public static Texture2D? ButtonTex(bool pressed) => pressed ? _buttonDown : _button;
    public static Texture2D? CaptionActiveTex() => _capActive;
    public static Texture2D? CaptionInactiveTex() => _capInactive;

    /// <summary>Switch to the Vista look: register + apply the Vista palette and the art skin.
    /// Call LoadAssets first (Apply has no device); without it every piece falls back procedural.</summary>
    public static void Apply()
    {
        ThemeManager.Register(Palette);
        ThemeManager.Apply(Palette);
        ThemeManager.ApplySkin(new VistaSkin());
    }
}
