using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FnaWindow;

/// <summary>
/// An in-app Win 3.1 styled Open / Save As dialog - the in-app replacement for the host OS
/// (comdlg32) dialog. Modal like <see cref="InputDialog"/>: it lives in the PopupLayer, its owner
/// pumps <see cref="Update"/> each frame and closes it on OK/Cancel. Callback-based
/// (OnOk/OnCancel) because a widget modal runs across frames and cannot block like a synchronous
/// host dialog call.
///
/// The list and the directory reading are not implemented here: they are <see cref="ListBox"/> and
/// <see cref="DirectoryLoader"/>, shared with any app that browses files.
/// </summary>
public sealed class RetroFileDialog : Widget
{
    public Action<string>? OnOk;      // full path chosen
    public Action? OnCancel;

    private readonly bool _save;
    private readonly string _pattern;   // e.g. "*.cs" or "*.csproj;*.sln"
    private readonly string _title;

    private string _dir;
    private string _fileName;
    private int _caret;

    private readonly List<FsEntry> _entries = new();
    private readonly ListBox _list = new() { HandleKeys = false };
    private readonly DirectoryLoader _loader = new();

    private Rectangle _fieldRect, _okRect, _cancelRect, _titleRect;

    // Drag by the caption, shared with the engine dialogs so every modal moves the same way.
    private readonly TitleDrag _drag = new();

    private enum Btn { None, Ok, Cancel }
    private Btn _armed = Btn.None;          // button the current press started on
    private Btn _pressedVisual = Btn.None;  // button to draw depressed this frame

    public RetroFileDialog(bool save, string pattern, string? initialDir, string? defaultName)
    {
        _save = save;
        _pattern = string.IsNullOrWhiteSpace(pattern) ? "*.*" : pattern;
        _title = save ? "Save As" : "Open";
        _fileName = defaultName ?? "";
        _caret = _fileName.Length;
        _dir = DirectoryListing.ResolveDir(initialDir);

        Add(_list);
        // Selecting a file puts its name in the field; selecting a folder leaves the field alone,
        // so a typed name survives browsing around for the folder to put it in.
        _list.SelectionChanged = i =>
        {
            if (i >= 0 && i < _entries.Count && _entries[i].Kind == FsEntryKind.File)
            {
                _fileName = _entries[i].Name;
                _caret = _fileName.Length;
            }
        };
        _list.Activated = i => { if (i >= 0 && i < _entries.Count) Activate(_entries[i]); };

        BeginLoad();
    }

    // -- Layout ------------------------------------------------------------
    public override void Layout()
    {
        int w = 440, h = 320;
        // Center within the whole window. Use Root().Bounds, not our own Bounds, so re-layout on a
        // window resize does not re-center off the already-shrunk dialog rect. Once the user has
        // dragged the dialog by its caption, keep where they put it (clamped into view).
        var area = Root()?.Bounds ?? Parent?.Bounds ?? new Rectangle(0, 0, 1024, 768);
        int x, y;
        if (_drag.Moved)
        {
            x = Math.Clamp(Bounds.X, area.X, Math.Max(area.X, area.Right - w));
            y = Math.Clamp(Bounds.Y, area.Y, Math.Max(area.Y, area.Bottom - h));
        }
        else
        {
            x = area.X + (area.Width - w) / 2;
            y = area.Y + (area.Height - h) / 3;
        }
        Bounds = new Rectangle(x, y, w, h);

        // The caption strip: the title text sits at y+14, so this covers it and the padding around it.
        _titleRect = new Rectangle(x + 4, y + 4, w - 8, 26);

        _fieldRect = new Rectangle(x + 14, y + 58, w - 28, 20);

        int listTop = y + 108;
        int listBottom = y + h - 44;
        _list.Bounds = new Rectangle(x + 14, listTop, w - 28, listBottom - listTop);

        _okRect = new Rectangle(x + w - 170, y + h - 34, 72, 24);
        _cancelRect = new Rectangle(x + w - 88, y + h - 34, 72, 24);

        base.Layout();
    }

