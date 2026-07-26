using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FnaWindow;

/// <summary>
/// The Win 3.1 list box: a sunken well of fixed-height rows, one selected at a time, with a real
/// <see cref="ScrollBar"/> down the right when the rows do not fit.
///
/// Rows are supplied as strings in <see cref="Items"/> for the common case. A list that needs
/// columns (a file browser wanting name, size and date) overrides <see cref="DrawRow"/> and keeps
/// its own row data alongside; <see cref="Count"/> then comes from the override of
/// <see cref="RowCount"/>. Everything else - scrolling, selection, keys, the wheel, double-click -
/// is the same either way, which is the reason this is one widget rather than a pattern each caller
/// re-implements.
/// </summary>
public class ListBox : Widget
{
    /// <summary>The rows, for the plain string case. Ignored if <see cref="RowCount"/> is overridden.</summary>
    public readonly List<string> Items = new();

    /// <summary>The selected row, or -1 for none.</summary>
    public int Selected { get; private set; } = -1;

    public int RowHeight = 16;

    /// <summary>Draw the sunken well behind the rows. Off when the owner has already drawn one.</summary>
    public bool DrawWell = true;

    /// <summary>
    /// Handle Up/Down/PageUp/PageDown/Home/End/Enter from <see cref="Update"/>. On when the list is
    /// the only thing in its window that wants those keys. Turn it OFF when something else shares
    /// the window and needs them (a command prompt, say) and drive selection through
    /// <see cref="MoveSelection"/> and <see cref="Select"/> instead.
    /// </summary>
    public bool HandleKeys = true;

    /// <summary>Raised when the selection changes, by any means.</summary>
    public Action<int>? SelectionChanged;

    /// <summary>Double-click, or Enter when <see cref="HandleKeys"/> is on.</summary>
    public Action<int>? Activated;

    protected readonly ScrollBar VBar = new();

    protected Rectangle Well, RowsRect;
    private int _scroll;

    public ListBox()
    {
        Add(VBar);
        VBar.OnChange = v => _scroll = v;
    }

    /// <summary>How many rows there are. Override alongside <see cref="DrawRow"/> for a list whose
    /// rows are not plain strings.</summary>
    protected virtual int RowCount => Items.Count;

    public int Count => RowCount;

    /// <summary>Rows that fit in the well. At least one, so the arithmetic never divides by zero on
    /// a list that has been laid out into no space at all.</summary>
    public int VisibleRows => Math.Max(1, RowsRect.Height / RowHeight);

    public int ScrollRow => _scroll;

    public override void Layout()
    {
        Well = Bounds;
        int sb = Theme.ScrollBarThickness;
        bool needBar = RowCount > Math.Max(1, (Bounds.Height - 4) / RowHeight);

        VBar.Visible = needBar;
        var inner = new Rectangle(Bounds.X + 2, Bounds.Y + 2, Bounds.Width - 4, Bounds.Height - 4);
        if (needBar)
        {
            VBar.Bounds = new Rectangle(Bounds.Right - sb - 2, inner.Y, sb, inner.Height);
            inner.Width -= sb;
        }
        RowsRect = inner;

        SyncScrollBar();
        base.Layout();
    }

    private void SyncScrollBar()
    {
        // Keep the selection in range: a subclass that shrinks its own rows and only calls this could
        // otherwise leave Selected past the end, and Enter would activate a stale, out-of-range index.
        if (Selected >= RowCount) SetSelected(RowCount > 0 ? RowCount - 1 : -1, notify: false);
        VBar.ContentSize = Math.Max(1, RowCount);
        VBar.ViewSize = VisibleRows;
        ClampScroll();
        VBar.Value = _scroll;
    }

    private void ClampScroll()
    {
        int max = Math.Max(0, RowCount - VisibleRows);
        _scroll = Math.Clamp(_scroll, 0, max);
    }

    /// <summary>Replaces the string rows and resets the view to the top with nothing selected.</summary>
    public void SetItems(IEnumerable<string> items)
    {
        Items.Clear();
        Items.AddRange(items);
        Reset();
    }

    /// <summary>Back to the top with no selection. Call after replacing the rows of a subclass that
    /// keeps its own data.</summary>
    public void Reset()
    {
        _scroll = 0;
        SetSelected(-1, notify: true);
        SyncScrollBar();
    }

