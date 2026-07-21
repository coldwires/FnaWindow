using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FnaWindow;

/// <summary>
/// The plain text editing widget: a SunkenThick white well over a fixed monospace grid, with a
/// caret, selection, both scrollbars, undo/redo, the OS clipboard, and the standard key and mouse
/// model. It knows nothing about languages - a plain notepad uses it as is.
///
/// Everything a richer editor adds (syntax coloring, squiggles, completion popups, jump-to-symbol)
/// hangs off the seams below, so a subclass never re-implements caret math or the key model:
/// <list type="bullet">
/// <item><see cref="ColorLine"/> - per-character foreground colors for one line.</item>
/// <item><see cref="DrawLineBackground"/> / <see cref="DrawLineOverlay"/> - behind and over one
/// line's text (bands, squiggles).</item>
/// <item><see cref="DrawOverlays"/> - over the whole widget (popups, tooltips).</item>
/// <item><see cref="MouseIntercept"/> / <see cref="ClickIntercept"/> - take the mouse before the
/// caret does.</item>
/// <item><see cref="WordWrap"/> - lay long lines out over several rows. Everything below works in
/// visual rows, so it is one code path either way; a subclass that positions anything itself
/// should go through <see cref="PointFor"/> or <see cref="RowIndexOf"/> rather than assuming one
/// row per line.</item>
/// <item><see cref="OnBufferChanged"/> - react to an edit (the base keeps its own metrics current).</item>
/// <item>Override <c>OnKey</c> / <c>OnChar</c> and call <c>base</c> for the plain behaviour, and
/// override <see cref="EnterKey"/> / <see cref="Backspace"/> for smarter versions.</item>
/// </list>
/// </summary>
public class TextArea : Widget
{
    protected const int CellW = Theme.EditorCellW; // 8
    protected const int CellH = Theme.EditorCellH; // 15

    /// <summary>
    /// App-selected editor font, or null for the engine default (fixedsys). All fonts meant for the
    /// text area share the fixed 8x15 cell, so a swap needs no relayout - the next Draw just uses
    /// the new glyphs.
    /// </summary>
    public static BitmapFont? FontOverride;

    public TextBuffer Buf { get; }

    /// <summary>The caret position (read-only; move it with <see cref="GoTo"/>).</summary>
    public Position Caret => _caret;

    /// <summary>Set by a host that covers this widget at the pointer: ignore the mouse this frame
    /// (clicks, wheel, scrollbars) while keyboard and the caret blink carry on.</summary>
    public bool MouseBlocked;

    /// <summary>Faint band across the caret's line when there is no selection.</summary>
    public bool HighlightCurrentLine = true;

    /// <summary>
    /// Wrap long lines to the width of the text region instead of scrolling sideways. The buffer is
    /// untouched - no line breaks are inserted - so this is purely how the text is laid out: a
    /// logical line becomes one or more visual rows, broken after a space where there is one and
    /// mid-word only when a single word is wider than the widget. The horizontal scrollbar goes
    /// away while it is on, and vertical motion (arrows, page keys, the wheel, Home/End) works in
    /// visual rows, which is what makes wrapped text feel right.
    /// </summary>
    public bool WordWrap
    {
        get => _wordWrap;
        set
        {
            if (_wordWrap == value) return;
            _wordWrap = value;
            ScrollCol = 0;
            Layout();          // re-measures the text region, then rebuilds the rows for it
            EnsureVisible();   // keep the caret on screen across the switch
            Root()?.RequestRedraw();
        }
    }

    /// <summary>Raised when the caret moves, for a status bar showing Ln/Col.</summary>
    public Action<Position>? CaretMoved;

    /// <summary>Raised on a click in the text, for a host that wants to come to the front.</summary>
    public Action? OnActivate;

    protected readonly ScrollBar VBar = new();
    protected readonly ScrollBar HBar = new() { Horizontal = true };

    // Geometry, recomputed in Layout: the outer well, the text region inside it (scrollbars
    // excluded), the first cell's top-left, and how many whole cells fit.
    protected Rectangle Well, TextRect;
    protected int OriginX, OriginY, VisLines, VisCols;
    protected int ScrollLine, ScrollCol;

