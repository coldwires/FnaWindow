using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FnaWindow;

/// <summary>
/// Builds a <see cref="BitmapFont"/> at startup from a font already installed on the machine,
/// instead of from a baked atlas shipped in the product.
///
/// Two reasons this exists, and the second is the important one:
///
/// 1. It reaches fonts nothing else here can. GDI+ (and therefore the bake tool) silently refuses
///    raster fonts - ask it for "MS Sans Serif" and it hands back Microsoft Sans Serif without a
///    word. Plain GDI loads them properly, so the genuine Windows 3.1 faces are available:
///    MS Sans Serif (sserife.fon), Fixedsys (vgafix.fon), Courier (coure.fon), System (vgasys.fon),
///    Small Fonts (smalle.fon). They ship with every Windows and are exactly the look this engine
///    is imitating.
///
/// 2. Nothing is redistributed. A font installed on the user's machine is licensed to that user;
///    baking its glyphs into an atlas and shipping the atlas is a copy, and copies of Microsoft's
///    faces are not ours to hand out. Rasterising at startup from their own copy sidesteps the
///    question rather than managing it.
///
/// Raster fonts are already bitmaps, so there is no hinting and no antialiasing to negotiate - the
/// glyphs come out exactly as drawn, identically on every machine. TrueType faces work too and are
/// rendered with antialiasing off, which is what keeps the 1-bit look.
///
/// Windows only. <see cref="Supported"/> is false elsewhere and callers fall back to a baked atlas.
/// </summary>
public sealed partial class BitmapFont
{
    public static bool Supported => OperatingSystem.IsWindows();

    /// <summary>
    /// Rasterises <paramref name="family"/> into a font ready to draw with, or null if the family is
    /// missing, the platform is not Windows, or anything at all goes wrong - callers are expected to
    /// fall back to a shipped atlas rather than fail.
    /// </summary>
    /// <param name="height">Cell height in pixels. Raster fonts only exist at the sizes baked into
    /// the file and GDI picks the nearest, so ask for one that exists: MS Sans Serif has 13 and 16,
    /// Fixedsys 15, Courier 13, 16 and 20.</param>
    /// <param name="lineHeight">Row pitch to report, or 0 to use the font's own height. Set this
    /// when the font must sit in a taller cell than it fills - the editor grid is 15px, but raster
    /// Courier is 13.</param>
    /// <param name="yOffset">Pixels to nudge every glyph down, to centre a short font in a taller
    /// cell.</param>
    /// <param name="antialiased">Keep the rasteriser's grey coverage instead of hard-thresholding
    /// every lit pixel to opaque. Off by default, because 1-bit glyphs ARE the Windows 3.1 look
    /// and every app built on this engine so far wants them. Turn it on for a modern-looking app,
    /// where hard edges read as harsh over a long reading session rather than as period-correct.
    ///
    /// Only meaningful for a TrueType face. A .FON raster font has no greys to keep, so this also
    /// stops asking GDI for raster precision - otherwise it hands back a bitmap face that cannot
    /// be antialiased and the flag appears to do nothing.</param>
    public static BitmapFont? FromSystemFont(GraphicsDevice gd, string family, int height,
        bool bold = false, int lineHeight = 0, int yOffset = 0,
        IReadOnlyList<int>? codepoints = null, bool antialiased = false)
    {
        if (!Supported) return null;
        try { return Rasterise(gd, family, height, bold, lineHeight, yOffset, codepoints, antialiased); }
        catch { return null; }
    }

