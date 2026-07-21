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

    private Rectangle _titleRect, _fieldRect, _okRect, _cancelRect;

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
        int buttonsTop = contentTop + contentH + 10;
        int h = buttonsTop + 22 + 8;

        var area = Root()?.Bounds ?? Parent?.Bounds ?? new Rectangle(0, 0, 1024, 768);
        if (area.Width < W || area.Height < h) area = new Rectangle(0, 0, 1024, 768);
        int x = area.X + (area.Width - W) / 2;
        int y = area.Y + (area.Height - h) / 2;
        Bounds = new Rectangle(x, y, W, h);

        _titleRect = new Rectangle(x + 3, y + 3, W - 6, 16);
        _promptY = y + contentTop;
        _fieldRect = new Rectangle(x + 14, y + contentTop + lineH + 2, W - 28, 20); // used only when !NoField
        if (OkOnly)
        {
            // One button, centred. Cancel keeps a rect so nothing has to null-check it, but it is
            // parked off the dialog where no click can reach it.
            _okRect = new Rectangle(x + (W - 72) / 2, y + buttonsTop, 72, 22);
            _cancelRect = Rectangle.Empty;
        }
        else
        {
            _okRect = new Rectangle(x + W - 168, y + buttonsTop, 72, 22);
            _cancelRect = new Rectangle(x + W - 88, y + buttonsTop, 72, 22);
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

        if (input.LeftPressed)
        {
            if (_okRect.Contains(input.Mouse)) { OnOk?.Invoke(_text); return; }
            if (!OkOnly && _cancelRect.Contains(input.Mouse)) { OnCancel?.Invoke(); return; }
        }
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

        Button(r, _okRect, OkLabel);
        if (!OkOnly) Button(r, _cancelRect, CancelLabel);
    }

    private static void Button(Win31Renderer r, Rectangle rect, string label)
    {
        r.DrawPanel(rect, BevelStyle.RaisedThin, Theme.Face);
        int tw = r.UiFont.MeasureWidth(label);
        r.DrawText(r.UiFont, label, rect.X + (rect.Width - tw) / 2,
            rect.Y + (rect.Height - r.UiFont.LineHeight) / 2, Theme.Text);
    }

    public override Widget? HitTest(Point p) => Bounds.Contains(p) ? this : null;
}
