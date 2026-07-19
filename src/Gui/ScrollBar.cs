using System;
using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>
/// Classic Win 3.1 scrollbar: dithered Face track, RaisedThin arrow
/// buttons and thumb. Works in content units - <see cref="ContentSize"/> total,
/// <see cref="ViewSize"/> visible, <see cref="Value"/> the first visible index.
/// </summary>
public sealed class ScrollBar : Widget
{
    private const double InitialDelayMs = 300;
    private const double RepeatMs = 50;

    public bool Horizontal;
    public int ContentSize = 1;
    public int ViewSize = 1;
    public int Value;
    public Action<int>? OnChange;

    private int _btn; // 16
    private Rectangle _dec, _inc, _track; // arrow buttons + inner track
    private int _pressed = -1;            // 0=dec 1=inc 2=trackPageUp 3=trackPageDown
    private bool _draggingThumb;
    private int _dragOffset;
    private double _repeatTimer;

    public int MaxValue => Math.Max(0, ContentSize - ViewSize);
    public bool Active => ContentSize > ViewSize;

    public override void Layout()
    {
        _btn = Theme.ScrollBarThickness;
        if (Horizontal)
        {
            _dec = new Rectangle(Bounds.X, Bounds.Y, _btn, _btn);
            _inc = new Rectangle(Bounds.Right - _btn, Bounds.Y, _btn, _btn);
            _track = new Rectangle(Bounds.X + _btn, Bounds.Y, Bounds.Width - 2 * _btn, _btn);
        }
        else
        {
            _dec = new Rectangle(Bounds.X, Bounds.Y, _btn, _btn);
            _inc = new Rectangle(Bounds.X, Bounds.Bottom - _btn, _btn, _btn);
            _track = new Rectangle(Bounds.X, Bounds.Y + _btn, _btn, Bounds.Height - 2 * _btn);
        }
    }

    private int TrackLen => Horizontal ? _track.Width : _track.Height;

    private int ThumbLen()
    {
        if (!Active) return TrackLen;
        int len = (int)((long)TrackLen * ViewSize / ContentSize);
        return Math.Clamp(len, 8, TrackLen);
    }

    private Rectangle ThumbRect()
    {
        int len = ThumbLen();
        int span = TrackLen - len;
        int off = MaxValue == 0 ? 0 : (int)((long)span * Value / MaxValue);
        return Horizontal
            ? new Rectangle(_track.X + off, _track.Y, len, _btn)
            : new Rectangle(_track.X, _track.Y + off, _btn, len);
    }

    private void SetValue(int v)
    {
        v = Math.Clamp(v, 0, MaxValue);
        if (v != Value) { Value = v; OnChange?.Invoke(v); }
    }

    public override void Update(InputState input, GameTime t)
    {
        if (Root()?.Popup.IsOpen == true) return;
        if (!Active) { _pressed = -1; _draggingThumb = false; return; }

        double dt = t.ElapsedGameTime.TotalMilliseconds;
        var m = input.Mouse;

        if (input.LeftPressed)
        {
            if (_dec.Contains(m)) { _pressed = 0; SetValue(Value - 1); _repeatTimer = InitialDelayMs; }
            else if (_inc.Contains(m)) { _pressed = 1; SetValue(Value + 1); _repeatTimer = InitialDelayMs; }
            else
            {
                var thumb = ThumbRect();
                if (thumb.Contains(m))
                {
                    _draggingThumb = true;
                    _dragOffset = (Horizontal ? m.X - thumb.X : m.Y - thumb.Y);
                }
                else if (_track.Contains(m))
                {
                    bool before = Horizontal ? m.X < thumb.X : m.Y < thumb.Y;
                    _pressed = before ? 2 : 3;
                    SetValue(Value + (before ? -ViewSize : ViewSize));
                    _repeatTimer = InitialDelayMs;
                }
            }
        }

        if (_draggingThumb && input.LeftDown)
        {
            int span = TrackLen - ThumbLen();
            int pos = (Horizontal ? m.X - _track.X : m.Y - _track.Y) - _dragOffset;
            SetValue(span <= 0 ? 0 : (int)((long)pos * MaxValue / span));
        }

        // Auto-repeat while an arrow/track region is held.
        if (_pressed >= 0 && input.LeftDown)
        {
            _repeatTimer -= dt;
            if (_repeatTimer <= 0)
            {
                _repeatTimer = RepeatMs;
                var thumb = ThumbRect();
                switch (_pressed)
                {
                    case 0 when _dec.Contains(m): SetValue(Value - 1); break;
                    case 1 when _inc.Contains(m): SetValue(Value + 1); break;
                    case 2 when _track.Contains(m) && (Horizontal ? m.X < thumb.X : m.Y < thumb.Y): SetValue(Value - ViewSize); break;
                    case 3 when _track.Contains(m) && (Horizontal ? m.X >= thumb.X : m.Y >= thumb.Y): SetValue(Value + ViewSize); break;
                }
            }
        }

        if (input.LeftReleased) { _pressed = -1; _draggingThumb = false; }
    }

    public override void Draw(Win31Renderer r)
    {
        // Track dither.
        r.DrawDither(_track);

        // Arrow buttons.
        DrawArrow(r, _dec, _pressed == 0, dec: true);
        DrawArrow(r, _inc, _pressed == 1, dec: false);

        // Thumb.
        if (Active) r.DrawPanel(ThumbRect(), BevelStyle.RaisedThin, Theme.Face);
    }

    private void DrawArrow(Win31Renderer r, Rectangle rect, bool down, bool dec)
    {
        r.DrawPanel(rect, down ? BevelStyle.SunkenThin : BevelStyle.RaisedThin, Theme.Face);
        var b = rect;
        if (down) b.Offset(1, 1);
        int cx = b.X + b.Width / 2, cy = b.Y + b.Height / 2;

        // Triangle pointing in the scroll direction (4px tall).
        for (int i = 0; i < 4; i++)
        {
            int w = 1 + 2 * i;
            if (Horizontal)
            {
                int x = dec ? cx + 1 - i : cx - 2 + i;
                r.Fill(x, cy - i, 1, 1 + 2 * i, Theme.Text);
            }
            else
            {
                int y = dec ? cy + 1 - i : cy - 2 + i;
                r.Fill(cx - i, y, w, 1, Theme.Text);
            }
        }
    }
}