    private Position _caret;
    private Position _anchor;              // selection anchor (== caret when there is no selection)
    private int _goalCol;                  // the column WITHIN ITS ROW a vertical move wants
    private bool _verticalMove;            // true during a vertical move (keeps _goalCol)
    private bool _selecting;               // a drag-select is in progress
    private double _blink;
    private bool _blinkOn = true;
    private int _maxLineLen = 1;
    private bool _wordWrap;

    /// <summary>
    /// One visual row: the slice [Start, End) of logical line <paramref name="Line"/> that occupies
    /// a single screen row. With <see cref="WordWrap"/> off every line is exactly one row spanning
    /// the whole line, so the one code path below serves both modes and there is no second,
    /// wrap-only version of the caret, hit-test or draw logic to drift out of step.
    /// </summary>
    private readonly record struct VisRow(int Line, int Start, int End);

    // Every visual row in the buffer, in order. ScrollLine indexes THIS list, not the line list.
    private readonly List<VisRow> _rows = new();
    // _firstRow[i] is the index of line i's first row; _firstRow[LineCount] == _rows.Count.
    private int[] _firstRow = { 0, 0 };
    private int _wrapWidth;                // the column width _rows was built for (0 = unwrapped)

    public TextArea(TextBuffer buffer)
    {
        Buf = buffer;
        Add(VBar); Add(HBar);
        VBar.OnChange = v => ScrollLine = v;
        HBar.OnChange = v => ScrollCol = v;
        Buf.Changed += OnBufferChanged;
        OnBufferChanged(default);
    }

    protected bool Focused => Root()?.Focused == this;

    /// <summary>True when a selection exists.</summary>
    protected bool HasSel => _caret.CompareTo(_anchor) != 0;

    /// <summary>The selection in document order (both ends equal when there is none).</summary>
    protected (Position a, Position b) Sel()
        => _caret.CompareTo(_anchor) <= 0 ? (_caret, _anchor) : (_anchor, _caret);

    /// <summary>The selected text, or "" when there is no selection.</summary>
    public string SelectedText
    {
        get { if (!HasSel) return ""; var (a, b) = Sel(); return Buf.GetText(a, b); }
    }

    // -- Seams -------------------------------------------------------------

    /// <summary>
    /// Per-character foreground colors for one line, or null for plain <see cref="Theme.Text"/>
    /// throughout (the default). The array must be at least <c>text.Length</c> long.
    /// </summary>
    protected virtual Color[]? ColorLine(int line, string text) => null;

    /// <summary>Drawn behind one line's text, before the current-line band and the selection.</summary>
    protected virtual void DrawLineBackground(Win31Renderer r, int line, int y) { }

    /// <summary>Drawn over one line's text (squiggles and the like). <paramref name="firstCol"/> and
    /// <paramref name="lastCol"/> are the columns actually on screen in this row - clip to them, or
    /// a wrapped line draws its overlay across every one of its rows.</summary>
    protected virtual void DrawLineOverlay(Win31Renderer r, int line, string text, int y,
                                           int firstCol, int lastCol) { }

    /// <summary>Drawn over the whole widget, after the scrollbars (popups, tooltips).</summary>
    protected virtual void DrawOverlays(Win31Renderer r) { }

    /// <summary>Called after every edit. The base rebuilds the visual rows and the scroll extent;
    /// override (and call base) to update any per-line state of your own.</summary>
    protected virtual void OnBufferChanged(TextChange change) => BuildRows();

    /// <summary>Take the mouse before the caret does; return true to consume this frame's mouse.</summary>
    protected virtual bool MouseIntercept(InputState input) => false;

    /// <summary>Handle a click at <paramref name="p"/> yourself; return true to skip caret placement.</summary>
    protected virtual bool ClickIntercept(Position p, InputState input) => false;

    /// <summary>Called after the caret moves, before <see cref="CaretMoved"/>.</summary>
    protected virtual void OnCaretMoved() { }

    // -- Visual rows -------------------------------------------------------

