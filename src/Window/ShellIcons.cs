using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FnaWindow;

/// <summary>
/// File-type icons from the Windows shell, the way Explorer (and DreamMaker) get them: the
/// registry walk HKCR\&lt;ext&gt; -> ProgID -> DefaultIcon -> ExtractIconEx. Ships no third-party
/// art, draws whatever the user's machine associates with the extension, and one registry walk
/// per distinct extension is cached for the life of the process. An extension nothing claims
/// returns null, so the caller keeps its own fallback art. Non-Windows always returns null.
/// </summary>
public static class ShellIcons
{
    private static readonly Dictionary<string, Texture2D?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The 16x16 shell icon for this path's extension, or null when nothing claims it.</summary>
    public static Texture2D? Get(GraphicsDevice gd, string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        string ext;
        try { ext = System.IO.Path.GetExtension(path); } catch { return null; }
        if (string.IsNullOrEmpty(ext)) return null;
        if (_cache.TryGetValue(ext, out var cached)) return cached;
        Texture2D? tex = null;
        try { tex = Load(gd, ext, path); } catch { /* an unreadable association is just the fallback */ }
        _cache[ext] = tex;
        return tex;
    }

    private static Texture2D? Load(GraphicsDevice gd, string ext, string samplePath)
    {
        string? progId = RegString(ext, null);
        if (string.IsNullOrWhiteSpace(progId)) return null;
        string? spec = RegString(progId + "\\DefaultIcon", null);
        if (string.IsNullOrWhiteSpace(spec)) return null;

        spec = Environment.ExpandEnvironmentVariables(spec.Trim().Trim('"'));
        string file; int index = 0;
        if (spec == "%1") file = samplePath; // the file is its own icon (.ico and friends)
        else
        {
            int comma = spec.LastIndexOf(',');
            if (comma > 0 && int.TryParse(spec[(comma + 1)..].Trim(), out int idx)) { file = spec[..comma].Trim().Trim('"'); index = idx; }
            else file = spec;
        }

        if (ExtractIconExW(file, index, IntPtr.Zero, out IntPtr icon, 1) < 1 || icon == IntPtr.Zero) return null;
        try { return FromHicon(gd, icon); }
        finally { DestroyIcon(icon); }
    }

    // HICON -> premultiplied Texture2D. 32bpp icons carry their own alpha; older 4/8bpp art has
    // an all-zero alpha channel and uses the mono mask instead.
    private static Texture2D? FromHicon(GraphicsDevice gd, IntPtr icon)
    {
        if (!GetIconInfo(icon, out ICONINFO ii)) return null;
        try
        {
            if (ii.hbmColor == IntPtr.Zero) return null; // 1bpp icon; not worth a special path
            if (GetObjectW(ii.hbmColor, Marshal.SizeOf<BITMAP>(), out BITMAP bm) == 0) return null;
            int w = bm.bmWidth, h = bm.bmHeight;
            if (w <= 0 || h <= 0 || w > 256 || h > 256) return null;

            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            var color = new int[w * h];
            var mask = new int[w * h];
            try
            {
                var bmi = new BITMAPINFO { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                if (GetDIBits(dc, ii.hbmColor, 0, (uint)h, color, ref bmi, 0) == 0) return null;
                var bmi2 = new BITMAPINFO { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                if (ii.hbmMask != IntPtr.Zero) GetDIBits(dc, ii.hbmMask, 0, (uint)h, mask, ref bmi2, 0);
            }
            finally { DeleteDC(dc); }

            bool anyAlpha = false;
            foreach (int px in color)
                if ((px & unchecked((int)0xFF000000)) != 0) { anyAlpha = true; break; }

            var data = new Color[w * h];
            for (int i = 0; i < data.Length; i++)
            {
                int px = color[i];
                int a = anyAlpha ? (px >> 24) & 0xFF : ((mask[i] & 0xFFFFFF) == 0 ? 255 : 0);
                int r = (px >> 16) & 0xFF, g = (px >> 8) & 0xFF, b = px & 0xFF;
                data[i] = new Color(r * a / 255, g * a / 255, b * a / 255, a); // premultiplied
            }
            var tex = new Texture2D(gd, w, h);
            tex.SetData(data);
            return tex;
        }
        finally
        {
            if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
            if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
        }
    }

    // A default-value read from HKEY_CLASSES_ROOT, without the Registry package dependency.
    private static string? RegString(string subKey, string? value)
    {
        uint size = 0;
        if (RegGetValueW(HKEY_CLASSES_ROOT, subKey, value, RRF_RT_REG_SZ, IntPtr.Zero, null, ref size) != 0 || size == 0)
            return null;
        var sb = new System.Text.StringBuilder((int)size / 2 + 1);
        return RegGetValueW(HKEY_CLASSES_ROOT, subKey, value, RRF_RT_REG_SZ, IntPtr.Zero, sb, ref size) == 0
            ? sb.ToString() : null;
    }

    // ---- Win32 -------------------------------------------------------------------------------

    private static readonly IntPtr HKEY_CLASSES_ROOT = new(unchecked((int)0x80000000));
    private const uint RRF_RT_REG_SZ = 0x02;

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegGetValueW(IntPtr hkey, string subKey, string? value, uint flags,
        IntPtr type, System.Text.StringBuilder? data, ref uint size);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string file, int index, IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetObjectW(IntPtr obj, int size, out BITMAP bm);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, int[] bits, ref BITMAPINFO bmi, uint usage);
}
