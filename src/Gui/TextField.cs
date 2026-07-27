using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FnaWindow;

/// <summary>
/// One line of editable text in a sunken Win 3.1 well: caret, selection, word motion, the OS
/// clipboard, and horizontal scrolling once the text outgrows the box.
///
/// <para>This is THE single-line field. Three dialogs had each grown their own copy of it
/// (<see cref="InputDialog"/>, <see cref="FormDialog"/>, <see cref="RetroFileDialog"/>) and they had
/// already drifted: none of them could select text, none could scroll, and one drew an underline
/// caret while the others drew a bar. They all use this now.</para>
///
/// <para>Deliberately not a <see cref="Widget"/>, the same as <see cref="PushButton"/> and
/// <see cref="CheckBox"/>: a dialog lays its own fields out and draws them in order, so this is a
/// small value the owner keeps and pumps from its own Update/Draw. A widget that wants one (an
/// editable cell, a formula bar) owns it the same way.</para>
///
/// <para>Enter and Escape are NOT handled here. What they mean belongs to the owner - OK and Cancel
/// in a dialog, commit-and-move-down in a grid - and a field that swallowed them could not be put
/// in either.</para>
/// </summary>
public sealed class TextField
{
    public Rectangle Bounds;

    /// <summary>Only a focused field takes keys, and only a focused field blinks a caret.</summary>
    public bool Focused = true;

    /// <summary>Stops the USER editing. The program still writes through <see cref="Text"/>.</summary>
    public bool ReadOnly;

    /// <summary>Draw the sunken well behind the text. Off when the owner already drew one.</summary>
    public bool DrawWell = true;

    public int MaxLength = 256;

    /// <summary>Face to draw and measure with; null uses the renderer's UI font.</summary>
    public BitmapFont? Font;

    /// <summary>Raised whenever the text changes, including a paste or a delete.</summary>
    public Action<string>? Changed;

    private const int PadX = 4;

    private string _text = "";
    private int _caret;      // 0.._text.Length
    private int _anchor;     // selection runs between _anchor and _caret
    private int _scrollPx;   // pixels of text scrolled off to the left
    private double _blinkMs;
    private bool _caretOn = true;
    private bool _dragging;
    private BitmapFont? _lastFont;   // the face the last Draw used, so Update can hit-test

    public TextField(string initial = "")
    {
        _text = initial ?? "";
        _caret = _anchor = _text.Length;
    }

    /// <summary>The content. Setting it moves the caret to the end and drops any selection.</summary>
    public string Text
    {
        get => _text;
        set
        {
            string v = value ?? "";
            if (v == _text) return;
            _text = v;
            _caret = _anchor = _text.Length;
            _scrollPx = 0;
            Blink();
            Changed?.Invoke(_text);
        }
    }

    public int Caret => _caret;
    public bool HasSelection => _caret != _anchor;
    public string SelectedText => HasSelection ? _text.Substring(SelLo, SelHi - SelLo) : "";

    private int SelLo => Math.Min(_caret, _anchor);
    private int SelHi => Math.Max(_caret, _anchor);

    /// <summary>Replace the content without raising <see cref="Changed"/> - the program talking, not the user.</summary>
    public void SetTextQuiet(string value, bool selectAll = false)
    {
        _text = value ?? "";
        _caret = _text.Length;
        _anchor = selectAll ? 0 : _caret;
        _scrollPx = 0;
        Blink();
    }

    public void SelectAll() { _anchor = 0; _caret = _text.Length; Blink(); }
    public void Deselect() { _anchor = _caret; Blink(); }
    public void MoveToEnd() { _caret = _anchor = _text.Length; Blink(); }

    /// <summary>An I-beam over the box, for a widget owner forwarding its CursorKey.</summary>
    public string? CursorKey(Point p) => Bounds.Contains(p) ? "ibeam" : null;