    /// <summary>
    /// Rebuilds the row list. Unwrapped that is one row per line; wrapped, each line is cut into
    /// screen-width pieces. Runs on every edit and whenever the usable width changes, walking the
    /// whole buffer - the same cost the horizontal scroll extent already had.
    /// </summary>
    private void BuildRows()
    {
        // Before the first Layout there is no real width to wrap to; stay unwrapped until there is
        // (Layout rebuilds the moment it knows better).
        int width = _wordWrap && VisCols > 1 ? VisCols : 0;
        _wrapWidth = width;

        _rows.Clear();
        int n = Buf.LineCount;
        if (_firstRow.Length < n + 1) _firstRow = new int[n + 1];
        _maxLineLen = 1;

        for (int i = 0; i < n; i++)
        {
            _firstRow[i] = _rows.Count;
            string line = Buf.Line(i);
            _maxLineLen = Math.Max(_maxLineLen, line.Length + 1);

            if (width == 0 || line.Length <= width) { _rows.Add(new VisRow(i, 0, line.Length)); continue; }

            int start = 0;
            while (start < line.Length)
            {
                int end = Math.Min(line.Length, start + width);
                if (end < line.Length) end = BreakAt(line, start, end);
                _rows.Add(new VisRow(i, start, end));
                start = end;
            }
        }
        _firstRow[n] = _rows.Count;
    }

    /// <summary>Where to cut a too-long row: just after the last space in it, so words stay whole.
    /// A single word wider than the widget has no break to find and is cut at the edge.</summary>
    private static int BreakAt(string line, int start, int hardEnd)
    {
        for (int i = hardEnd; i > start; i--)
            if (line[i - 1] == ' ' || line[i - 1] == '\t') return i;
        return hardEnd;
    }

    /// <summary>The index into the row list of the row the position sits on. A position exactly on a
    /// wrap point belongs to the row that follows, which is where the caret is drawn.</summary>
    protected int RowIndexOf(Position p)
    {
        int line = Math.Clamp(p.Line, 0, Buf.LineCount - 1);
        int first = _firstRow[line], last = _firstRow[line + 1] - 1;
        for (int i = first; i < last; i++)
            if (p.Col < _rows[i].End) return i;
        return last;
    }

    /// <summary>Total visual rows in the buffer - the vertical scroll extent.</summary>
    protected int RowCount => _rows.Count;

    /// <summary>The visual row at <paramref name="index"/>: which logical line it belongs to and the
    /// column span [Start, End) of that line it shows. Unwrapped that is always the whole line.</summary>
    protected (int Line, int Start, int End) RowAt(int index)
    {
        var row = _rows[Math.Clamp(index, 0, _rows.Count - 1)];
        return (row.Line, row.Start, row.End);
    }

    // -- Layout ------------------------------------------------------------

    /// <summary>I-beam over the text region (not the scrollbars), else defer to parent/default.</summary>
    public override string? CursorKey(Point p) => TextRect.Contains(p) ? "ibeam" : null;

    public override void Layout()
    {
        Well = Bounds;
        var inner = Win31Renderer.Inset(Well, Win31Renderer.Thickness(BevelStyle.SunkenThick));
        int t = Theme.ScrollBarThickness;

        // Wrapped text never scrolls sideways, so the horizontal bar goes away and both the text
        // region and the vertical bar take the strip it would have used.
        HBar.Visible = !_wordWrap;
        int bottomStrip = HBar.Visible ? t : 0;

        VBar.Bounds = new Rectangle(inner.Right - t, inner.Y, t, inner.Height - bottomStrip);
        HBar.Bounds = HBar.Visible
            ? new Rectangle(inner.X, inner.Bottom - t, inner.Width - t, t)
            : Rectangle.Empty;
        VBar.Layout(); HBar.Layout();

        TextRect = new Rectangle(inner.X, inner.Y, inner.Width - t, inner.Height - bottomStrip);
        OriginX = TextRect.X + Theme.EditorPaddingLeft;
        OriginY = TextRect.Y + Theme.EditorPaddingTop;
        VisLines = Math.Max(1, (TextRect.Bottom - OriginY) / CellH);
        VisCols = Math.Max(1, (TextRect.Right - OriginX) / CellW);

        // A resize changes how the text wraps, so the rows have to follow the width.
        if (_wrapWidth != (_wordWrap && VisCols > 1 ? VisCols : 0)) BuildRows();
    }

    // -- Update ------------------------------------------------------------

    public override void Update(InputState input, GameTime t)
    {
        // Scrollbars first (they may consume the mouse over their strips).
        SyncScrollbars();
        if (!MouseBlocked)
        {
            VBar.Update(input, t);
            if (HBar.Visible) HBar.Update(input, t);
        }

        _blink += t.ElapsedGameTime.TotalMilliseconds;
        if (_blink >= Theme.CaretBlinkMs) { _blink -= Theme.CaretBlinkMs; _blinkOn = !_blinkOn; }

        if (Root()?.Popup.BlocksInput == true) return;
        if (!MouseBlocked) HandleMouse(input);
    }