    // -- Update ------------------------------------------------------------
    public override void Update(InputState input, GameTime t)
    {
        Root()?.RequestRedraw(); // keep drawing while the modal is open (engine idle-redraw throttle)

        base.Update(input, t);   // the list: mouse selection, double-click, wheel, its scrollbar

        // Filename field editing.
        foreach (char c in input.TypedChars)
            if (!char.IsControl(c)) { _fileName = _fileName.Insert(_caret, c.ToString()); _caret++; }
        if (input.Pressed(Keys.Back) && _caret > 0) { _fileName = _fileName.Remove(_caret - 1, 1); _caret--; }
        if (input.Pressed(Keys.Delete) && _caret < _fileName.Length) _fileName = _fileName.Remove(_caret, 1);
        if (input.Pressed(Keys.Left) && _caret > 0) _caret--;
        if (input.Pressed(Keys.Right) && _caret < _fileName.Length) _caret++;
        if (input.Pressed(Keys.Home)) _caret = 0;
        if (input.Pressed(Keys.End)) _caret = _fileName.Length;

        // List navigation is driven from here rather than by the list itself: Enter has to mean
        // "accept the typed name, or else open the selection", which is a dialog rule, not a list one.
        if (input.Pressed(Keys.Up)) _list.MoveSelection(-1);
        if (input.Pressed(Keys.Down)) _list.MoveSelection(+1);
        if (input.Pressed(Keys.PageUp)) _list.MoveSelection(-_list.VisibleRows);
        if (input.Pressed(Keys.PageDown)) _list.MoveSelection(+_list.VisibleRows);

        if (input.Pressed(Keys.Escape)) { OnCancel?.Invoke(); return; }
        if (input.Pressed(Keys.Enter)) { AcceptOrOpen(); return; }

        // Move by the caption, like any Win 3.1 dialog.
        var bounds = Bounds;
        if (_drag.Update(input, _titleRect, Root()?.Bounds ?? Bounds, ref bounds)) { Bounds = bounds; Layout(); }

        var m = input.Mouse;

        // Buttons depress on press and fire on release-if-still-over (classic Win 3.1).
        if (input.LeftPressed)
        {
            _armed = _okRect.Contains(m) ? Btn.Ok
                   : _cancelRect.Contains(m) ? Btn.Cancel : Btn.None;
        }

        // Depress visual: the armed button, while the mouse is still held down over it.
        _pressedVisual = input.LeftDown && _armed != Btn.None && RectOf(_armed).Contains(m) ? _armed : Btn.None;

        if (input.LeftReleased)
        {
            if (_armed == Btn.Ok && _okRect.Contains(m)) { _armed = Btn.None; AcceptOrOpen(); return; }
            if (_armed == Btn.Cancel && _cancelRect.Contains(m)) { _armed = Btn.None; OnCancel?.Invoke(); return; }
            _armed = Btn.None;
        }
    }

    private Rectangle RectOf(Btn b) => b switch
    {
        Btn.Ok => _okRect, Btn.Cancel => _cancelRect, _ => Rectangle.Empty,
    };

    // The OK/Open action: accept the typed filename, else open the selected entry.
    private void AcceptOrOpen()
    {
        if (_fileName.Trim().Length > 0) Accept();
        else if (_list.Selected >= 0 && _list.Selected < _entries.Count) Activate(_entries[_list.Selected]);
    }

    private void Activate(FsEntry e)
    {
        if (e.IsNavigable) Navigate(e.FullPath);
        else { _fileName = e.Name; Accept(); }
    }

    private void Accept()
    {
        string name = _fileName.Trim();
        if (name.Length == 0) return;

        string candidate;
        try { candidate = Path.IsPathRooted(name) ? name : Path.Combine(_dir, name); }
        catch { return; }

        // Typing a directory name navigates into it rather than accepting.
        if (Directory.Exists(candidate)) { Navigate(candidate); _fileName = ""; _caret = 0; return; }

        if (_save && !Path.HasExtension(candidate)) candidate += ".cs"; // default extension
        if (!_save && !File.Exists(candidate)) return;                  // Open requires an existing file

        try { OnOk?.Invoke(Path.GetFullPath(candidate)); }
        catch { OnOk?.Invoke(candidate); }
    }

