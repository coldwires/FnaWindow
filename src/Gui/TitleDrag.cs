using System;
using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>
/// Drag-a-box-by-its-caption, shared by anything that floats and is not an OS window: modal dialogs,
/// pickers, panels. A Win 3.1 dialog can be moved by its title bar, and every dialog wanting that
/// should not re-implement the offset bookkeeping.
/// <para>The owner keeps one of these, calls <see cref="Update"/> with its caption rect, and honours
/// <see cref="Moved"/> when laying out: a box that has been dragged must stop re-centring itself.</para>
/// </summary>
public sealed class TitleDrag
{
    private bool _dragging;
    private Point _offset;

    /// <summary>The box has been moved by the user at least once.</summary>
    public bool Moved { get; private set; }

    /// <summary>A drag is in progress right now.</summary>
    public bool Dragging => _dragging;

    /// <summary>
    /// Pump the drag. Moves <paramref name="bounds"/> while the caption is held, keeping the box
    /// inside <paramref name="area"/>. Returns true if the position changed this frame, so the owner
    /// can re-layout its contents.
    /// </summary>
    public bool Update(InputState input, Rectangle title, Rectangle area, ref Rectangle bounds)
    {
        var m = input.Mouse;

        if (input.LeftPressed && title.Contains(m))
        {
            _dragging = true;
            _offset = new Point(m.X - bounds.X, m.Y - bounds.Y);
        }
        if (input.LeftReleased) _dragging = false;
        if (!_dragging || !input.LeftDown) return false;

        int x = Math.Clamp(m.X - _offset.X, area.X, Math.Max(area.X, area.Right - bounds.Width));
        int y = Math.Clamp(m.Y - _offset.Y, area.Y, Math.Max(area.Y, area.Bottom - bounds.Height));
        if (x == bounds.X && y == bounds.Y) return false;

        bounds = new Rectangle(x, y, bounds.Width, bounds.Height);
        Moved = true;
        return true;
    }
}