    private void SyncScrollbars()
    {
        VBar.ContentSize = _rows.Count;   // rows, not lines: one wrapped line can be several
        VBar.ViewSize = VisLines;
        VBar.Value = ScrollLine = Math.Clamp(ScrollLine, 0, VBar.MaxValue);

        if (!HBar.Visible) { ScrollCol = 0; return; }
        HBar.ContentSize = _maxLineLen;
        HBar.ViewSize = VisCols;
        HBar.Value = ScrollCol = Math.Clamp(ScrollCol, 0, HBar.MaxValue);
    }

    private void HandleMouse(InputState input)
    {
        var m = input.Mouse;
        bool inText = TextRect.Contains(m);

        if (MouseIntercept(input)) return;

        if (input.LeftPressed && inText && !VBar.Bounds.Contains(m) && !(HBar.Visible && HBar.Bounds.Contains(m)))
        {
            Root()?.SetFocus(this);
            OnActivate?.Invoke();
            Buf.BreakUndoCoalescing();

            var p = PosFromMouse(m);
            if (!ClickIntercept(p, input))
            {
                if (input.DoubleClicked) SelectWordAt(p);
                else { _caret = p; if (!input.Shift) _anchor = p; _selecting = true; }
            }
            ResetBlink();
            NotifyCaret();
        }
        else if (_selecting && input.LeftDown)
        {
            _caret = PosFromMouse(m);
            EnsureVisible();
            NotifyCaret();
        }
        if (input.LeftReleased) _selecting = false;

        // The wheel moves the caret one step per notch (there was no wheel in 3.1; a notch maps to a
        // caret step and EnsureVisible follows). Plain wheel = up/down a line with the goal column
        // locked; Shift+wheel = one column; Ctrl+wheel = one word.
        if (inText && input.WheelDelta != 0)
        {
            int n = Math.Abs(input.WheelDelta);
            bool up = input.WheelDelta > 0;
            for (int i = 0; i < n; i++)
            {
                if (input.Ctrl)
                    Move(up ? WordLeft(_caret) : WordRight(_caret), extend: false);
                else if (input.Shift)
                {
                    int col = Math.Clamp(_caret.Col + (up ? -1 : +1), 0, Buf.LineLength(_caret.Line));
                    Move(new Position(_caret.Line, col), extend: false);
                }
                else MoveV(up ? -1 : +1, extend: false);
            }
        }
    }

    // -- Keyboard ----------------------------------------------------------

    public override bool WantsKeyboard => true;

    public override void OnChar(char c)
    {
        if (char.IsControl(c)) return; // Tab/Enter/Backspace arrive as keys
        InsertText(c.ToString(), coalesce: true);
    }

    public override void OnKey(InputState input)
    {
        bool shift = input.Shift, ctrl = input.Ctrl;

        if (ctrl)
        {
            if (input.Pressed(Keys.A)) { SelectAll(); return; }
            if (input.Pressed(Keys.C)) { Copy(); return; }
            if (input.Pressed(Keys.X)) { Cut(); return; }
            if (input.Pressed(Keys.V)) { Paste(); return; }
            if (input.Pressed(Keys.Z)) { Undo(); return; }
            if (input.Pressed(Keys.Y)) { Redo(); return; }
        }

        if (input.Pressed(Keys.Left))  { Move(ctrl ? WordLeft(_caret) : HStepLeft(_caret), shift); return; }
        if (input.Pressed(Keys.Right)) { Move(ctrl ? WordRight(_caret) : HStepRight(_caret), shift); return; }
        if (input.Pressed(Keys.Up))    { MoveV(-1, shift); return; }
        if (input.Pressed(Keys.Down))  { MoveV(+1, shift); return; }
        // Home/End work on the visual row, so on a wrapped line they go to the ends of the row you
        // can see. Unwrapped a row is the whole line, so this is the classic behaviour.
        if (input.Pressed(Keys.Home)) { Move(ctrl ? new Position(0, 0) : RowStart(), shift); return; }
        if (input.Pressed(Keys.End))  { Move(ctrl ? Buf.End() : RowEnd(), shift); return; }
        if (input.Pressed(Keys.PageUp))   { MoveV(-VisLines, shift); return; }
        if (input.Pressed(Keys.PageDown)) { MoveV(+VisLines, shift); return; }

        if (input.Pressed(Keys.Enter)) { EnterKey(); return; }
        if (input.Pressed(Keys.Tab))
        {
            if (shift) OutdentLines();
            else if (HasSel) IndentLines();
            else InsertText(new string(' ', Tabs.Width), coalesce: false);
            return;
        }
        if (input.Pressed(Keys.Back))   { Backspace(); return; }
        if (input.Pressed(Keys.Delete)) { DeleteKey(); return; }
    }

