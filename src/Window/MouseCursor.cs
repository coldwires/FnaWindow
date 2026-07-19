using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FnaWindow;

/// <summary>
/// A software-drawn mouse pointer: a small sprite loaded from a PNG and blitted at the mouse
/// position each frame in place of the OS cursor. Integer-scaled with the renderer's point
/// sampling so it stays crisp. Swap the PNG to change the pointer.
/// </summary>
public sealed class MouseCursor
{
    private readonly Texture2D _tex;

    /// <summary>The sprite pixel (pre-scale) that lands on the actual mouse point. (0,0) is the
    /// top-left pixel; use the tip for an arrow or the center for a block.</summary>
    public Point Hotspot { get; }

    /// <summary>Integer magnification. 1 = native pixels; larger matches chunky Win 3.1 chrome.</summary>
    public int Scale { get; set; }

    private MouseCursor(Texture2D tex, Point hotspot, int scale)
    {
        _tex = tex;
        Hotspot = hotspot;
        Scale = scale;
    }

    /// <summary>
    /// Load a cursor PNG. If <paramref name="colorKey"/> is given, every pixel matching that RGB is
    /// turned fully transparent (for art that ships on a flat background). <paramref name="hotspot"/>
    /// is the sprite pixel that sits on the mouse point.
    /// </summary>
    public static MouseCursor Load(GraphicsDevice gd, string pngPath, Point hotspot,
                                   int scale = 2, Color? colorKey = null)
    {
        Texture2D tex;
        using (var fs = File.OpenRead(pngPath))
            tex = Texture2D.FromStream(gd, fs);

        if (colorKey is Color key)
        {
            var px = new Color[tex.Width * tex.Height];
            tex.GetData(px);
            for (int i = 0; i < px.Length; i++)
                if (px[i].R == key.R && px[i].G == key.G && px[i].B == key.B)
                    px[i] = Color.Transparent;
            tex.SetData(px);
        }
        return new MouseCursor(tex, hotspot, scale);
    }

    /// <summary>Blit the pointer at the given mouse point. Call inside an active SpriteBatch (the
    /// same PointClamp batch the chrome uses), after everything else so it sits on top.</summary>
    public void Draw(SpriteBatch sb, Point mouse)
    {
        var dest = new Rectangle(
            mouse.X - Hotspot.X * Scale, mouse.Y - Hotspot.Y * Scale,
            _tex.Width * Scale, _tex.Height * Scale);
        sb.Draw(_tex, dest, Color.White);
    }
}
