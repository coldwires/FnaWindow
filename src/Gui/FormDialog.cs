using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FnaWindow;

/// <summary>The values a <see cref="FormDialog"/> was holding when OK was pressed.</summary>
public readonly struct FormResult
{
    private readonly string[] _fields;
    private readonly bool[] _checks;

    internal FormResult(string[] fields, bool[] checks) { _fields = fields; _checks = checks; }

    /// <summary>The text of field <paramref name="i"/>, in the order it was added. "" if out of range.</summary>
    public string Text(int i) => i >= 0 && i < _fields.Length ? _fields[i] : "";

    /// <summary>The state of check <paramref name="i"/>, in the order it was added. False if out of range.</summary>
    public bool Check(int i) => i >= 0 && i < _checks.Length && _checks[i];

    public int FieldCount => _fields.Length;
    public int CheckCount => _checks.Length;
}

/// <summary>
/// A Win 3.1 modal with any number of labelled text fields and check boxes - Find and Replace,
/// a copy-to box, anything that needs more than the one field <see cref="InputDialog"/> offers.
///
/// <see cref="InputDialog"/> is still the right choice for one field, a confirm or a message; this
/// is its bigger sibling rather than a replacement, so nothing that already works has to change.
///
/// Built by chaining, then handed to <c>frame.ShowDialog</c>:
/// <code>
/// var dlg = new FormDialog("Replace")
///     .AddField("Find what:", term)
///     .AddField("Replace with:", "")
///     .AddCheck("Match case");
/// dlg.OkLabel = "Replace All";
/// dlg.OnOk = v => DoReplace(v.Text(0), v.Text(1), v.Check(0));
/// dlg.OnCancel = frame.CloseDialog;
/// </code>
/// Tab moves between fields, Enter is OK, Esc is Cancel, and the box is dragged by its caption like
/// every other engine modal.
/// </summary>
public sealed class FormDialog : Widget
{
    private const int W = 360;
    private const int FieldH = 20;
    private const int RowGap = 8;

    private sealed class Field
    {
        public string Label = "";
        public TextField Box = new();
    }

    private readonly string _title;
    private readonly List<Field> _fields = new();
    private readonly List<CheckBox> _checks = new();

    private int _focus;              // index of the focused field

    private Rectangle _titleRect;
    private readonly PushButton _ok = new("OK");
    private readonly PushButton _alt = new("");
    private readonly PushButton _cancel = new("Cancel");
    private readonly TitleDrag _drag = new();

    public Action<FormResult>? OnOk;
    public Action? OnCancel;
    public string OkLabel = "OK", CancelLabel = "Cancel";

    /// <summary>Label for an optional third button; null (the default) leaves it off.</summary>
    public string? AltLabel;
    public Action<FormResult>? OnAlt;

    public FormDialog(string title) => _title = title;

    public FormDialog AddField(string label, string initial = "", int maxLength = 128)
    {
        _fields.Add(new Field
        {
            Label = label,
            Box = new TextField(initial ?? "") { MaxLength = maxLength, Focused = _fields.Count == 0 },
        });
        return this;
    }

    public FormDialog AddCheck(string label, bool isChecked = false)
    {
        _checks.Add(new CheckBox(label, isChecked));
        return this;
    }

    /// <summary>Reads a field back before OK - for a live search that updates as you type.</summary>
    public string Text(int i) => i >= 0 && i < _fields.Count ? _fields[i].Box.Text : "";
    public bool Checked(int i) => i >= 0 && i < _checks.Count && _checks[i].Checked;

    private FormResult Result()
    {
        var f = new string[_fields.Count];
        for (int i = 0; i < f.Length; i++) f[i] = _fields[i].Box.Text;
        var c = new bool[_checks.Count];
        for (int i = 0; i < c.Length; i++) c[i] = _checks[i].Checked;
        return new FormResult(f, c);
    }