    // -- Editing -----------------------------------------------------------

    protected void DeleteSelection()
    {
        var (a, b) = Sel();
        Buf.Delete(a, b);
        Collapse(a);
    }

    /// <summary>Replaces any selection with <paramref name="s"/> and leaves the caret after it.</summary>
    public void InsertText(string s) => InsertText(s, coalesce: false);

    protected void InsertText(string s, bool coalesce)
    {
        if (HasSel) DeleteSelection();
        if (!coalesce) Buf.BreakUndoCoalescing();
        var at = _caret;
        Buf.Insert(at, s);
        Collapse(TextBuffer.Advance(at, s));
        EnsureVisible();
    }

    // Tab / Shift+Tab over a selection indent and outdent whole lines.
    private (int first, int last) SelectedLineRange()
    {
        var (a, b) = Sel();
        int last = (b.Line > a.Line && b.Col == 0) ? b.Line - 1 : b.Line;
        return (a.Line, last);
    }

    protected void IndentLines()
    {
        var (first, last) = SelectedLineRange();
        Buf.BreakUndoCoalescing();
        string pad = new string(' ', Tabs.Width);
        for (int ln = first; ln <= last; ln++) Buf.Insert(new Position(ln, 0), pad);
        _anchor = new Position(first, 0);
        _caret = new Position(last, Buf.LineLength(last));
        ResetBlink(); NotifyCaret(); EnsureVisible();
    }

    protected void OutdentLines()
    {
        var (first, last) = SelectedLineRange();
        Buf.BreakUndoCoalescing();
        for (int ln = first; ln <= last; ln++)
        {
            string line = Buf.Line(ln);
            int remove = 0;
            while (remove < Tabs.Width && remove < line.Length && line[remove] == ' ') remove++;
            if (remove == 0 && line.Length > 0 && line[0] == '\t') remove = 1; // a leading hard tab
            if (remove > 0) Buf.Delete(new Position(ln, 0), new Position(ln, remove));
        }
        if (HasSel) { _anchor = new Position(first, 0); _caret = new Position(last, Buf.LineLength(last)); }
        else Collapse(new Position(first, Math.Min(_caret.Col, Buf.LineLength(first))));
        ResetBlink(); NotifyCaret(); EnsureVisible();
    }

    /// <summary>Enter: a newline that keeps the current line's indentation.</summary>
    protected virtual void EnterKey()
    {
        if (HasSel) DeleteSelection();
        string line = Buf.Line(_caret.Line);
        int ws = 0; while (ws < line.Length && (line[ws] == ' ' || line[ws] == '\t')) ws++;
        string indent = line.Substring(0, Math.Min(ws, _caret.Col));

        Buf.BreakUndoCoalescing();
        var at = _caret;
        Buf.Insert(at, "\n" + indent);
        Collapse(new Position(at.Line + 1, indent.Length));
        EnsureVisible();
    }

    protected virtual void Backspace()
    {
        if (HasSel) { DeleteSelection(); EnsureVisible(); return; }
        var prev = StepLeft(_caret);
        if (prev.CompareTo(_caret) != 0) { Buf.Delete(prev, _caret); Collapse(prev); EnsureVisible(); }
    }

    protected virtual void DeleteKey()
    {
        if (HasSel) { DeleteSelection(); EnsureVisible(); return; }
        var next = StepRight(_caret);
        if (next.CompareTo(_caret) != 0) { Buf.Delete(_caret, next); Collapse(_caret); EnsureVisible(); }
    }

    // Public ops, for keyboard shortcuts and an Edit menu alike.
    public void Copy() { if (HasSel) Clipboard.Text = SelectedText; }
    public void Cut() { if (HasSel) { Clipboard.Text = SelectedText; DeleteSelection(); EnsureVisible(); } }

    public void Paste()
    {
        string text = Clipboard.Text;
        if (string.IsNullOrEmpty(text)) return;
        int startCol = HasSel ? Sel().a.Col : _caret.Col; // where the paste lands once the selection is gone
        InsertText(Tabs.Expand(text, startCol), coalesce: false);
    }

