using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FnaWindow;

/// <summary>
/// An in-app Win 3.1 styled Open / Save As dialog - the in-app replacement for the host OS
/// (comdlg32) dialog. Modal like <see cref="InputDialog"/>: it lives in the PopupLayer, its owner
/// pumps <see cref="Update"/> each frame and closes it on OK/Cancel. Callback-based
/// (OnOk/OnCancel) because a widget modal runs across frames and cannot block like a synchronous
/// host dialog call.
/// </summary>
public sealed class RetroFileDialog : Widget
{
    public Action<string>? OnOk;      // full path chosen
    public Action? OnCancel;

    private enum Kind { Parent, Dir, File, Drive }
    private readonly record struct Entry(Kind Kind, string Display, string FullPath);

    private readonly bool _save;
    private readonly string _pattern;   // e.g. "*.cs" or "*.csproj;*.sln"
    private readonly string _title;

    private string _dir;
    private string _fileName;
    private int _caret;

    private readonly List<Entry> _entries = new();
    private int _sel = -1;
    private int _scroll;
    private bool _loading;
    private int _loadGen;   // bumped per navigation; a stale background read discards itself

    private const int RowH = 16;
    private Rectangle _fieldRect, _listRect, _upRect, _downRect, _okRect, _cancelRect, _titleRect;

    // Drag by the caption, shared with the engine dialogs so every modal moves the same way.
    private readonly TitleDrag _drag = new();

    private enum Btn { None, Ok, Cancel, Up, Down }
    private Btn _armed = Btn.None;          // button the current press started on
    private Btn _pressedVisual = Btn.None;  // button to draw depressed this frame

    public RetroFileDialog(bool save, string pattern, string? initialDir, string? defaultName)
    {
        _save = save;
        _pattern = string.IsNullOrWhiteSpace(pattern) ? "*.*" : pattern;
        _title = save ? "Save As" : "Open";
        _fileName = defaultName ?? "";
        _caret = _fileName.Length;
        _dir = ResolveInitialDir(initialDir);
        BeginLoad();
    }

    private int VisibleRows => Math.Max(1, _listRect.Height / RowH);

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
        int sb = Theme.ScrollBarThickness;
        _listRect = new Rectangle(x + 14, listTop, w - 28 - sb, listBottom - listTop);
        int sbx = _listRect.Right;
        _upRect = new Rectangle(sbx, listTop, sb, sb);
        _downRect = new Rectangle(sbx, listBottom - sb, sb, sb);

