using System;
using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>
/// A simple bottom status bar: one flexible message cell on the left plus any number of
/// fixed-width cells on the right. Each cell is a classic SunkenThin panel. Set
/// <see cref="Message"/> and <see cref="RightCells"/> and it draws itself.
/// </summary>
public sealed class StatusBar : Widget
{
    public string Message = "Ready";
    /// <summary>Fixed cells shown right-to-left (index 0 is rightmost).</summary>
    public string[] RightCells = Array.Empty<string>();
    public int RightCellWidth = 90;

    private Rectangle _msgCell;

    public override void Layout()
    {
        int rightW = RightCells.Length * RightCellWidth;
        _msgCell = new Rectangle(Bounds.X, Bounds.Y, Math.Max(0, Bounds.Width - rightW), Bounds.Height);
    }

    public override void Draw(Win31Renderer r)
    {
        r.Fill(Bounds, Theme.Face);
        Cell(r, _msgCell, Message);
        for (int i = 0; i < RightCells.Length; i++)
        {
            var cell = new Rectangle(Bounds.Right - (i + 1) * RightCellWidth, Bounds.Y, RightCellWidth, Bounds.Height);
            Cell(r, cell, RightCells[i]);
        }
    }

    private static void Cell(Win31Renderer r, Rectangle cell, string text)
    {
        r.DrawBevel(cell, BevelStyle.SunkenThin);
        int max = cell.Width - 2 * Theme.StatusCellPadX;
        string s = text;
        while (s.Length > 1 && r.UiFont.MeasureWidth(s) > max) s = s.Substring(0, s.Length - 1);
        int ty = cell.Y + (cell.Height - r.UiFont.LineHeight) / 2;
        r.DrawText(r.UiFont, s, cell.X + Theme.StatusCellPadX, ty, Theme.Text);
    }
}
