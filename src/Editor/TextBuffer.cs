using System;
using System.Collections.Generic;
using System.Text;

namespace FnaWindow;

/// <summary>A 0-based position; Col counts UTF-16 code units (LSP convention).</summary>
public readonly record struct Position(int Line, int Col) : IComparable<Position>
{
    public int CompareTo(Position other)
        => Line != other.Line ? Line.CompareTo(other.Line) : Col.CompareTo(other.Col);

    public static bool operator <(Position a, Position b) => a.CompareTo(b) < 0;
    public static bool operator >(Position a, Position b) => a.CompareTo(b) > 0;
    public static bool operator <=(Position a, Position b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Position a, Position b) => a.CompareTo(b) >= 0;
}

/// <summary>A single edit as a range replacement, carrying the resulting version.</summary>
public readonly record struct TextChange(Position Start, Position End, string NewText, int NewVersion);

/// <summary>
/// Line-based text buffer. Lines never contain '\n' or trailing '\r'.
/// Every edit bumps <see cref="Version"/> and fires <see cref="Changed"/> with a
/// range-replacement TextChange (LSP-incremental shape). Undo/redo coalesces
/// consecutive single-char typing into word-sized groups.
/// </summary>
public sealed class TextBuffer
{
    private readonly List<string> _lines = new() { "" };

    public IReadOnlyList<string> Lines => _lines;
    public int Version { get; private set; }
    public event Action<TextChange>? Changed;

    public TextBuffer() { }
    public TextBuffer(string text) { SetText(text); }

    /// <summary>Replaces the whole buffer (used on file load). Resets undo history.</summary>
    public void SetText(string text)
    {
        _lines.Clear();
        foreach (var line in Normalize(text).Split('\n')) _lines.Add(line);
        if (_lines.Count == 0) _lines.Add("");
        _undo.Clear();
        _redo.Clear();
        Version++;
        Changed?.Invoke(new TextChange(new Position(0, 0), End(), GetText(), Version));
    }

    public int LineCount => _lines.Count;
    public string Line(int i) => _lines[i];
    public int LineLength(int i) => _lines[i].Length;

    /// <summary>The end position of the buffer (last line, last column).</summary>
    public Position End() => new(_lines.Count - 1, _lines[^1].Length);

    // -- Public edits ------------------------------------------------------
    public void Insert(Position pos, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        Apply(pos, pos, Normalize(text), coalescable: text.Length == 1 && text[0] != '\n');
    }

    public void Delete(Position start, Position end)
    {
        if (start.CompareTo(end) == 0) return;
        Apply(start, end, "", coalescable: false);
    }

    /// <summary>
    /// Replaces the range [start, end) with <paramref name="text"/> as ONE edit, and therefore as one
    /// undo step. Every edit in this class is a range replacement underneath; this just exposes it.
    ///
    /// Reach for it instead of Delete-then-Insert whenever the pair is logically a single change -
    /// Replace All over a document being the obvious case. Spelled as two edits, one Ctrl+Z leaves
    /// the document in a state the user never asked for. This is also not <see cref="SetText"/>,
    /// which throws the undo history away entirely.
    /// </summary>
    public void Replace(Position start, Position end, string text)
    {
        text = Normalize(text ?? "");
        if (start.CompareTo(end) == 0 && text.Length == 0) return;
        Apply(start, end, text, coalescable: false);
    }

    /// <summary>Signals a boundary (caret move, mouse click) so the next typed char starts a new undo group.</summary>
    public void BreakUndoCoalescing() => _coalesceAnchor = null;

    // -- Undo / redo -------------------------------------------------------
    private sealed class Op { public Position Start; public string OldText = ""; public string NewText = ""; }

    private readonly Stack<Op> _undo = new();
    private readonly Stack<Op> _redo = new();
    private Op? _coalesceAnchor; // the op currently accepting coalesced single chars

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Undoes the last edit group; returns the caret position after undo, or null if nothing to undo.</summary>
    public Position? Undo()
    {
        if (_undo.Count == 0) return null;
        var op = _undo.Pop();
        _coalesceAnchor = null;
        // The inserted text currently spans [Start, Advance(Start, NewText)); restore OldText.
        var insertedEnd = Advance(op.Start, op.NewText);
        RawReplace(op.Start, insertedEnd, op.OldText);
        _redo.Push(op);
        return Advance(op.Start, op.OldText);
    }