    /// <summary>True if <paramref name="family"/> resolves to itself rather than being quietly
    /// substituted. GDI never reports a miss, so this asks what it actually selected.</summary>
    public static bool HasFamily(string family)
    {
        if (!Supported) return false;
        IntPtr dc = IntPtr.Zero, font = IntPtr.Zero, prev = IntPtr.Zero;
        try
        {
            dc = CreateCompatibleDC(IntPtr.Zero);
            font = MakeFont(family, 12, false);
            prev = SelectObject(dc, font);
            var sb = new System.Text.StringBuilder(64);
            GetTextFaceW(dc, sb.Capacity, sb);
            return string.Equals(sb.ToString(), family, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
        finally
        {
            if (prev != IntPtr.Zero) SelectObject(dc, prev);
            if (font != IntPtr.Zero) DeleteObject(font);
            if (dc != IntPtr.Zero) DeleteDC(dc);
        }
    }

    private static BitmapFont? Rasterise(GraphicsDevice gd, string family, int height, bool bold,
        int lineHeight, int yOffset, IReadOnlyList<int>? codepoints, bool antialiased = false)
    {
        var codes = codepoints ?? DefaultCodepoints;

        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return null;
        IntPtr font = IntPtr.Zero, dib = IntPtr.Zero, prevFont = IntPtr.Zero, prevDib = IntPtr.Zero;

        try
        {
            font = MakeFont(family, height, bold, antialiased);
            if (font == IntPtr.Zero) return null;
            prevFont = SelectObject(dc, font);

            if (!GetTextMetricsW(dc, out TEXTMETRIC tm)) return null;
            int cellH = tm.tmHeight;

            // Per-glyph advances, and the atlas layout that follows from them.
            var advance = new Dictionary<int, int>(codes.Count);
            foreach (int c in codes)
            {
                var buf = new int[1];
                advance[c] = GetCharWidth32W(dc, (uint)c, (uint)c, buf) ? Math.Max(0, buf[0]) : tm.tmAveCharWidth;
            }

            // A cell is the glyph's advance plus side bearing, NOT just the advance.
            //
            // A 1-bit raster glyph never paints outside its advance, so a cell of exactly the
            // advance was safe. An antialiased TrueType glyph does: the coverage fringe spills a
            // pixel, and letters like W and italic forms overhang by design. With a cell of exactly
            // the advance, everything past the edge is sliced off and the text looks chewed.
            //
            // The bearing is carried as a negative XOff so drawing still starts at the pen
            // position and the advance still governs layout - only the sampled rectangle grows.
            int bearing = antialiased ? 2 : 0;

            const int AtlasW = 512, Pad = 1;
            var place = new Dictionary<int, Rectangle>(codes.Count);
            int x = Pad, y = Pad, maxAdvance = 0;
            foreach (int c in codes)
            {
                int w = Math.Max(1, advance[c]) + bearing * 2;
                if (x + w + Pad > AtlasW) { x = Pad; y += cellH + Pad; }
                place[c] = new Rectangle(x, y, w, cellH);
                x += w + Pad;
                maxAdvance = Math.Max(maxAdvance, advance[c]);
            }
            int atlasH = y + cellH + Pad;

            // A 32bpp top-down DIB we can both draw into with GDI and read back as pixels.
            dib = CreateDib(dc, AtlasW, atlasH, out IntPtr bits);
            if (dib == IntPtr.Zero) return null;
            prevDib = SelectObject(dc, dib);
            SelectObject(dc, font);                 // the DIB selection reset the font

            SetBkMode(dc, TRANSPARENT);
            SetTextColor(dc, 0x00FFFFFF);           // white glyphs; the DIB starts black
            foreach (int c in codes)
            {
                var r = place[c];
                // Inset by the bearing so the glyph sits centred in its padded cell and its
                // overhang has somewhere to go on both sides.
                TextOutW(dc, r.X + bearing, r.Y, char.ConvertFromUtf32(c), 1);
            }
            GdiFlush();

            // Read the DIB back. Glyphs were drawn white on black, so a pixel's brightness IS its
            // coverage.
            //
            // Thresholded (the default): any lit pixel becomes opaque white so SpriteBatch can
            // tint it. Raster fonts are 1-bit so this is exact.
            //
            // Antialiased: keep the coverage as alpha, written PREMULTIPLIED (v,v,v,v). The batch
            // blends premultiplied, and BitmapFont.Draw tints by multiplying, so an opaque tint
            // colour times (v,v,v,v) lands as that colour at coverage v - which is what the blend
            // expects. Writing it straight (255,255,255,v) would blend as white haze around every
            // glyph instead.
            int count = AtlasW * atlasH;
            var raw = new int[count];
            Marshal.Copy(bits, raw, 0, count);
            var pixels = new Color[count];
            for (int i = 0; i < count; i++)
            {
                int rgb = raw[i] & 0x00FFFFFF;
                if (!antialiased) { pixels[i] = rgb != 0 ? Color.White : Color.Transparent; continue; }
                int v = rgb & 0xFF;                     // grey, so any channel will do
                pixels[i] = v == 0 ? Color.Transparent : new Color(v, v, v, v);
            }

            var tex = new Texture2D(gd, AtlasW, atlasH);
            tex.SetData(pixels);

            var glyphs = new Dictionary<int, GlyphRec>(codes.Count);
            foreach (int c in codes)
                glyphs[c] = new GlyphRec(place[c], advance[c], -bearing, yOffset);

            return new BitmapFont(tex, glyphs, lineHeight > 0 ? lineHeight : cellH, maxAdvance);
        }
        finally
        {
            if (prevDib != IntPtr.Zero) SelectObject(dc, prevDib);
            if (prevFont != IntPtr.Zero) SelectObject(dc, prevFont);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (font != IntPtr.Zero) DeleteObject(font);
            if (dc != IntPtr.Zero) DeleteDC(dc);
        }
    }

    /// <summary>Printable ASCII, plus the two punctuation marks the Win 3.1 UI text uses literally.</summary>
    private static readonly int[] DefaultCodepoints = BuildDefaultCodepoints();

    private static int[] BuildDefaultCodepoints()
    {
        var list = new List<int>(97);
        for (int c = 32; c <= 126; c++) list.Add(c);
        list.Add(0x2014);   // em dash
        list.Add(0x2026);   // ellipsis
        return list.ToArray();
    }

    private static IntPtr MakeFont(string family, int height, bool bold, bool antialiased = false) =>
        CreateFontW(height, 0, 0, 0, bold ? FW_BOLD : FW_NORMAL, 0, 0, 0,
            DEFAULT_CHARSET,
            antialiased ? OUT_DEFAULT_PRECIS : OUT_RASTER_PRECIS, 0,
            antialiased ? ANTIALIASED_QUALITY : NONANTIALIASED_QUALITY, 0, family);

    private static IntPtr CreateDib(IntPtr dc, int w, int h, out IntPtr bits)
    {
        var bi = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h,          // negative = top-down, so row 0 is the top
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0,      // BI_RGB
        };
        return CreateDIBSection(dc, ref bi, 0 /* DIB_RGB_COLORS */, out bits, IntPtr.Zero, 0);
    }

    // -- Win32 -------------------------------------------------------------
    private const int FW_NORMAL = 400, FW_BOLD = 700;
    private const uint DEFAULT_CHARSET = 1;
    private const uint OUT_RASTER_PRECIS = 6;         // reach the .FON faces, not a TrueType stand-in
    private const uint OUT_DEFAULT_PRECIS = 0;        // let GDI pick: a .FON cannot be antialiased
    private const uint NONANTIALIASED_QUALITY = 3;    // 1-bit glyphs, which is the whole look
    private const uint ANTIALIASED_QUALITY = 4;       // grey coverage; ClearType is deliberately
                                                      // not used - it is subpixel and coloured,
                                                      // which a tinted single-channel atlas cannot
                                                      // carry
    private const int TRANSPARENT = 1;

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(int h, int w, int esc, int orient, int weight, uint italic,
        uint underline, uint strikeout, uint charset, uint outPrec, uint clipPrec, uint quality,
        uint pitchAndFamily, string face);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr hdc, uint color);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool TextOutW(IntPtr hdc, int x, int y, string s, int len);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetCharWidth32W(IntPtr hdc, uint first, uint last, int[] buffer);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetTextFaceW(IntPtr hdc, int count, System.Text.StringBuilder face);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetTextMetricsW(IntPtr hdc, out TEXTMETRIC tm);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage,
        out IntPtr bits, IntPtr section, uint offset);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TEXTMETRIC
    {
        public int tmHeight, tmAscent, tmDescent, tmInternalLeading, tmExternalLeading,
            tmAveCharWidth, tmMaxCharWidth, tmWeight, tmOverhang,
            tmDigitizedAspectX, tmDigitizedAspectY;
        public char tmFirstChar, tmLastChar, tmDefaultChar, tmBreakChar;
        public byte tmItalic, tmUnderlined, tmStruckOut, tmPitchAndFamily, tmCharSet;
    }
}
