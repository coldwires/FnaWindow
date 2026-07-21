using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FnaWindow;

/// <summary>
/// A minimal Win 3.1-style modal input box (RaisedThick panel, navy title, a sunken text
/// field, OK / Cancel). Lives in the <see cref="PopupLayer"/> and is driven by its owner
/// (like a menu). Enter/OK commits the text; Esc/Cancel aborts. Reusable for Rename Session,
/// Resume by ID, etc.
/// <para>Two flags trim it down: <see cref="NoField"/> drops the text field, for a confirm or a
/// message; <see cref="OkOnly"/> drops the Cancel button and centres OK, for an About box or a
/// notice where there is nothing to cancel. Set both for a plain message box.</para>
/// </summary>
public sealed class InputDialog : Widget
{
    private const int W = 320;

    private readonly string _title, _prompt;
    private string _text;
    private int _caret;
    private int _promptY;
    private double _blinkMs;
    private bool _caretOn = true;

    private Rectangle _titleRect, _fieldRect;

    private readonly PushButton _ok = new("OK");
    private readonly PushButton _cancel = new("Cancel");
    private readonly TitleDrag _drag = new();

    public Action<string>? OnOk;
    public Action? OnCancel;
    public bool NoField;                                  // confirm-only (no text field)
    public bool OkOnly;                                   // single button, centred (About, notices)
    public string OkLabel = "OK", CancelLabel = "Cancel";

    public InputDialog(string title, string prompt, string initial)
    {
        _title = title;
        _prompt = prompt;
        _text = initial ?? "";
        _caret = _text.Length;
    }

    public override void Layout()
    {
        // Height sizes to the content so multi-line prompts never run under the buttons.
        const int lineH = 15;
        int lineCount = Math.Max(1, _prompt.Split('\n').Length);
        int contentTop = 26;                                   // below the 16px title
        int contentH = NoField ? lineCount * lineH : lineH + 24; // prompt lines | label + field
        int buttonsTop = contentTop + contentH + 18;           // breathing room above the buttons
        int h = buttonsTop + 22 + 10;

        var area = Root()?.Bounds ?? Parent?.Bounds ?? new Rectangle(0, 0, 1024, 768);
        if (area.Width < W || area.Height < h) area = new Rectangle(0, 0, 1024, 768);

        // Centre on first layout; once the user has dragged it, keep where they put it (clamped, so a
        // window resize cannot strand it off screen).
        int x, y;
        if (_drag.Moved)
        {
            x = Math.Clamp(Bounds.X, area.X, Math.Max(area.X, area.Right - W));
            y = Math.Clamp(Bounds.Y, area.Y, Math.Max(area.Y, area.Bottom - h));
        }
        else
        {
            x = area.X + (area.Width - W) / 2;
            y = area.Y + (area.Height - h) / 2;
        }
        Bounds = new Rectangle(x, y, W, h);

        _titleRect = new Rectangle(x + 3, y + 3, W - 6, 16);
        _promptY = y + contentTop;
        _fieldRect = new Rectangle(x + 14, y + contentTop + lineH + 2, W - 28, 20); // used only when !NoField

        _ok.Label = OkLabel;
        _cancel.Label = CancelLabel;
        if (OkOnly)
        {
            _ok.Bounds = new Rectangle(x + (W - 72) / 2, y + buttonsTop, 72, 22);
            _cancel.Bounds = Rectangle.Empty;
        }
        else
        {
            _ok.Bounds = new Rectangle(x + W - 168, y + buttonsTop, 72, 22);
            _cancel.Bounds = new Rectangle(x + W - 88, y + buttonsTop, 72, 22);
        }
    }

    public override void Update(InputState input, GameTime t)
    {
        Root()?.RequestRedraw(); // keep the caret blinking while the modal is open (idle throttle)

        _blinkMs += t.ElapsedGameTime.TotalMilliseconds;
        if (_blinkMs >= Theme.CaretBlinkMs) { _blinkMs = 0; _caretOn = !_caretOn; }

        if (!NoField)
        {
            foreach (char c in input.TypedChars)
                if (!char.IsControl(c) && _text.Length < 64)
                { _text = _text.Insert(_caret, c.ToString()); _caret++; Blink(); }

            if (input.Pressed(Keys.Back) && _caret > 0) { _text = _text.Remove(_caret - 1, 1); _caret--; Blink(); }
            else if (input.Pressed(Keys.Delete) && _caret < _text.Length) { _text = _text.Remove(_caret, 1); Blink(); }
            else if (input.Pressed(Keys.Left) && _caret > 0) { _caret--; Blink(); }
            else if (input.Pressed(Keys.Right) && _caret < _text.Length) { _caret++; Blink(); }
            else if (input.Pressed(Keys.Home)) { _caret = 0; Blink(); }
            else if (input.Pressed(Keys.End)) { _caret = _text.Length; Blink(); }
        }

        if (input.Pressed(Keys.Enter)) { OnOk?.Invoke(_text); return; }
        // Esc still dismisses a one-button box; with nothing to cancel it means the same as OK.
        if (input.Pressed(Keys.Escape)) { if (OkOnly) OnOk?.Invoke(_text); else OnCancel?.Invoke(); return; }

        // Move by the title bar, like any Win 3.1 dialog.
        var bounds = Bounds;
        if (_drag.Update(input, _titleRect, Root()?.Bounds ?? Bounds, ref bounds)) { Bounds = bounds; Layout(); }

        // Buttons act on release, showing pressed while held.
        if (_ok.Update(input)) { OnOk?.Invoke(_text); return; }
        if (!OkOnly && _cancel.Update(input)) { OnCancel?.Invoke(); return; }
    }

    private void Blink() { _blinkMs = 0; _caretOn = true; }

    public override void Draw(Win31Renderer r)
    {
        r.DrawPanel(Bounds, BevelStyle.RaisedThick, Theme.Face);

        r.Fill(_titleRect, Theme.TitleActive);
        r.DrawText(r.UiBoldFont, _title, _titleRect.X + 5,
            _titleRect.Y + (_titleRect.Height - r.UiBoldFont.LineHeight) / 2, Theme.TitleText);

        int py = _promptY;
        foreach (var pl in _prompt.Split('\n'))
        {
            r.DrawText(r.UiFont, pl, Bounds.X + 14, py, Theme.Text);
            py += r.UiFont.LineHeight + 2;
        }

        if (!NoField)
        {
            r.DrawPanel(_fieldRect, BevelStyle.SunkenThick, Theme.WindowBg);
            int tx = _fieldRect.X + 4;
            int ty = _fieldRect.Y + (_fieldRect.Height - r.UiFont.LineHeight) / 2;
            r.DrawText(r.UiFont, _text, tx, ty, Theme.Text);
            if (_caretOn)
            {
                int cx = tx + r.UiFont.MeasureWidth(_text.Substring(0, _caret));
                r.Fill(cx, ty, 1, r.UiFont.LineHeight, Theme.Text);
            }
        }

        _ok.Draw(r);
        if (!OkOnly) _cancel.Draw(r);
    }

    public override Widget? HitTest(Point p) => Bounds.Contains(p) ? this : null;
}