    public void Undo() { var p = Buf.Undo(); if (p is { } pp) { Collapse(pp); EnsureVisible(); } }
    public void Redo() { var p = Buf.Redo(); if (p is { } pp) { Collapse(pp); EnsureVisible(); } }
    public void SelectAll() { _anchor = new Position(0, 0); _caret = Buf.End(); ResetBlink(); NotifyCaret(); }
    public bool CanUndo => Buf.CanUndo;
    public bool CanRedo => Buf.CanRedo;

    // -- Caret movement ----------------------------------------------------

    /// <summary>Focuses this widget and puts the caret at <paramref name="p"/>, scrolled into view.</summary>
    public void GoTo(Position p)
    {
        Root()?.SetFocus(this);
        _caret = _anchor = Buf.Clamp(p);
        ResetBlink(); EnsureVisible(); NotifyCaret();
    }

    /// <summary>Selects the range and scrolls the caret end into view.</summary>
    public void Select(Position a, Position b)
    {
        Buf.BreakUndoCoalescing();
        _anchor = Buf.Clamp(a);
        _caret = Buf.Clamp(b);
        ResetBlink(); EnsureVisible(); NotifyCaret();
    }

    /// <summary>Finds the next occurrence after the caret (wrapping) and selects it.</summary>
    public bool FindNext(string term, bool matchCase = false)
    {
        if (string.IsNullOrEmpty(term)) return false;
        string text = Buf.GetText();
        var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int from = OffsetOf(HasSel ? Sel().b : _caret);
        int idx = text.IndexOf(term, Math.Min(from, text.Length), cmp);
        if (idx < 0) idx = text.IndexOf(term, 0, cmp); // wrap
        if (idx < 0) return false;

        Select(PositionOf(text, idx), PositionOf(text, idx + term.Length));
        return true;
    }

    protected int OffsetOf(Position p)
    {
        int off = 0;
        for (int i = 0; i < p.Line && i < Buf.LineCount; i++) off += Buf.LineLength(i) + 1; // +1 for '\n'
        return off + p.Col;
    }

    protected static Position PositionOf(string text, int offset)
    {
        int line = 0, lastNl = -1;
        for (int i = 0; i < offset && i < text.Length; i++)
            if (text[i] == '\n') { line++; lastNl = i; }
        return new Position(line, offset - (lastNl + 1));
    }

    protected void Move(Position to, bool extend)
    {
        Buf.BreakUndoCoalescing();
        _caret = Buf.Clamp(to);
        if (!extend) _anchor = _caret;
        ResetBlink();
        EnsureVisible();
        NotifyCaret();
    }

    /// <summary>Puts the caret at <paramref name="p"/> and drops any selection.</summary>
    protected void Collapse(Position p) { _caret = _anchor = Buf.Clamp(p); ResetBlink(); NotifyCaret(); }

    protected Position StepLeft(Position p)
    {
        if (p.Col > 0) return new Position(p.Line, p.Col - 1);
        if (p.Line > 0) return new Position(p.Line - 1, Buf.LineLength(p.Line - 1));
        return p;
    }

    protected Position StepRight(Position p)
    {
        if (p.Col < Buf.LineLength(p.Line)) return new Position(p.Line, p.Col + 1);
        if (p.Line < Buf.LineCount - 1) return new Position(p.Line + 1, 0);
        return p;
    }

    // Arrow-key steps that skip whitespace: over non-whitespace they move one char, but a run of
    // whitespace (indentation, gaps between tokens) is crossed in a single press. Line wrapping is
    // preserved. StepLeft/StepRight stay char-exact for backspace/delete.
    private Position HStepLeft(Position p)
    {
        if (p.Col == 0) return StepLeft(p); // wrap to the previous line's end
        string line = Buf.Line(p.Line);
        int c = p.Col - 1;                  // the char just left of the caret
        if (!char.IsWhiteSpace(line[c])) return new Position(p.Line, c);
        while (c > 0 && char.IsWhiteSpace(line[c - 1])) c--;
        return new Position(p.Line, c);
    }

    private Position HStepRight(Position p)
    {
        string line = Buf.Line(p.Line);
        if (p.Col >= line.Length) return StepRight(p); // wrap to the next line's start
        int c = p.Col;                                 // the char just right of the caret
        if (!char.IsWhiteSpace(line[c])) return new Position(p.Line, c + 1);
        while (c < line.Length && char.IsWhiteSpace(line[c])) c++;
        return new Position(p.Line, c);
    }

