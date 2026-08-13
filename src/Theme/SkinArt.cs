using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FnaWindow;

/// <summary>Shared loader for skin art PNGs, used by every asset-driven skin so the premultiply
/// rule lives once. Returns null on any failure so a missing piece falls back to procedural.</summary>
public static class SkinArt
{
    // Load a PNG and premultiply alpha (the batch composites premultiplied; FromStream loads straight
    // alpha, so authored anti-aliasing would fringe without this). 1-bit art (a=0/255) is unaffected.
    public static Texture2D? TryLoad(GraphicsDevice gd, string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var tex = Texture2D.FromStream(gd, fs);
            var data = new Color[tex.Width * tex.Height];
            tex.GetData(data);
            for (int i = 0; i < data.Length; i++)
            {
                var c = data[i];
                if (c.A == 255) continue;
                data[i] = new Color(c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255, (int)c.A);
            }
            tex.SetData(data);
            return tex;
        }
        catch { return null; }
    }
}
