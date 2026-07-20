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
    public string Title = "Window";
    public bool Active = true;     // navy caption when focused; grey (3.1 inactive) otherwise
    public bool Maximized;         // when true the maximize button shows the restore glyph
    public BitmapFont? Font;       // caption font; null -> the bold UI font
    public Action? OnMaximize;
    public Action? OnMinimize;
    public Action? OnClose;

    private Rectangle _sysRect, _minRect, _maxRect;
    private int _pressed = -1; // 0=sys 1=min 2=max

    /// <summary>True where a borderless host may grab the window: the bar, minus its buttons.</summary>
    public bool IsOnDragArea(Point p)
        => Bounds.Contains(p) && !_sysRect.Contains(p) && !_minRect.Contains(p) && !_maxRect.Contains(p);

    public override void Layout()
        => WindowCaption.LayoutButtons(Bounds, out _sysRect, out _minRect, out _maxRect);

    public override void Update(InputState input, GameTime t)
    {
        if (Root()?.Popup.IsOpen == true) return;

        if (input.LeftPressed)
            _pressed = WindowCaption.HitButton(input.Mouse, _sysRect, _minRect, _maxRect);
        if (input.LeftReleased)
        {
            // Fire only if released over the same button that was pressed. Single click on the system
            // box closes (no fiddly double-click).
            if (_pressed >= 0 && WindowCaption.HitButton(input.Mouse, _sysRect, _minRect, _maxRect) == _pressed)
            {
                if (_pressed == 0) OnClose?.Invoke();
                else if (_pressed == 1) OnMinimize?.Invoke();
                else if (_pressed == 2) OnMaximize?.Invoke();
            }
            _pressed = -1;
        }
    }

    public override void Draw(Win31Renderer r)
        => WindowCaption.Draw(r, Bounds, Title,
            Active ? Theme.TitleActive : Theme.Face,
            Active ? Theme.TitleText : Theme.Text,
            _sysRect, _minRect, _maxRect, _pressed, Maximized, Font ?? r.UiBoldFont);
}