    // Vertical move by VISUAL rows, preserving the goal column (the column within the row that the
    // caret "wants"), so passing through short rows and back returns to the same screen column
    // instead of shrinking each time. Unwrapped a row is a whole line, so this is line motion.
    protected void MoveV(int dRows, bool extend)
    {
        _verticalMove = true;
        int ri = Math.Clamp(RowIndexOf(_caret) + dRows, 0, _rows.Count - 1);
        var row = _rows[ri];
        int col = Math.Min(row.Start + _goalCol, row.End);
        Move(new Position(row.Line, col), extend);
        _verticalMove = false;
    }

    /// <summary>The first column of the caret's visual row.</summary>
    private Position RowStart()
    {
        var row = _rows[RowIndexOf(_caret)];
        return new Position(row.Line, row.Start);
    }

    /// <summary>The last column of the caret's visual row. On a wrapped row that stops before the
    /// space the break was made at, so End does not land the caret on the row below.</summary>
    private Position RowEnd()
    {
        int ri = RowIndexOf(_caret);
        var row = _rows[ri];
        int end = row.End;
        bool wrapped = end < Buf.LineLength(row.Line);
        if (wrapped)
        {
            string line = Buf.Line(row.Line);
            while (end > row.Start && (line[end - 1] == ' ' || line[end - 1] == '\t')) end--;
        }
        return new Position(row.Line, end);
    }

    protected Position WordLeft(Position p)
    {
        if (p.Col == 0) return StepLeft(p);
        string line = Buf.Line(p.Line);
        int i = p.Col - 1;
        int cls = Cls(line[i]);
        while (i > 0 && Cls(line[i - 1]) == cls) i--;
        return new Position(p.Line, i);
    }

    protected Position WordRight(Position p)
    {
        string line = Buf.Line(p.Line);
        if (p.Col >= line.Length) return StepRight(p);
        int i = p.Col;
        int cls = Cls(line[i]);
        while (i < line.Length && Cls(line[i]) == cls) i++;
        return new Position(p.Line, i);
    }

    protected void SelectWordAt(Position p)
    {
        string line = Buf.Line(p.Line);
        if (line.Length == 0) { _anchor = _caret = p; return; }
        int col = Math.Min(p.Col, line.Length - 1);
        int cls = Cls(line[col]);
        int a = col, b = col + 1;
        while (a > 0 && Cls(line[a - 1]) == cls) a--;
        while (b < line.Length && Cls(line[b]) == cls) b++;
        _anchor = new Position(p.Line, a);
        _caret = new Position(p.Line, b);
    }

    /// <summary>Word class for motion and double-click: identifier, whitespace, or punctuation.</summary>
    protected static int Cls(char c)
    {
        if (char.IsLetterOrDigit(c) || c == '_') return 0;
        if (char.IsWhiteSpace(c)) return 1;
        return 2;
    }

    /// <summary>The buffer position under a screen point.</summary>
    protected Position PosFromMouse(Point m)
    {
        int ri = Math.Clamp(ScrollLine + (m.Y - OriginY) / CellH, 0, _rows.Count - 1);
        if (m.Y < OriginY) ri = Math.Clamp(ScrollLine, 0, _rows.Count - 1);
        var row = _rows[ri];
        int rel = (int)Math.Round((double)(m.X - OriginX) / CellW);
        int col = Math.Clamp(row.Start + ScrollCol + Math.Max(0, rel), row.Start, row.End);
        return new Position(row.Line, col);
    }

    /// <summary>The screen point of a cell's top-left corner (off-screen values are legal).</summary>
    protected Point PointFor(Position p)
    {
        int ri = RowIndexOf(p);
        var row = _rows[ri];
        return new Point(OriginX + (p.Col - row.Start - ScrollCol) * CellW,
                         OriginY + (ri - ScrollLine) * CellH);
    }

    protected void EnsureVisible()
    {
        int ri = RowIndexOf(_caret);
        if (ri < ScrollLine) ScrollLine = ri;
        else if (ri >= ScrollLine + VisLines) ScrollLine = ri - VisLines + 1;
        ScrollLine = Math.Max(0, ScrollLine);

        if (!HBar.Visible) { ScrollCol = 0; return; } // wrapped text never scrolls sideways
        if (_caret.Col < ScrollCol) ScrollCol = _caret.Col;
        else if (_caret.Col >= ScrollCol + VisCols) ScrollCol = _caret.Col - VisCols + 1;
        ScrollCol = Math.Max(0, ScrollCol);
    }

