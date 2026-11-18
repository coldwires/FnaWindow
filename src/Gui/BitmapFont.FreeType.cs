using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FnaWindow;

/// <summary>
/// <see cref="FromSystemFont"/> away from Windows: the same atlas, drawn by FreeType instead of GDI.
/// Families come from files the app registered (<see cref="AddFontFile"/>) first, then from the
/// machine's fonts through fontconfig (the fc-match tool). A few Windows UI faces that apps name
/// stand for their nearest generic family, so "Consolas" gets the machine's monospace face.
///
/// Sizes follow GDI's meaning of a positive height: the cell, from the OS/2 table's Windows ascent
/// and descent, so a font asked for at 20px is as tall as on Windows. Hinting differs, so glyphs
/// are not pixel identical to the GDI ones.
///
/// Reads FreeType's structs at fixed offsets, which hold for 64-bit Linux and macOS. Anything else
/// reports no FreeType and callers fall back to a shipped atlas.
/// </summary>
public sealed partial class BitmapFont
{
    private static class FreeType
    {
        private const string Lib = "libfreetype.so.6";

        [DllImport(Lib)] private static extern int FT_Init_FreeType(out IntPtr library);
        [DllImport(Lib)] private static extern int FT_New_Face(IntPtr library, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint index, out IntPtr face);
        [DllImport(Lib)] private static extern int FT_Done_Face(IntPtr face);
        [DllImport(Lib)] private static extern int FT_Request_Size(IntPtr face, ref SizeRequest req);
        [DllImport(Lib)] private static extern uint FT_Get_Char_Index(IntPtr face, nuint code);
        [DllImport(Lib)] private static extern int FT_Load_Char(IntPtr face, nuint code, int flags);
        [DllImport(Lib)] private static extern int FT_Render_Glyph(IntPtr slot, int mode);
        [DllImport(Lib)] private static extern void FT_GlyphSlot_Embolden(IntPtr slot);
        [DllImport(Lib)] private static extern int FT_Load_Sfnt_Table(IntPtr face, nuint tag, nint offset, byte[]? buffer, ref nuint length);

        [StructLayout(LayoutKind.Sequential)]
        private struct SizeRequest { public int Type; public nint Width, Height; public uint HoriRes, VertRes; }

        private const int SizeNominal = 0, SizeRealDim = 1;   // FT_SIZE_REQUEST_TYPE_*: the em, or ascender to descender
        private const uint TagOS2 = 0x4F532F32;               // 'OS/2'
        private const int LoadTargetMono = 2 << 16;           // FT_LOAD_TARGET_MONO: hint for 1-bit output
        private const int RenderNormal = 0, RenderMono = 2;   // FT_RENDER_MODE_*
        private const int PixelModeMono = 1;                  // FT_PIXEL_MODE_MONO; GRAY (2) is a byte a pixel
        private const long StyleBold = 2;                     // FT_STYLE_FLAG_BOLD

        // Offsets into FT_FaceRec, FT_SizeRec and FT_GlyphSlotRec on LP64.
        private const int FaceStyleFlags = 24, FaceFamily = 40, FaceUnitsPerEm = 136, FaceGlyph = 152, FaceSize = 160;
        private const int SizeAscender = 48, SizeDescender = 56;
        private const int SlotAdvanceX = 128, SlotBitmap = 152, SlotLeft = 192, SlotTop = 196;

        private static readonly object Gate = new();
        private static IntPtr _library;
        private static bool _tried;

        public static bool Available
        {
            get
            {
                lock (Gate)
                {
                    if (_tried) return _library != IntPtr.Zero;
                    _tried = true;
                    if (OperatingSystem.IsWindows() || IntPtr.Size != 8) return false;
                    try { if (FT_Init_FreeType(out var lib) == 0) _library = lib; } catch { }
                    return _library != IntPtr.Zero;
                }
            }
        }

        /// <summary>Families registered by AddFontFile: family (lower case) to its files and whether
        /// each is a bold style.</summary>
        private static readonly Dictionary<string, List<(string path, bool bold)>> Registered = new();
        private static readonly Dictionary<string, string?> Matched = new();

