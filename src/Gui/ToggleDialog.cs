using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FnaWindow;

/// <summary>
/// A modal list of on/off options, each a <see cref="CheckBox"/>, grouped under optional headings.
/// The dialog owns no settings of its own: each option is a label plus a getter and a setter, so an
/// app points it at whatever flags it has and nothing about those flags leaks in here. Toggling
/// applies immediately (the setter runs on the click), so the button just closes.
/// <para>Movable by its caption and closed by its button, Esc, or Enter, like the other modals.</para>
/// </summary>
public sealed class ToggleDialog : Widget
{
    /// <summary>One row: a toggle bound to the caller's state, or a heading when Get is null.</summary>
    public sealed class Option
    {
        public string Label = "";
        public Func<bool>? Get;
        public Action<bool>? Set;
        public string? Note;            // one dim line under the row, for what the option does

        public static Option Toggle(string label, Func<bool> get, Action<bool> set, string? note = null)
            => new() { Label = label, Get = get, Set = set, Note = note };

        public static Option Heading(string label) => new() { Label = label };

        public bool IsHeading => Get == null;
    }

    private const int W = 340, RowH = 20, NoteH = 14, Pad = 14;

    private readonly string _title;
    private readonly List<Option> _options;
    private readonly List<CheckBox?> _boxes = new();   // null for a heading row

    private readonly PushButton _close = new("Close");
    private readonly TitleDrag _drag = new();

    private Rectangle _titleRect;
    private int _listTop;

    public Action? OnClose;

    public ToggleDialog(string title, IEnumerable<Option> options)
    {
        _title = title;
        _options = new List<Option>(options);
        foreach (var o in _options)
            _boxes.Add(o.IsHeading ? null : new CheckBox(o.Label, o.Get!()));
    }

    private int RowHeight(Option o) => o.IsHeading ? RowH : RowH + (o.Note != null ? NoteH : 0);

    public override void Layout()
    {
        int contentH = 0;
        foreach (var o in _options) contentH += RowHeight(o);

        int listTop = 30;
        int h = listTop + contentH + 12 + 22 + Pad;

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
        _listTop = y + listTop;

        int ry = _listTop;
        for (int i = 0; i < _options.Count; i++)
        {
            var o = _options[i];
            if (_boxes[i] is { } cb) cb.Bounds = new Rectangle(x + Pad + 8, ry, W - 2 * Pad - 8, RowH);
            ry += RowHeight(o);
        }

        _close.Bounds = new Rectangle(x + W - Pad - 72, Bounds.Bottom - Pad - 22, 72, 22);
    }

    public override void Update(InputState input, GameTime t)
    {
        Root()?.RequestRedraw(); // modal: keep drawing while it is up (idle throttle)

        if (input.Pressed(Keys.Escape) || input.Pressed(Keys.Enter)) { OnClose?.Invoke(); return; }

        var bounds = Bounds;
        if (_drag.Update(input, _titleRect, Root()?.Bounds ?? Bounds, ref bounds)) { Bounds = bounds; Layout(); }

        for (int i = 0; i < _options.Count; i++)
        {
            if (_boxes[i] is not { } cb) continue;
            if (cb.Update(input)) _options[i].Set!(cb.Checked);
        }

        if (_close.Update(input)) { OnClose?.Invoke(); return; }
    }

    public override void Draw(Win31Renderer r)
    {
        r.DrawPanel(Bounds, BevelStyle.RaisedThick, Theme.Face);

        r.Fill(_titleRect, Theme.TitleActive);
        r.DrawText(r.UiBoldFont, _title, _titleRect.X + 5,
            _titleRect.Y + (_titleRect.Height - r.UiBoldFont.LineHeight) / 2, Theme.TitleText);

        int ry = _listTop;
        for (int i = 0; i < _options.Count; i++)
        {
            var o = _options[i];
            if (_boxes[i] is { } cb)
            {
                // Re-read each frame: a toggle can also be changed from a menu while this is open.
                cb.Checked = o.Get!();
                cb.Draw(r);

                if (o.Note != null)
                    r.DrawText(r.UiFont, o.Note, Bounds.X + Pad + 8 + CheckBox.BoxSize + 6,
                        ry + RowH - 2, Theme.TextDisabled);
            }
            else
            {
                r.DrawText(r.UiBoldFont, o.Label, Bounds.X + Pad,
                    ry + (RowH - r.UiBoldFont.LineHeight) / 2, Theme.Text);
            }
            ry += RowHeight(o);
        }

        _close.Draw(r);
    }

    public override Widget? HitTest(Point p) => Bounds.Contains(p) ? this : null;
}