    protected void ResetBlink() { _blink = 0; _blinkOn = true; }

    private void NotifyCaret()
    {
        // Any non-vertical caret move re-arms the goal column to the caret's column within its row.
        if (!_verticalMove) _goalCol = _caret.Col - _rows[RowIndexOf(_caret)].Start;
        OnCaretMoved();
        CaretMoved?.Invoke(_caret);
    }

    // -- Draw --------------------------------------------------------------

    public override void Draw(Win31Renderer r)
    {
        r.DrawPanel(Well, BevelStyle.SunkenThick, Theme.WindowBg);

        var (selA, selB) = Sel();
        bool hasSel = HasSel;
        var font = FontOverride ?? r.EditorFont;
        int caretRow = RowIndexOf(_caret);

        // ColorLine is asked once per LINE, not once per row: consecutive rows of a wrapped line
        // share one answer, and a colorizer can be expensive.
        Color[]? colors = null;
        int coloredLine = -1;

        for (int screenRow = 0; screenRow < VisLines; screenRow++)
        {
            int ri = ScrollLine + screenRow;
            if (ri >= _rows.Count) break;
            var row = _rows[ri];
            string line = Buf.Line(row.Line);
            int y = OriginY + screenRow * CellH;

            DrawLineBackground(r, row.Line, y);

            // Current-line band, under the selection and the text. On a wrapped line it covers
            // every row of that line, so "the line you are editing" still reads as one block.
            if (HighlightCurrentLine && row.Line == _caret.Line && !hasSel)
                r.Fill(TextRect.X, y, TextRect.Width, CellH, Theme.EditorCurrentLine);

            if (coloredLine != row.Line) { colors = ColorLine(row.Line, line); coloredLine = row.Line; }

            // The columns of this row that are actually on screen. Wrapped, ScrollCol is 0 and the
            // row is already screen-width; unwrapped, the row is the whole line and ScrollCol pans.
            int firstCol = row.Start + ScrollCol;
            int lastCol = Math.Min(row.End, firstCol + VisCols);

            // Selection band for this row.
            int selFrom = -1, selTo = -1;
            if (hasSel && row.Line >= selA.Line && row.Line <= selB.Line)
            {
                selFrom = row.Line == selA.Line ? selA.Col : 0;
                selTo = row.Line == selB.Line ? selB.Col : line.Length;
                int vf = Math.Max(selFrom, firstCol);
                int vt = Math.Min(selTo, lastCol);
                if (vt > vf)
                {
                    int x = OriginX + (vf - row.Start - ScrollCol) * CellW;
                    r.Fill(x, y, (vt - vf) * CellW, CellH, Theme.TitleActive);
                }
            }

            for (int ci = firstCol; ci < lastCol; ci++)
            {
                char ch = line[ci];
                if (ch == ' ') continue;
                int x = OriginX + (ci - row.Start - ScrollCol) * CellW;
                bool sel = ci >= selFrom && ci < selTo;
                Color c = sel ? Theme.TitleText : (colors != null ? colors[ci] : Theme.Text);
                font.Draw(r.Sb, ch.ToString(), x, y, c);
            }

            DrawLineOverlay(r, row.Line, line, y, firstCol, lastCol);

            // Caret - the traditional thick underline, 2px tall spanning the cell.
            if (Focused && _blinkOn && ri == caretRow)
            {
                int cx = OriginX + (_caret.Col - row.Start - ScrollCol) * CellW;
                if (_caret.Col >= firstCol && _caret.Col <= firstCol + VisCols)
                    r.Fill(cx, y + CellH - 2, CellW, 2, Theme.Text);
            }
        }

        VBar.Draw(r);
        if (HBar.Visible)
        {
            HBar.Draw(r);
            // The corner square between the two scrollbars (skin-drawn: Face fill by default, or art).
            ThemeManager.Skin.DrawScrollCorner(r,
                new Rectangle(VBar.Bounds.X, HBar.Bounds.Y, Theme.ScrollBarThickness, Theme.ScrollBarThickness));
        }

        DrawOverlays(r);
    }
}