        /// <summary>Windows faces apps name for UI text, and the fontconfig pattern that stands in.</summary>
        private static readonly Dictionary<string, string> StandIns = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Consolas"] = "monospace",
            ["Courier New"] = "monospace",
            ["Segoe UI"] = "sans-serif",
            ["Tahoma"] = "sans-serif",
            ["Arial"] = "sans-serif",
            ["Segoe UI Symbol"] = "DejaVu Sans",
        };

        public static bool AddFile(string path)
        {
            if (!Available) return false;
            lock (Gate)
            {
                if (FT_New_Face(_library, path, 0, out var face) != 0) return false;
                try
                {
                    string? family = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(face, FaceFamily));
                    if (string.IsNullOrEmpty(family)) return false;
                    bool bold = (Marshal.ReadInt64(face, FaceStyleFlags) & StyleBold) != 0;
                    string key = family.ToLowerInvariant();
                    if (!Registered.TryGetValue(key, out var list)) Registered[key] = list = new();
                    list.Add((path, bold));
                    return true;
                }
                finally { FT_Done_Face(face); }
            }
        }

        public static bool Has(string family) => Locate(family, false) != null;

        /// <summary>The file to draw <paramref name="family"/> from, and whether it is already bold
        /// (if not and bold was asked for, the glyphs are emboldened).</summary>
        private static (string path, bool bold)? Locate(string family, bool bold)
        {
            if (!Available) return null;
            lock (Gate)
            {
                if (Registered.TryGetValue(family.ToLowerInvariant(), out var list) && list.Count > 0)
                {
                    foreach (var f in list) if (f.bold == bold) return f;
                    return list[0];
                }
            }
            bool standIn = StandIns.TryGetValue(family, out var pattern);
            string? file = Match((standIn ? pattern! : family) + (bold ? ":weight=bold" : ""), standIn ? null : family);
            return file == null ? null : (file, bold && Match((standIn ? pattern! : family) + ":weight=bold", null, styleBold: true) == file);
        }

        /// <summary>fc-match's file for a pattern, or null. With <paramref name="family"/> set, only a
        /// face of that family counts: fc-match always answers, often with a substitute.</summary>
        private static string? Match(string pattern, string? family, bool styleBold = false)
        {
            string key = pattern + "|" + family + "|" + styleBold;
            lock (Gate) if (Matched.TryGetValue(key, out var known)) return known;
            string? result = null;
            try
            {
                var psi = new ProcessStartInfo("fc-match") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                psi.ArgumentList.Add("-f");
                psi.ArgumentList.Add("%{family}\n%{style}\n%{file}\n");
                psi.ArgumentList.Add(pattern);
                using var p = Process.Start(psi);
                if (p != null)
                {
                    var lines = p.StandardOutput.ReadToEnd().Split('\n');
                    p.WaitForExit(2000);
                    if (lines.Length >= 3 && lines[2].Length > 0)
                    {
                        bool familyOk = family == null || Array.Exists(lines[0].Split(','), f => string.Equals(f.Trim(), family, StringComparison.OrdinalIgnoreCase));
                        bool styleOk = !styleBold || lines[1].Contains("Bold", StringComparison.OrdinalIgnoreCase);
                        if (familyOk && styleOk) result = lines[2];
                    }
                }
            }
            catch { }
            lock (Gate) Matched[key] = result;
            return result;
        }

        private sealed class Face : IDisposable
        {
            public IntPtr Ptr;
            public bool Embolden;
            public int Ascent, Descent;
            public IntPtr Slot => Marshal.ReadIntPtr(Ptr, FaceGlyph);
            public void Dispose() { if (Ptr != IntPtr.Zero) FT_Done_Face(Ptr); Ptr = IntPtr.Zero; }
        }

        private static Face? Open(string family, int height, bool bold)
        {
            if (Locate(family, bold) is not { } at) return null;
            if (FT_New_Face(_library, at.path, 0, out var ptr) != 0) return null;
            var face = new Face { Ptr = ptr, Embolden = bold && !at.bold };
            // GDI's cell is the OS/2 table's Windows ascent plus descent, which can differ a lot from
            // the hhea metrics FreeType goes by (Press Start 2P: 1374 units against 1000). Size the
            // em so that cell comes out at the height asked for, as GDI does.
            int upem = (ushort)Marshal.ReadInt16(ptr, FaceUnitsPerEm);
            if (upem > 0 && WinMetrics(ptr) is { } win && win.ascent + win.descent > 0)
            {
                int total = win.ascent + win.descent;
                var em = new SizeRequest { Type = SizeNominal, Width = 0, Height = (nint)Math.Round(height * 64.0 * upem / total) };
                if (FT_Request_Size(ptr, ref em) != 0) { face.Dispose(); return null; }
                face.Ascent = (int)Math.Round(height * (double)win.ascent / total);
                face.Descent = height - face.Ascent;
                return face;
            }
            var req = new SizeRequest { Type = SizeRealDim, Width = 0, Height = height * 64 };
            if (FT_Request_Size(ptr, ref req) != 0) { face.Dispose(); return null; }
            IntPtr size = Marshal.ReadIntPtr(ptr, FaceSize);
            face.Ascent = (int)((Marshal.ReadInt64(size, SizeAscender) + 63) >> 6);
            face.Descent = (int)((-Marshal.ReadInt64(size, SizeDescender) + 63) >> 6);
            return face;
        }

        /// <summary>usWinAscent and usWinDescent from the face's OS/2 table, or null without one.</summary>
        private static (int ascent, int descent)? WinMetrics(IntPtr face)
        {
            nuint length = 0;
            if (FT_Load_Sfnt_Table(face, TagOS2, 0, null, ref length) != 0 || length < 78) return null;
            var os2 = new byte[(int)length];
            if (FT_Load_Sfnt_Table(face, TagOS2, 0, os2, ref length) != 0) return null;
            return ((os2[74] << 8) | os2[75], (os2[76] << 8) | os2[77]);
        }

        /// <summary>Load and render one character into the face's slot. False if it has no glyph.</summary>
        private static bool Render(Face f, int c, bool antialiased)
        {
            if (FT_Get_Char_Index(f.Ptr, (nuint)c) == 0) return false;
            if (FT_Load_Char(f.Ptr, (nuint)c, antialiased ? 0 : LoadTargetMono) != 0) return false;
            if (f.Embolden) FT_GlyphSlot_Embolden(f.Slot);
            return FT_Render_Glyph(f.Slot, antialiased ? RenderNormal : RenderMono) == 0;
        }

        public static BitmapFont? Rasterise(GraphicsDevice gd, string family, int height, bool bold,
            int lineHeight, int yOffset, IReadOnlyList<int> codes, bool antialiased, IReadOnlyList<string>? fallbacks)
        {
            if (!Available || height <= 0) return null;
            lock (Gate)
            {
                var faces = new List<Face>();
                try
                {
                    var main = Open(family, height, bold);
                    if (main == null) return null;
                    faces.Add(main);
                    int cellH = main.Ascent + main.Descent;

                    // Each character from the first face that has it: the main one, then the fallbacks,
                    // all on the main face's baseline.
                    var from = new Dictionary<int, Face>(codes.Count);
                    var missing = new List<int>();
                    foreach (int c in codes)
                        if (FT_Get_Char_Index(main.Ptr, (nuint)c) != 0) from[c] = main; else missing.Add(c);
                    if (fallbacks != null)
                        foreach (string fam in fallbacks)
                        {
                            if (missing.Count == 0) break;
                            if (Open(fam, height, bold) is not { } ff) continue;
                            faces.Add(ff);
                            for (int i = missing.Count - 1; i >= 0; i--)
                                if (FT_Get_Char_Index(ff.Ptr, (nuint)missing[i]) != 0) { from[missing[i]] = ff; missing.RemoveAt(i); }
                        }

                    // Render each glyph once, keeping its coverage, offset and advance.
                    var glyphs = new Dictionary<int, (byte[] cov, int w, int h, int left, int top, int advance)>(codes.Count);
                    int fallbackAdvance = Math.Max(1, height / 2);
                    foreach (int c in codes)
                    {
                        var face = from.TryGetValue(c, out var ff) ? ff : main;
                        if (!Render(face, c, antialiased)) { glyphs[c] = (Array.Empty<byte>(), 0, 0, 0, 0, c == ' ' ? fallbackAdvance / 2 : fallbackAdvance); continue; }
                        IntPtr slot = face.Slot;
                        int advance = (int)((Marshal.ReadInt64(slot, SlotAdvanceX) + 32) >> 6);
                        int rows = Marshal.ReadInt32(slot, SlotBitmap), width = Marshal.ReadInt32(slot, SlotBitmap + 4);
                        int pitch = Marshal.ReadInt32(slot, SlotBitmap + 8);
                        IntPtr buffer = Marshal.ReadIntPtr(slot, SlotBitmap + 16);
                        byte mode = Marshal.ReadByte(slot, SlotBitmap + 26);
                        var cov = new byte[width * rows];
                        if (buffer != IntPtr.Zero && width > 0 && rows > 0)
                        {
                            var row = new byte[Math.Abs(pitch)];
                            for (int y = 0; y < rows; y++)
                            {
                                Marshal.Copy(buffer + y * pitch, row, 0, row.Length);
                                for (int x = 0; x < width; x++)
                                    cov[y * width + x] = mode == PixelModeMono ? ((row[x >> 3] & (0x80 >> (x & 7))) != 0 ? (byte)255 : (byte)0) : row[x];
                            }
                        }
                        glyphs[c] = (cov, width, rows, Marshal.ReadInt32(slot, SlotLeft), Marshal.ReadInt32(slot, SlotTop), Math.Max(0, advance));
                    }

                    // The same layout as the GDI path: a cell is the advance plus a bearing each side.
                    const int bearing = 2, AtlasW = 512, Pad = 1;
                    var place = new Dictionary<int, Rectangle>(codes.Count);
                    int px = Pad, py = Pad, maxAdvance = 0;
                    foreach (int c in codes)
                    {
                        int w = Math.Min(AtlasW - 2 * Pad, Math.Max(1, glyphs[c].advance) + bearing * 2);
                        if (px + w + Pad > AtlasW) { px = Pad; py += cellH + Pad; }
                        place[c] = new Rectangle(px, py, w, cellH);
                        px += w + Pad;
                        maxAdvance = Math.Max(maxAdvance, glyphs[c].advance);
                    }
                    int atlasH = py + cellH + Pad;

                    // White glyphs: thresholded to opaque, or coverage kept premultiplied (see the GDI path).
                    var pixels = new Color[AtlasW * atlasH];
                    foreach (int c in codes)
                    {
                        var r = place[c];
                        var g = glyphs[c];
                        int ox = r.X + bearing + g.left, oy = r.Y + main.Ascent - g.top;
                        for (int y = 0; y < g.h; y++)
                        {
                            int ty = oy + y;
                            if (ty < r.Top || ty >= r.Bottom) continue;
                            for (int x = 0; x < g.w; x++)
                            {
                                int tx = ox + x;
                                if (tx < r.Left || tx >= r.Right) continue;
                                byte v = g.cov[y * g.w + x];
                                if (v == 0) continue;
                                pixels[ty * AtlasW + tx] = antialiased ? new Color(v, v, v, v) : Color.White;
                            }
                        }
                    }

                    var tex = new Texture2D(gd, AtlasW, atlasH);
                    tex.SetData(pixels);
                    var recs = new Dictionary<int, GlyphRec>(codes.Count);
                    foreach (int c in codes) recs[c] = new GlyphRec(place[c], glyphs[c].advance, -bearing, yOffset);
                    return new BitmapFont(tex, recs, lineHeight > 0 ? lineHeight : cellH, maxAdvance);
                }
                finally { foreach (var f in faces) f.Dispose(); }
            }
        }
    }
}
