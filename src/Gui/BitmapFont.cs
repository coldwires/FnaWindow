using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FnaWindow;

/// <summary>
/// A baked bitmap font: a PNG atlas of white glyphs + a JSON glyph map
/// (char → x,y,w,h,advance), produced by tools/FontGen.
/// Glyphs are white so <see cref="SpriteBatch"/> tints them to any color.
/// No kerning; text is never scaled fractionally.
/// </summary>
public sealed class BitmapFont
{
    private readonly Texture2D _atlas;
    private readonly Dictionary<int, GlyphRec> _glyphs;

    public int LineHeight { get; }
    /// <summary>Nominal cell width (monospace advance for the editor font).</summary>
    public int CellWidth { get; }

    private BitmapFont(Texture2D atlas, Dictionary<int, GlyphRec> glyphs, int lineHeight, int cellWidth)
    {
        _atlas = atlas;
        _glyphs = glyphs;
        LineHeight = lineHeight;
        CellWidth = cellWidth;
    }

    /// <summary>
    /// Loads a font from "<paramref name="basePathNoExt"/>.png" + ".json".
    /// </summary>
    public static BitmapFont Load(GraphicsDevice gd, string basePathNoExt)
    {
        string json = File.ReadAllText(basePathNoExt + ".json");
        var doc = JsonSerializer.Deserialize<AtlasJson>(json, JsonOpts)
                  ?? throw new InvalidDataException("Bad font json: " + basePathNoExt);

        Texture2D atlas;
        using (var fs = File.OpenRead(basePathNoExt + ".png"))
            atlas = Texture2D.FromStream(gd, fs);

        var map = new Dictionary<int, GlyphRec>(doc.Glyphs.Count);
        foreach (var g in doc.Glyphs)
            map[g.C] = new GlyphRec(new Rectangle(g.X, g.Y, g.W, g.H), g.Advance);

        return new BitmapFont(atlas, map, doc.LineHeight, doc.CellW);
    }

    /// <summary>Pixel size of a single-line string: (sum of advances, LineHeight).</summary>
    public Point Measure(string s)
    {
        int w = 0;
        foreach (char c in s)
            w += AdvanceOf(c);
        return new Point(w, LineHeight);
    }

    public int MeasureWidth(string s)
    {
        int w = 0;
        foreach (char c in s) w += AdvanceOf(c);
        return w;
    }

    /// <summary>Advance width of one glyph (falls back to cell width for unknown chars).</summary>
    public int AdvanceOf(char c)
        => _glyphs.TryGetValue(c, out var g) ? g.Advance : CellWidth;

    /// <summary>Draws a single line of text, top-left at (<paramref name="x"/>,<paramref name="y"/>).</summary>
    public void Draw(SpriteBatch sb, string s, int x, int y, Color color)
    {
        int pen = x;
        foreach (char c in s)
        {
            if (_glyphs.TryGetValue(c, out var g))
            {
                if (c != ' ')
                    sb.Draw(_atlas, new Vector2(pen, y), g.Src, color);
                pen += g.Advance;
            }
            else
            {
                pen += CellWidth;
            }
        }
    }

    public void Draw(SpriteBatch sb, string s, Point pos, Color color) => Draw(sb, s, pos.X, pos.Y, color);

    private readonly record struct GlyphRec(Rectangle Src, int Advance);

    // ── JSON DTOs (match tools/FontGen output) ────────────────────────────
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class AtlasJson
    {
        [JsonPropertyName("name")]       public string Name { get; set; } = "";
        [JsonPropertyName("lineHeight")] public int LineHeight { get; set; }
        [JsonPropertyName("cellW")]      public int CellW { get; set; }
        [JsonPropertyName("glyphs")]     public List<GlyphJson> Glyphs { get; set; } = new();
    }

    private sealed class GlyphJson
    {
        [JsonPropertyName("c")] public int C { get; set; }
        [JsonPropertyName("x")] public int X { get; set; }
        [JsonPropertyName("y")] public int Y { get; set; }
        [JsonPropertyName("w")] public int W { get; set; }
        [JsonPropertyName("h")] public int H { get; set; }
        [JsonPropertyName("advance")] public int Advance { get; set; }
    }
}