        _okRect = new Rectangle(x + w - 170, y + h - 34, 72, 24);
        _cancelRect = new Rectangle(x + w - 88, y + h - 34, 72, 24);
    }

    // -- Update ------------------------------------------------------------
    public override void Update(InputState input, GameTime t)
    {
        Root()?.RequestRedraw(); // keep drawing while the modal is open (engine idle-redraw throttle)

        // Filename field editing.
        foreach (char c in input.TypedChars)
            if (!char.IsControl(c)) { _fileName = _fileName.Insert(_caret, c.ToString()); _caret++; }
        if (input.Pressed(Keys.Back) && _caret > 0) { _fileName = _fileName.Remove(_caret - 1, 1); _caret--; }
        if (input.Pressed(Keys.Delete) && _caret < _fileName.Length) _fileName = _fileName.Remove(_caret, 1);
        if (input.Pressed(Keys.Left) && _caret > 0) _caret--;
        if (input.Pressed(Keys.Right) && _caret < _fileName.Length) _caret++;
        if (input.Pressed(Keys.Home)) _caret = 0;
        if (input.Pressed(Keys.End)) _caret = _fileName.Length;

        // List navigation.
        if (input.Pressed(Keys.Up)) { MoveSel(-1); }
        if (input.Pressed(Keys.Down)) { MoveSel(+1); }
        if (input.Pressed(Keys.PageUp)) { MoveSel(-VisibleRows); }
        if (input.Pressed(Keys.PageDown)) { MoveSel(+VisibleRows); }

        if (input.Pressed(Keys.Escape)) { OnCancel?.Invoke(); return; }
        if (input.Pressed(Keys.Enter)) { AcceptOrOpen(); return; }

        if (input.WheelDelta != 0) Scroll(-input.WheelDelta * 3);

        // Move by the caption, like any Win 3.1 dialog.
        var bounds = Bounds;
        if (_drag.Update(input, _titleRect, Root()?.Bounds ?? Bounds, ref bounds)) { Bounds = bounds; Layout(); }

        var m = input.Mouse;

        // Buttons depress on press and fire on release-if-still-over (classic Win 3.1). Scroll arrows
        // scroll on press; the list selects on press, and a double-click opens.
        if (input.LeftPressed)
        {
            _armed = _okRect.Contains(m) ? Btn.Ok
                   : _cancelRect.Contains(m) ? Btn.Cancel
                   : _upRect.Contains(m) ? Btn.Up
                   : _downRect.Contains(m) ? Btn.Down : Btn.None;

            if (_armed == Btn.Up) Scroll(-1);
            else if (_armed == Btn.Down) Scroll(+1);
            else if (_armed == Btn.None && _listRect.Contains(m))
            {
                int row = _scroll + (m.Y - _listRect.Y) / RowH;
                if (row >= 0 && row < _entries.Count)
                {
                    _sel = row;
                    var e = _entries[row];
                    if (e.Kind == Kind.File) { _fileName = e.Display; _caret = _fileName.Length; }
                    if (input.DoubleClicked) { Activate(e); return; }
                }
            }
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

    private void MoveSel(int delta)
    {
        if (_entries.Count == 0) return;
        _sel = Math.Clamp((_sel < 0 ? 0 : _sel) + delta, 0, _entries.Count - 1);
        if (_sel < _scroll) _scroll = _sel;
        else if (_sel >= _scroll + VisibleRows) _scroll = _sel - VisibleRows + 1;
    }

    private void Scroll(int delta)
    {
        int max = Math.Max(0, _entries.Count - VisibleRows);
        _scroll = Math.Clamp(_scroll + delta, 0, max);
    }

    private Rectangle RectOf(Btn b) => b switch
    {
        Btn.Ok => _okRect, Btn.Cancel => _cancelRect, Btn.Up => _upRect, Btn.Down => _downRect, _ => Rectangle.Empty,
    };

    // The OK/Open action: accept the typed filename, else open the selected entry.
    private void AcceptOrOpen()
    {
        if (_fileName.Trim().Length > 0) Accept();
        else if (_sel >= 0 && _sel < _entries.Count) Activate(_entries[_sel]);
    }

    private void Activate(Entry e)
    {
        switch (e.Kind)
        {
            case Kind.Parent:
            case Kind.Dir:
            case Kind.Drive:
                Navigate(e.FullPath);
                break;
            case Kind.File:
                _fileName = Path.GetFileName(e.FullPath);
                Accept();
                break;
        }
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

    // -- Entry list --------------------------------------------------------
    // Directory reads are the one slow thing here (a network path can stall for seconds), so they run
    // on a background thread and the result is marshaled back onto the game thread via MainThread -
    // the whole point of the UI/background split. The dialog stays responsive and shows "reading...".
    private void BeginLoad()
    {
        int gen = ++_loadGen;   // this read's generation; a newer navigation supersedes it
        _loading = true;
        _entries.Clear();
        _sel = -1;
        _scroll = 0;

        string dir = _dir, pattern = _pattern; // capture immutables for the worker
        Task.Run(() =>
        {
            var list = BuildEntries(dir, pattern); // heavy I/O off the game thread
            MainThread.Post(() =>                   // apply on the game thread
            {
                if (gen != _loadGen) return;        // a later navigation already won
                _entries.Clear();
                _entries.AddRange(list);
                _loading = false;
            });
        });
    }

    private static List<Entry> BuildEntries(string dir, string pattern)
    {
        var list = new List<Entry>();
        try
        {
            var parent = Directory.GetParent(dir);
            if (parent != null) list.Add(new Entry(Kind.Parent, "[..]", parent.FullName));
        }
        catch { }

        foreach (string d in SafeDirs(dir))
            list.Add(new Entry(Kind.Dir, "[" + Path.GetFileName(d) + "]", d));

        foreach (string f in SafeFiles(dir))
            if (MatchesFilter(pattern, Path.GetFileName(f)))
                list.Add(new Entry(Kind.File, Path.GetFileName(f), f));

        foreach (string drv in SafeDrives())
            list.Add(new Entry(Kind.Drive, "[-" + drv.TrimEnd('\\', '/').Replace(":", "") + "-]", drv));

        return list;
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try { var a = Directory.GetDirectories(dir); Array.Sort(a, StringComparer.OrdinalIgnoreCase); return a; }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeFiles(string dir)
    {
        try { var a = Directory.GetFiles(dir); Array.Sort(a, StringComparer.OrdinalIgnoreCase); return a; }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeDrives()
    {
        var list = new List<string>();
        try
        {
            // Do NOT probe d.IsReady here: a not-ready optical/removable/disconnected-network drive
            // can block for seconds (this runs on the game thread when the dialog opens). Just list
            // the roots; Navigate() validates with Directory.Exists when one is actually clicked.
            foreach (var d in DriveInfo.GetDrives())
                try { list.Add(d.RootDirectory.FullName); } catch { }
        }
        catch { }
        return list;
    }

    private static bool MatchesFilter(string pattern, string name)
    {
        foreach (string patRaw in pattern.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string pat = patRaw.Trim();
            if (pat is "*.*" or "*") return true;
            if (pat.StartsWith("*.", StringComparison.Ordinal)
                && name.EndsWith(pat.Substring(1), StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(pat, name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string ResolveInitialDir(string? initial)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial)) return Path.GetFullPath(initial);
        }
        catch { }
        return Directory.GetCurrentDirectory();
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

        // List.
        r.DrawPanel(_listRect, BevelStyle.SunkenThick, Theme.WindowBg);
        if (_loading && _entries.Count == 0)
            r.DrawText(font, "reading...", _listRect.X + 6, _listRect.Y + 3, Theme.TextDisabled);
        int rows = VisibleRows;
        for (int i = 0; i < rows; i++)
        {
            int idx = _scroll + i;
            if (idx >= _entries.Count) break;
            var e = _entries[idx];
            int ry = _listRect.Y + i * RowH;
            bool selected = idx == _sel;
            if (selected) r.Fill(_listRect.X + 2, ry, _listRect.Width - 4, RowH, Theme.TitleActive);
            Color fg = selected ? Theme.TitleText : Theme.Text;
            r.DrawText(font, e.Display, _listRect.X + 6, ry + (RowH - font.LineHeight) / 2, fg);
        }

        // Scrollbar arrows + proportional thumb.
        DrawArrow(r, _upRect, up: true, pressed: _pressedVisual == Btn.Up);
        DrawArrow(r, _downRect, up: false, pressed: _pressedVisual == Btn.Down);
        int trackTop = _upRect.Bottom, trackH = _downRect.Y - _upRect.Bottom;
        if (_entries.Count > rows && trackH > 0)
        {
            int thumbH = Math.Max(12, trackH * rows / _entries.Count);
            int max = Math.Max(1, _entries.Count - rows);
            int thumbY = trackTop + (trackH - thumbH) * _scroll / max;
            r.DrawPanel(new Rectangle(_upRect.X, thumbY, _upRect.Width, thumbH), BevelStyle.RaisedThin, Theme.Face);
        }

        DrawButton(r, _okRect, _save ? "Save" : "Open", _pressedVisual == Btn.Ok);
        DrawButton(r, _cancelRect, "Cancel", _pressedVisual == Btn.Cancel);
    }

    private static void DrawArrow(Win31Renderer r, Rectangle rect, bool up, bool pressed)
    {
        r.DrawPanel(rect, pressed ? BevelStyle.SunkenThin : BevelStyle.RaisedThin, Theme.Face);
        int off = pressed ? 1 : 0; // nudge the glyph into the sunken bevel
        int cx = rect.X + rect.Width / 2 + off, cy = rect.Y + rect.Height / 2 + off;
        for (int i = 0; i < 4; i++)
        {
            int wdt = up ? i : 3 - i;
            r.Fill(cx - wdt, cy - 2 + i, wdt * 2 + 1, 1, Theme.Text);
        }
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