    /// <summary>Redoes the last undone edit; returns the caret position after redo, or null.</summary>
    public Position? Redo()
    {
        if (_redo.Count == 0) return null;
        var op = _redo.Pop();
        _coalesceAnchor = null;
        var oldEnd = Advance(op.Start, op.OldText);
        RawReplace(op.Start, oldEnd, op.NewText);
        _undo.Push(op);
        return Advance(op.Start, op.NewText);
    }

    // -- Core replace ------------------------------------------------------
    private void Apply(Position start, Position end, string newText, bool coalescable)
    {
        (start, end) = Order(Clamp(start), Clamp(end));
        string oldText = GetText(start, end);

        // Try to coalesce a single-char insert onto the previous typing group.
        if (coalescable && oldText.Length == 0 && _coalesceAnchor is { } anchor)
        {
            var anchorEnd = Advance(anchor.Start, anchor.NewText);
            bool contiguous = anchorEnd.CompareTo(start) == 0;
            bool sameClass = anchor.NewText.Length > 0 && SameWordClass(anchor.NewText[^1], newText[0]);
            if (contiguous && sameClass)
            {
                RawReplace(start, end, newText);
                anchor.NewText += newText;
                return;
            }
        }

        RawReplace(start, end, newText);

        var op = new Op { Start = start, OldText = oldText, NewText = newText };
        _undo.Push(op);
        _redo.Clear();
        _coalesceAnchor = coalescable ? op : null;
    }

    // Performs the edit + version bump + event, WITHOUT touching undo stacks.
    private void RawReplace(Position start, Position end, string newText)
    {
        (start, end) = Order(Clamp(start), Clamp(end));

        string prefix = _lines[start.Line].Substring(0, start.Col);
        string suffix = _lines[end.Line].Substring(end.Col);
        string[] insert = newText.Split('\n');

        var rebuilt = new List<string>(insert.Length);
        if (insert.Length == 1)
        {
            rebuilt.Add(prefix + insert[0] + suffix);
        }
        else
        {
            rebuilt.Add(prefix + insert[0]);
            for (int i = 1; i < insert.Length - 1; i++) rebuilt.Add(insert[i]);
            rebuilt.Add(insert[^1] + suffix);
        }

        _lines.RemoveRange(start.Line, end.Line - start.Line + 1);
        _lines.InsertRange(start.Line, rebuilt);

        Version++;
        Changed?.Invoke(new TextChange(start, end, newText, Version));
    }

    // -- Text access -------------------------------------------------------
    public string GetText() => string.Join("\n", _lines);

    public string GetText(Position start, Position end)
    {
        (start, end) = Order(Clamp(start), Clamp(end));
        if (start.Line == end.Line)
            return _lines[start.Line].Substring(start.Col, end.Col - start.Col);

        var sb = new StringBuilder();
        sb.Append(_lines[start.Line].Substring(start.Col));
        for (int i = start.Line + 1; i < end.Line; i++) { sb.Append('\n'); sb.Append(_lines[i]); }
        sb.Append('\n');
        sb.Append(_lines[end.Line].Substring(0, end.Col));
        return sb.ToString();
    }

    // -- Position helpers --------------------------------------------------
    public Position Clamp(Position p)
    {
        int line = Math.Clamp(p.Line, 0, _lines.Count - 1);
        int col = Math.Clamp(p.Col, 0, _lines[line].Length);
        return new Position(line, col);
    }

    /// <summary>The position reached by inserting <paramref name="text"/> starting at <paramref name="start"/>.</summary>
    public static Position Advance(Position start, string text)
    {
        int nl = 0, lastNl = -1;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') { nl++; lastNl = i; }
        if (nl == 0) return new Position(start.Line, start.Col + text.Length);
        return new Position(start.Line + nl, text.Length - lastNl - 1);
    }

    private static (Position, Position) Order(Position a, Position b)
        => a.CompareTo(b) <= 0 ? (a, b) : (b, a);

    private static string Normalize(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');

    // Word-class for coalescing: letters/digits/underscore group together; each run of
    // the same "other" char groups; whitespace (non-newline) groups. Newlines never coalesce.
    private static bool SameWordClass(char a, char b)
    {
        if (a == '\n' || b == '\n') return false;
        return WordClass(a) == WordClass(b);
    }

    private static int WordClass(char c)
    {
        if (char.IsLetterOrDigit(c) || c == '_') return 0;
        if (char.IsWhiteSpace(c)) return 1;
        return 2;
    }
}