    /// <summary>Pump the field. Returns true on a frame where the text changed.</summary>
    public bool Update(InputState input, GameTime t)
    {
        _blinkMs += t.ElapsedGameTime.TotalMilliseconds;
        if (_blinkMs >= Theme.CaretBlinkMs) { _blinkMs = 0; _caretOn = !_caretOn; }

        bool changed = false;

        // -- Mouse: click to place the caret, drag to select, double-click to take the word.
        if (input.LeftPressed && Bounds.Contains(input.Mouse))
        {
            Focused = true;
            int at = IndexAt(input.Mouse.X);
            if (input.DoubleClicked) SelectWordAt(at);
            else { _caret = at; if (!input.Shift) _anchor = at; _dragging = true; }
            Blink();
        }
        else if (_dragging && input.LeftDown)
        {
            _caret = IndexAt(input.Mouse.X);
            Blink();
        }
        if (input.LeftReleased) _dragging = false;

        if (!Focused) return false;

        // -- Clipboard. Cut and paste are edits, so they are gated on ReadOnly; copy is not.
        if (input.Ctrl)
        {
            if (input.Pressed(Keys.A)) { SelectAll(); return false; }
            if (input.Pressed(Keys.C)) { if (HasSelection) Clipboard.TrySet(SelectedText); return false; }
            if (input.Pressed(Keys.X) && !ReadOnly)
            {
                if (HasSelection) { Clipboard.TrySet(SelectedText); DeleteSelection(); changed = true; }
                if (changed) Changed?.Invoke(_text);
                return changed;
            }
            if (input.Pressed(Keys.V) && !ReadOnly)
            {
                if (Clipboard.TryGet(out var paste) && paste.Length > 0) { Insert(FirstLine(paste)); changed = true; }
                if (changed) Changed?.Invoke(_text);
                return changed;
            }
        }

        // -- Typing. Ctrl-chords are shortcuts, not text, so they never reach the buffer.
        if (!ReadOnly && !input.Ctrl)
            foreach (char c in input.TypedChars)
                if (!char.IsControl(c)) { Insert(c.ToString()); changed = true; }

        // -- Editing keys.
        if (!ReadOnly && input.Pressed(Keys.Back))
        {
            if (HasSelection) { DeleteSelection(); changed = true; }
            else if (_caret > 0) { _text = _text.Remove(_caret - 1, 1); _caret = _anchor = _caret - 1; changed = true; }
            Blink();
        }
        else if (!ReadOnly && input.Pressed(Keys.Delete))
        {
            if (HasSelection) { DeleteSelection(); changed = true; }
            else if (_caret < _text.Length) { _text = _text.Remove(_caret, 1); changed = true; }
            Blink();
        }

        // -- Motion. Shift extends, Ctrl steps by word, and an unshifted arrow over a selection
        // collapses to its edge rather than moving a character, which is what an edit control does.
        if (input.Pressed(Keys.Left)) MoveTo(input.Ctrl ? WordLeft(_caret) : StepLeft(), input.Shift);
        else if (input.Pressed(Keys.Right)) MoveTo(input.Ctrl ? WordRight(_caret) : StepRight(), input.Shift);
        else if (input.Pressed(Keys.Home)) MoveTo(0, input.Shift);
        else if (input.Pressed(Keys.End)) MoveTo(_text.Length, input.Shift);

        if (changed) Changed?.Invoke(_text);
        return changed;
    }

    public void Draw(Win31Renderer r)
    {
        var font = Font ?? r.UiFont;
        _lastFont = font;
        if (DrawWell) r.DrawPanel(Bounds, BevelStyle.SunkenThick, Theme.WindowBg);

        var view = TextRect(font);
        EnsureCaretVisible(font, view.Width);

        int baseX = view.X - _scrollPx;
        int ty = view.Y;

        // The selection band first, clipped to the box, then the text over it in two colours.
        if (HasSelection)
        {
            int sx = baseX + font.MeasureWidth(_text.Substring(0, SelLo));
            int ex = baseX + font.MeasureWidth(_text.Substring(0, SelHi));
            int lo = Math.Max(sx, view.X), hi = Math.Min(ex, view.Right);
            if (hi > lo) r.Fill(lo, ty, hi - lo, font.LineHeight, Theme.TitleActive);
        }

        // Draw character by character so nothing spills outside the well: there is no scissor rect,
        // so a glyph that would cross an edge is simply not drawn.
        int x = baseX;
        for (int i = 0; i < _text.Length; i++)
        {
            int adv = font.MeasureWidth(_text[i].ToString());
            if (x + adv > view.X && x < view.Right)
            {
                bool sel = HasSelection && i >= SelLo && i < SelHi;
                if (x >= view.X && x + adv <= view.Right)
                    r.DrawText(font, _text[i].ToString(), x, ty, sel ? Theme.TitleText : Theme.Text);
            }
            x += adv;
            if (x >= view.Right) break;
        }

        if (Focused && _caretOn && !ReadOnly)
        {
            int cx = baseX + font.MeasureWidth(_text.Substring(0, _caret));
            if (cx >= view.X && cx <= view.Right) r.Fill(cx, ty, 1, font.LineHeight, Theme.Text);
        }
    }