    private void Navigate(string newDir)
    {
        try
        {
            string full = Path.GetFullPath(newDir);
            if (Directory.Exists(full)) { _dir = full; BeginLoad(); }
        }
        catch { }
    }

    // Reading a directory is the one slow thing here (a network path can stall for seconds), so it
    // runs off the game thread and applies back on it. DirectoryLoader owns that, including
    // discarding a read the user has already navigated away from.
    private void BeginLoad()
    {
        _entries.Clear();
        _list.Reset();
        _loader.Begin(_dir, entries =>
        {
            _entries.Clear();
            _entries.AddRange(entries);
            var display = new List<string>(_entries.Count);
            foreach (var e in _entries) display.Add(e.Display);
            _list.SetItems(display);
            _list.Layout();
            Root()?.RequestRedraw();
        }, _pattern);
    }

    // -- Draw --------------------------------------------------------------
    public override void Draw(Win31Renderer r)
    {
        r.DrawPanel(Bounds, BevelStyle.RaisedThick, Theme.Face);
        var font = r.UiFont;
        int x = Bounds.X, y = Bounds.Y;

        r.DrawText(r.UiBoldFont, _title, x + 14, y + 14, Theme.Text);
        r.DrawText(font, "File Name:", x + 14, y + 40, Theme.Text);
        r.DrawText(font, "Folder: " + TruncateLeft(font, _dir, Bounds.Width - 70), x + 14, y + 86, Theme.Text);

        // Filename field.
        r.DrawPanel(_fieldRect, BevelStyle.SunkenThick, Theme.WindowBg);
        int tx = _fieldRect.X + 4, ty = _fieldRect.Y + (_fieldRect.Height - font.LineHeight) / 2;
        r.DrawText(font, _fileName, tx, ty, Theme.Text);
        int cx = tx + font.MeasureWidth(_fileName.Substring(0, _caret));
        r.Fill(cx, _fieldRect.Bottom - 4, font.MeasureWidth("n"), 2, Theme.Text);

        base.Draw(r); // the list (well, rows, scrollbar)

        if (_loader.Loading && _entries.Count == 0)
            r.DrawText(font, "reading...", _list.Bounds.X + 6, _list.Bounds.Y + 3, Theme.TextDisabled);

        DrawButton(r, _okRect, _save ? "Save" : "Open", _pressedVisual == Btn.Ok);
        DrawButton(r, _cancelRect, "Cancel", _pressedVisual == Btn.Cancel);
    }

    private static void DrawButton(Win31Renderer r, Rectangle rect, string label, bool pressed)
    {
        // Pressed = sunken bevel + content nudged (+1,+1), the way Win 3.1 depresses a button.
        r.DrawPanel(rect, pressed ? BevelStyle.SunkenThin : BevelStyle.RaisedThin, Theme.Face);
        var inner = rect;
        if (pressed) inner.Offset(1, 1);
        int tw = r.UiFont.MeasureWidth(label);
        r.DrawText(r.UiFont, label, inner.X + (inner.Width - tw) / 2,
            inner.Y + (inner.Height - r.UiFont.LineHeight) / 2, Theme.Text);
    }

    // Trim a long path from the left, keeping the tail visible: "...\dir\sub".
    private static string TruncateLeft(BitmapFont font, string s, int maxW)
    {
        if (font.MeasureWidth(s) <= maxW) return s;
        int start = 0;
        while (start < s.Length && font.MeasureWidth("..." + s.Substring(start)) > maxW) start++;
        return "..." + s.Substring(Math.Min(start, s.Length));
    }

    public override Widget? HitTest(Point p) => Bounds.Contains(p) ? this : null;
}