    public override void Layout()
    {
        const int lineH = 15;
        int contentTop = 26;                                  // below the 16px caption

        int contentH = 0;
        foreach (var _ in _fields) contentH += lineH + FieldH + RowGap;
        foreach (var _ in _checks) contentH += CheckBox.BoxSize + RowGap;

        int buttonsTop = contentTop + contentH + 10;
        int h = buttonsTop + 22 + 10;

        var area = Root()?.Bounds ?? Parent?.Bounds ?? new Rectangle(0, 0, 1024, 768);
        if (area.Width < W || area.Height < h) area = new Rectangle(0, 0, 1024, 768);

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

        int ry = y + contentTop;
        foreach (var f in _fields)
        {
            f.Box.Bounds = new Rectangle(x + 14, ry + lineH, W - 28, FieldH);
            ry += lineH + FieldH + RowGap;
        }
        foreach (var c in _checks)
        {
            c.Bounds = new Rectangle(x + 14, ry, W - 28, CheckBox.BoxSize);
            ry += CheckBox.BoxSize + RowGap;
        }

        _ok.Label = OkLabel;
        _cancel.Label = CancelLabel;
        _alt.Label = AltLabel ?? "";
        _ok.Bounds = new Rectangle(x + W - (AltLabel != null ? 248 : 168), y + buttonsTop, 72, 22);
        _alt.Bounds = AltLabel != null ? new Rectangle(x + W - 168, y + buttonsTop, 72, 22) : Rectangle.Empty;
        _cancel.Bounds = new Rectangle(x + W - 88, y + buttonsTop, 72, 22);
    }

    public override void Update(InputState input, GameTime t)
    {
        Root()?.RequestRedraw(); // keep the caret blinking while the modal is open (idle throttle)

        // Click a field to focus it. The field itself also sets Focused on a click inside it, but
        // this dialog owns which ONE is focused, so it is decided here and pushed down.
        if (input.LeftPressed)
            for (int i = 0; i < _fields.Count; i++)
                if (_fields[i].Box.Bounds.Contains(input.Mouse)) _focus = i;

        // Tab cycles fields - the reason this dialog exists is having more than one.
        if (_fields.Count > 0 && input.Pressed(Keys.Tab))
            _focus = (_focus + (input.Shift ? _fields.Count - 1 : 1)) % _fields.Count;

        _focus = Math.Clamp(_focus, 0, Math.Max(0, _fields.Count - 1));
        for (int i = 0; i < _fields.Count; i++)
        {
            _fields[i].Box.Focused = i == _focus;
            _fields[i].Box.Update(input, t);
        }

        foreach (var c in _checks) c.Update(input);

        if (input.Pressed(Keys.Enter)) { OnOk?.Invoke(Result()); return; }
        if (input.Pressed(Keys.Escape)) { OnCancel?.Invoke(); return; }

        var bounds = Bounds;
        if (_drag.Update(input, _titleRect, Root()?.Bounds ?? Bounds, ref bounds)) { Bounds = bounds; Layout(); }

        if (_ok.Update(input)) { OnOk?.Invoke(Result()); return; }
        if (AltLabel != null && _alt.Update(input)) { OnAlt?.Invoke(Result()); return; }
        if (_cancel.Update(input)) { OnCancel?.Invoke(); return; }
    }

    public override void Draw(Win31Renderer r)
    {
        r.DrawPanel(Bounds, BevelStyle.RaisedThick, Theme.Face);

        r.Fill(_titleRect, Theme.TitleActive);
        r.DrawText(r.UiBoldFont, _title, _titleRect.X + 5,
            _titleRect.Y + (_titleRect.Height - r.UiBoldFont.LineHeight) / 2, Theme.TitleText);

        foreach (var f in _fields)
        {
            r.DrawText(r.UiFont, f.Label, f.Box.Bounds.X, f.Box.Bounds.Y - r.UiFont.LineHeight - 2, Theme.Text);
            f.Box.Draw(r);
        }

        foreach (var c in _checks) c.Draw(r);

        _ok.Draw(r);
        if (AltLabel != null) _alt.Draw(r);
        _cancel.Draw(r);
    }

    public override Widget? HitTest(Point p) => Bounds.Contains(p) ? this : null;
}
