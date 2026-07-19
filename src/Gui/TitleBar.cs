using System;
using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>
/// The window's title bar, built to drive a *borderless* window: <see cref="IsOnDragArea"/>
/// reports the draggable region (bar minus buttons) for WM_NCHITTEST, <see cref="OnClose"/>
/// fires when the system box is clicked, and the minimize/maximize buttons are wired by the
/// host. System-menu box on the left, minimize/maximize on the right, centered title.
/// </summary>
public sealed class TitleBar : Widget
{
    private const int BtnSize = 18;

    public string Title = "Window";
    public Action? OnMaximize;
    public Action? OnMinimize;
    public Action? OnClose;

    private Rectangle _sysRect, _minRect, _maxRect;
    private int _pressed = -1; // 0=sys 1=min 2=max

    /// <summary>True where a borderless host may grab the window: the bar, minus its buttons.</summary>
    public bool IsOnDragArea(Point p)
        => Bounds.Contains(p) && !_sysRect.Contains(p) && !_minRect.Contains(p) && !_maxRect.Contains(p);

    public override void Layout()
    {
        int y = Bounds.Y + (Bounds.Height - BtnSize) / 2;
        _sysRect = new Rectangle(Bounds.X + 2, y, BtnSize, BtnSize);
        _maxRect = new Rectangle(Bounds.Right - 2 - BtnSize, y, BtnSize, BtnSize);
        _minRect = new Rectangle(_maxRect.X - BtnSize, y, BtnSize, BtnSize);
    }

    public override void Update(InputState input, GameTime t)
    {
        if (Root()?.Popup.IsOpen == true) return;

        if (input.LeftPressed)
        {
            if (_minRect.Contains(input.Mouse)) _pressed = 1;
            else if (_maxRect.Contains(input.Mouse)) _pressed = 2;
            else if (_sysRect.Contains(input.Mouse)) _pressed = 0;
        }
        if (input.LeftReleased)
        {
            // Single click on the system box closes (no fiddly double-click).
            if (_pressed == 0 && _sysRect.Contains(input.Mouse)) OnClose?.Invoke();
            else if (_pressed == 1 && _minRect.Contains(input.Mouse)) OnMinimize?.Invoke();
            else if (_pressed == 2 && _maxRect.Contains(input.Mouse)) OnMaximize?.Invoke();
            _pressed = -1;
        }
    }

    public override void Draw(Win31Renderer r)
    {
        r.Fill(Bounds, Theme.TitleActive);

        var font = r.UiBoldFont;
        int tw = font.MeasureWidth(Title);
        int tx = Bounds.X + (Bounds.Width - tw) / 2;
        int ty = Bounds.Y + (Bounds.Height - font.LineHeight) / 2;
        r.DrawText(font, Title, tx, ty, Theme.TitleText);

        DrawBtn(r, _sysRect, 0, DrawSysGlyph);
        DrawBtn(r, _minRect, 1, DrawMinGlyph);
        DrawBtn(r, _maxRect, 2, DrawMaxGlyph);
    }

    private void DrawBtn(Win31Renderer r, Rectangle rect, int id, Action<Win31Renderer, Rectangle> glyph)
    {
        bool down = _pressed == id;
        r.DrawPanel(rect, down ? BevelStyle.SunkenThin : BevelStyle.RaisedThin, Theme.Face);
        var inner = rect;
        if (down) inner.Offset(1, 1);
        glyph(r, inner);
    }

    private static void DrawSysGlyph(Win31Renderer r, Rectangle b)
    {
        int w = 10, h = 3;
        int x = b.X + (b.Width - w) / 2, y = b.Y + (b.Height - h) / 2;
        r.Fill(x, y, w, h, Theme.Text);
        r.Fill(x + 1, y + 1, w - 2, 1, Theme.Face);
    }

    private static void DrawMinGlyph(Win31Renderer r, Rectangle b)
    {
        int cx = b.X + b.Width / 2, cy = b.Y + b.Height / 2 + 1;
        for (int i = 0; i < 4; i++) r.Fill(cx - 3 + i, cy - 3 + i, 7 - 2 * i, 1, Theme.Text);
    }

    private static void DrawMaxGlyph(Win31Renderer r, Rectangle b)
    {
        int cx = b.X + b.Width / 2, cy = b.Y + b.Height / 2 - 2;
        for (int i = 0; i < 4; i++) r.Fill(cx - i, cy + i, 1 + 2 * i, 1, Theme.Text);
    }
}
