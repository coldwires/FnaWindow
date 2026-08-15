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
/// <para>Setting <see cref="AltLabel"/> adds a third button between OK and Cancel, for the classic
/// three-way prompt (Save / Discard / Cancel when closing a modified file).</para>
/// </summary>
public sealed class InputDialog : Widget
{
    private const int W = 320;

    private readonly string _title, _prompt;
    private readonly TextField _field;
    private int _promptY;

    private Rectangle _titleRect;

    private readonly PushButton _ok = new("OK");
    private readonly PushButton _alt = new("");
    private readonly PushButton _cancel = new("Cancel");
    private readonly TitleDrag _drag = new();

    public Action<string>? OnOk;
    public Action? OnCancel;
    public bool NoField;                                  // confirm-only (no text field)
    public bool OkOnly;                                   // single button, centred (About, notices)
    public string OkLabel = "OK", CancelLabel = "Cancel";

    /// <summary>Label for an optional third button; null (the default) leaves it off.</summary>
    public string? AltLabel;
    public Action? OnAlt;

    public InputDialog(string title, string prompt, string initial)
    {
        _title = title;
        _prompt = prompt;
        _field = new TextField(initial ?? "") { MaxLength = 64 };
    }

    /// <summary>What the field holds right now, for an owner that reads it before OK.</summary>
    public string FieldText => _field.Text;

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
        _field.Bounds = new Rectangle(x + 14, y + contentTop + lineH + 2, W - 28, 20); // only when !NoField

        _ok.Label = OkLabel;
        _cancel.Label = CancelLabel;
        _alt.Label = AltLabel ?? "";
        if (OkOnly)
        {
            _ok.Bounds = new Rectangle(x + (W - 72) / 2, y + buttonsTop, 72, 22);
            _alt.Bounds = _cancel.Bounds = Rectangle.Empty;
        }
        else
        {
            // Right-aligned row, 12px between buttons; the third slot runs wider so a label
            // like "Don't Save" is not crammed into the OK/Cancel width.
            const int gap = 12, btnW = 72, altW = 88;
            _cancel.Bounds = new Rectangle(x + W - 16 - btnW, y + buttonsTop, btnW, 22);
            _alt.Bounds = AltLabel != null
                ? new Rectangle(_cancel.Bounds.X - gap - altW, y + buttonsTop, altW, 22)
                : Rectangle.Empty;
            _ok.Bounds = new Rectangle((AltLabel != null ? _alt.Bounds.X : _cancel.Bounds.X) - gap - btnW, y + buttonsTop, btnW, 22);
        }
    }

    public override void Update(InputState input, GameTime t)
    {
        Root()?.RequestRedraw(); // keep the caret blinking while the modal is open (idle throttle)

        if (!NoField) _field.Update(input, t);

        if (input.Pressed(Keys.Enter)) { OnOk?.Invoke(_field.Text); return; }
        // Esc still dismisses a one-button box; with nothing to cancel it means the same as OK.
        if (input.Pressed(Keys.Escape)) { if (OkOnly) OnOk?.Invoke(_field.Text); else OnCancel?.Invoke(); return; }

        // Move by the title bar, like any Win 3.1 dialog.
        var bounds = Bounds;
        if (_drag.Update(input, _titleRect, Root()?.Bounds ?? Bounds, ref bounds)) { Bounds = bounds; Layout(); }

        // Buttons act on release, showing pressed while held.
        if (_ok.Update(input)) { OnOk?.Invoke(_field.Text); return; }
        if (!OkOnly && AltLabel != null && _alt.Update(input)) { OnAlt?.Invoke(); return; }
        if (!OkOnly && _cancel.Update(input)) { OnCancel?.Invoke(); return; }
    }

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

        if (!NoField) _field.Draw(r);

        _ok.Draw(r);
        if (!OkOnly)
        {
            if (AltLabel != null) _alt.Draw(r);
            _cancel.Draw(r);
        }
    }

    public override Widget? HitTest(Point p) => Bounds.Contains(p) ? this : null;
}