    /// <summary>Selects a row (clamped; -1 clears) and scrolls it into view.</summary>
    public void Select(int index)
    {
        int n = RowCount;
        // A negative index clears, as documented. Clamping it to 0 instead would select the first
        // row and raise SelectionChanged for it, so "clear the selection" would silently mean
        // "activate row 0" for any caller repopulating a list.
        if (index < 0 || n == 0) { SetSelected(-1, notify: true); return; }
        SetSelected(Math.Clamp(index, 0, n - 1), notify: true);
        ScrollIntoView(Selected);
    }

    /// <summary>Moves the selection by <paramref name="delta"/> rows, starting from the top if
    /// nothing is selected yet.</summary>
    public void MoveSelection(int delta)
    {
        if (RowCount == 0) return;
        Select((Selected < 0 ? 0 : Selected) + delta);
    }

    public void ScrollIntoView(int index)
    {
        if (index < 0) return;
        if (index < _scroll) _scroll = index;
        else if (index >= _scroll + VisibleRows) _scroll = index - VisibleRows + 1;
        ClampScroll();
        VBar.Value = _scroll;
    }

    public void ScrollBy(int rows)
    {
        _scroll += rows;
        ClampScroll();
        VBar.Value = _scroll;
    }

    private void SetSelected(int value, bool notify)
    {
        if (Selected == value) return;
        Selected = value;
        if (notify) SelectionChanged?.Invoke(value);
    }

    /// <summary>The row index at a point, or -1 if the point is outside the rows or past the last one.</summary>
    public int RowAtPoint(Point p)
    {
        if (!RowsRect.Contains(p)) return -1;
        int row = _scroll + (p.Y - RowsRect.Y) / RowHeight;
        return row >= 0 && row < RowCount ? row : -1;
    }

    public override void Update(InputState input, GameTime t)
    {
        // A popup that owns input blocks the whole list, scrollbar included - nothing here should
        // act on the mouse while another widget holds it, and no idle animation is lost by returning.
        if (InputBlocked) return;
        if (!Enabled) return;

        SyncScrollBar();
        base.Update(input, t);

        var m = input.Mouse;

        if (input.WheelDelta != 0 && Bounds.Contains(m))
            ScrollBy(-input.WheelDelta * 3);

        if (input.LeftPressed)
        {
            int row = RowAtPoint(m);
            if (row >= 0)
            {
                SetSelected(row, notify: true);
                if (input.DoubleClicked) Activated?.Invoke(row);
            }
        }

        if (HandleKeys) UpdateKeys(input);
    }

    /// <summary>The key model, split out so a subclass can call it from its own conditions.</summary>
    protected void UpdateKeys(InputState input)
    {
        if (input.Pressed(Keys.Up)) MoveSelection(-1);
        if (input.Pressed(Keys.Down)) MoveSelection(+1);
        if (input.Pressed(Keys.PageUp)) MoveSelection(-VisibleRows);
        if (input.Pressed(Keys.PageDown)) MoveSelection(+VisibleRows);
        if (input.Pressed(Keys.Home)) Select(0);
        if (input.Pressed(Keys.End)) Select(RowCount - 1);
        if (input.Pressed(Keys.Enter) && Selected >= 0) Activated?.Invoke(Selected);
    }

    public override void Draw(Win31Renderer r)
    {
        if (DrawWell) r.DrawPanel(Well, BevelStyle.SunkenThick, Theme.WindowBg);

        int rows = VisibleRows;
        for (int i = 0; i < rows; i++)
        {
            int idx = _scroll + i;
            if (idx >= RowCount) break;

            var rect = new Rectangle(RowsRect.X, RowsRect.Y + i * RowHeight, RowsRect.Width, RowHeight);
            bool selected = idx == Selected;
            if (selected) r.Fill(rect.X, rect.Y, rect.Width, rect.Height, Theme.TitleActive);
            DrawRow(r, idx, rect, selected);
        }

        base.Draw(r); // the scrollbar, over the well
    }

    /// <summary>
    /// Draws one row into <paramref name="rect"/>. The selection band is already painted, so an
    /// override only draws content, and takes its text color from <paramref name="selected"/>.
    /// </summary>
    protected virtual void DrawRow(Win31Renderer r, int index, Rectangle rect, bool selected)
    {
        var font = r.UiFont;
        Color fg = selected ? Theme.TitleText : Theme.Text;
        r.DrawText(font, Items[index], rect.X + 4, rect.Y + (rect.Height - font.LineHeight) / 2, fg);
    }

    public override Widget? HitTest(Point p) => Bounds.Contains(p) ? this : null;
}
