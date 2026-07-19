using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FnaWindow;

/// <summary>
/// Win31Renderer - every pixel of chrome comes through here: solid fills, the four
/// bevel styles, and bitmap text with Win 3.1 mnemonic underlines.
/// A single 1×1 white texture is stretched for all fills/borders; nothing is
/// anti-aliased (the owning SpriteBatch uses SamplerState.PointClamp).
/// </summary>
public sealed class Win31Renderer
{
    private readonly Texture2D _pixel;
    private readonly Texture2D _dither; // 2×2 Face/LightEdge checker (scrollbar tracks)

    public SpriteBatch Sb { get; }
    public BitmapFont UiFont { get; }
    public BitmapFont UiBoldFont { get; }
    public BitmapFont EditorFont { get; }

    public Win31Renderer(GraphicsDevice gd, SpriteBatch sb, BitmapFont uiFont, BitmapFont uiBoldFont, BitmapFont editorFont)
    {
        Sb = sb;
        UiFont = uiFont;
        UiBoldFont = uiBoldFont;
        EditorFont = editorFont;
        _pixel = new Texture2D(gd, 1, 1);
        _pixel.SetData(new[] { Color.White });

        // 64×64 checker of the 2×2 Face/LightEdge dither pattern. Tiled
        // across scrollbar tracks; period 2 so even-offset tiling keeps phase.
        const int D = 64;
        var px = new Color[D * D];
        for (int y = 0; y < D; y++)
            for (int x = 0; x < D; x++)
                px[y * D + x] = ((x + y) & 1) == 0 ? Theme.Face : Theme.LightEdge;
        _dither = new Texture2D(gd, D, D);
        _dither.SetData(px);
    }

    /// <summary>Fills a rectangle with the 2×2 Face/LightEdge checker (scrollbar track).</summary>
    public void DrawDither(Rectangle r)
    {
        const int D = 64;
        for (int y = 0; y < r.Height; y += D)
        {
            int h = Math.Min(D, r.Height - y);
            for (int x = 0; x < r.Width; x += D)
            {
                int w = Math.Min(D, r.Width - x);
                Sb.Draw(_dither, new Rectangle(r.X + x, r.Y + y, w, h), new Rectangle(0, 0, w, h), Color.White);
            }
        }
    }

    // ── Primitive fills ───────────────────────────────────────────────────
    public void Fill(Rectangle r, Color c) => Sb.Draw(_pixel, r, c);
    public void Fill(int x, int y, int w, int h, Color c) => Sb.Draw(_pixel, new Rectangle(x, y, w, h), c);

    public void HLine(int x, int y, int w, Color c) => Sb.Draw(_pixel, new Rectangle(x, y, w, 1), c);
    public void VLine(int x, int y, int h, Color c) => Sb.Draw(_pixel, new Rectangle(x, y, 1, h), c);

    /// <summary>Aliased 1px line (Bresenham) - used for diagnostic leader lines.</summary>
    public void Line(int x0, int y0, int x1, int y1, Color c)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            Sb.Draw(_pixel, new Rectangle(x0, y0, 1, 1), c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>1px hairline rectangle outline in a single flat color (e.g. listbox popup border).</summary>
    public void FrameRect(Rectangle r, Color c)
    {
        HLine(r.Left, r.Top, r.Width, c);
        HLine(r.Left, r.Bottom - 1, r.Width, c);
        VLine(r.Left, r.Top, r.Height, c);
        VLine(r.Right - 1, r.Top, r.Height, c);
    }

    // ── Bevels (forwarded to the active skin) ─────────────────────────────
    // The look of panels/bevels lives in the active Skin (default Win31Skin), so a skin swap can
    // change chrome drawing without touching a single widget. See src/Theme/Skin.cs.

    public void DrawBevel(Rectangle r, BevelStyle style) => ThemeManager.Skin.DrawBevel(this, r, style);

    /// <summary>Bevel border plus an interior fill (interior excludes the bevel thickness).</summary>
    public void DrawPanel(Rectangle r, BevelStyle style, Color bg) => ThemeManager.Skin.DrawPanel(this, r, style, bg);

    public static int Thickness(BevelStyle s) => ThemeManager.Skin.Thickness(s);

    public static Rectangle Inset(Rectangle r, int n)
        => new(r.X + n, r.Y + n, r.Width - 2 * n, r.Height - 2 * n);

    // ── Text ──────────────────────────────────────────────────────────────
    public void DrawText(BitmapFont font, string s, int x, int y, Color color) => font.Draw(Sb, s, x, y, color);
    public void DrawText(BitmapFont font, string s, Point p, Color color) => font.Draw(Sb, s, p.X, p.Y, color);

    /// <summary>
    /// Draws a label honoring Win 3.1 accelerator markup: "&amp;F" underlines the F,
    /// "&amp;&amp;" renders a literal ampersand. Underline is a 1px rule under the marked glyph.
    /// </summary>
    public void DrawTextMnemonic(BitmapFont font, string raw, int x, int y, Color color)
    {
        var (disp, uidx) = ParseMnemonic(raw);
        font.Draw(Sb, disp, x, y, color);
        if (uidx >= 0)
        {
            int ux = x + font.MeasureWidth(disp.Substring(0, uidx));
            int uw = font.AdvanceOf(disp[uidx]);
            Fill(new Rectangle(ux, y + font.LineHeight - 2, uw, 1), color);
        }
    }

    /// <summary>Returns the displayed string (markers stripped) and the index of the
    /// underlined glyph within it, or -1 if none.</summary>
    public static (string display, int underlineIndex) ParseMnemonic(string raw)
    {
        var sb = new System.Text.StringBuilder(raw.Length);
        int uidx = -1;
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '&' && i + 1 < raw.Length)
            {
                char next = raw[i + 1];
                if (next == '&')
                {
                    sb.Append('&');
                    i++;
                }
                else
                {
                    if (uidx < 0) uidx = sb.Length;
                    sb.Append(next);
                    i++;
                }
            }
            else
            {
                sb.Append(c);
            }
        }
        return (sb.ToString(), uidx);
    }

    /// <summary>The accelerator key of a mnemonic label (lowercased), or '\0' if none.</summary>
    public static char MnemonicKey(string raw)
    {
        var (disp, uidx) = ParseMnemonic(raw);
        return uidx >= 0 ? char.ToLowerInvariant(disp[uidx]) : '\0';
    }
}