    // -- Geometry ----------------------------------------------------------

    private Rectangle TextRect(BitmapFont font)
    {
        int inset = DrawWell ? PadX : 1;
        int h = font.LineHeight;
        return new Rectangle(Bounds.X + inset, Bounds.Y + (Bounds.Height - h) / 2,
            Math.Max(0, Bounds.Width - inset * 2), h);
    }

    /// <summary>The caret index nearest a screen x, so a click lands between characters.</summary>
    private int IndexAt(int mouseX)
    {
        // Update has no renderer, so hit-testing uses the face the last Draw measured with. Before
        // the first frame is drawn there is nothing to hit, and the caret simply stays put.
        var font = Font ?? _lastFont;
        if (font == null) return _caret;
        var view = TextRect(font);
        int x = view.X - _scrollPx;
        for (int i = 0; i < _text.Length; i++)
        {
            int adv = font.MeasureWidth(_text[i].ToString());
            if (mouseX < x + adv / 2) return i;
            x += adv;
        }
        return _text.Length;
    }

    private void EnsureCaretVisible(BitmapFont font, int viewW)
    {
        int caretX = font.MeasureWidth(_text.Substring(0, _caret));
        int total = font.MeasureWidth(_text);
        if (caretX - _scrollPx > viewW - 1) _scrollPx = caretX - viewW + 1;
        if (caretX - _scrollPx < 0) _scrollPx = caretX;
        // Never leave dead space on the right while text is scrolled off to the left.
        if (total - _scrollPx < viewW) _scrollPx = Math.Max(0, total - viewW + 1);
        if (total <= viewW) _scrollPx = 0;
    }

    // -- Edits -------------------------------------------------------------

    private void Insert(string s)
    {
        if (HasSelection) DeleteSelection();
        int room = MaxLength - _text.Length;
        if (room <= 0) return;
        if (s.Length > room) s = s.Substring(0, room);
        _text = _text.Insert(_caret, s);
        _caret = _anchor = _caret + s.Length;
        Blink();
    }

    private void DeleteSelection()
    {
        int lo = SelLo, hi = SelHi;
        _text = _text.Remove(lo, hi - lo);
        _caret = _anchor = lo;
        Blink();
    }

    private static string FirstLine(string s)
    {
        int nl = s.IndexOfAny(new[] { '\r', '\n' });
        return nl < 0 ? s : s.Substring(0, nl);
    }

    // -- Motion ------------------------------------------------------------

    private void MoveTo(int index, bool extend)
    {
        _caret = Math.Clamp(index, 0, _text.Length);
        if (!extend) _anchor = _caret;
        Blink();
    }

    private int StepLeft() => HasSelection ? SelLo : Math.Max(0, _caret - 1);
    private int StepRight() => HasSelection ? SelHi : Math.Min(_text.Length, _caret + 1);

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private int WordLeft(int i)
    {
        while (i > 0 && !IsWordChar(_text[i - 1])) i--;
        while (i > 0 && IsWordChar(_text[i - 1])) i--;
        return i;
    }

    private int WordRight(int i)
    {
        while (i < _text.Length && IsWordChar(_text[i])) i++;
        while (i < _text.Length && !IsWordChar(_text[i])) i++;
        return i;
    }

    private void SelectWordAt(int i)
    {
        if (_text.Length == 0) { _caret = _anchor = 0; return; }
        int at = Math.Clamp(i, 0, _text.Length - 1);
        if (!IsWordChar(_text[at])) { _anchor = at; _caret = at + 1; return; }
        int lo = at, hi = at;
        while (lo > 0 && IsWordChar(_text[lo - 1])) lo--;
        while (hi < _text.Length && IsWordChar(_text[hi])) hi++;
        _anchor = lo; _caret = hi;
    }

    private void Blink() { _blinkMs = 0; _caretOn = true; }
}
