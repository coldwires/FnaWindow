using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FnaWindow;

/// <summary>One row in a popup menu.</summary>
public sealed class MenuItemDef
{
    public string Label = "";
    public string? Shortcut;
    public Action? OnClick;
    public bool Enabled = true;
    public bool Checked;
    public bool IsSeparator;

    public static MenuItemDef Sep() => new() { IsSeparator = true };
    public static MenuItemDef Item(string label, string? shortcut = null, Action? onClick = null, bool enabled = true)
        => new() { Label = label, Shortcut = shortcut, OnClick = onClick, Enabled = enabled };
}

/// <summary>A top-level menu ("&amp;File") and its rows.</summary>
public sealed class TopMenu
{
    public string Title;
    public List<MenuItemDef> Items;
    public Rectangle TitleRect;
    /// <summary>Called just before the menu opens, so dynamic menus (Window) can rebuild Items.</summary>
    public Action? OnOpen;

    public TopMenu(string title, List<MenuItemDef> items) { Title = title; Items = items; }
}

/// <summary>
/// The menu bar row. Owns which top menu is open and drives its popup
/// directly (the PopupLayer is a passive container). Handles hover, click-to-open,
/// Alt+mnemonic, Left/Right to switch menus, and Esc to close.
/// </summary>
public sealed class MenuBar : Widget
{
    private const int TitlePadX = 8;

    public List<TopMenu> Menus { get; }
    public BitmapFont? BarFont; // top-bar label font (File/Edit); null -> the UI font
    private int _openIndex = -1;
    private Menu? _popup;
    private int _hovered = -1;

    public bool IsOpen => _openIndex >= 0;

    public MenuBar(List<TopMenu> menus) { Menus = menus; }

    public override void Layout()
    {
        int x = Bounds.X + 2;
        foreach (var m in Menus)
        {
            var (disp, _) = Win31Renderer.ParseMnemonic(m.Title);
            // Width uses UI font; measured lazily against a cached width is fine since
            // labels are short. We approximate here and refine in Draw via the renderer.
            int w = MeasureTitle(disp);
            m.TitleRect = new Rectangle(x, Bounds.Y, w, Bounds.Height);
            x += w;
        }
    }

    // Text-measuring hooks (UI font), set by Frame at construction.
    public Func<string, int>? MeasureTitleWidth;
    public Func<string, int>? MeasureItemWidth;
    private int MeasureTitle(string disp)
        => (BarFont?.MeasureWidth(disp) ?? MeasureTitleWidth?.Invoke(disp) ?? disp.Length * 6) + TitlePadX * 2;

    public override void Update(InputState input, GameTime t)
    {
        var root = Root();
        var popupLayer = root?.Popup;

        _hovered = -1;
        for (int i = 0; i < Menus.Count; i++)
            if (Menus[i].TitleRect.Contains(input.Mouse)) _hovered = i;

        if (IsOpen)
        {
            _popup?.Update(input, t);

            // Left/Right switch between top menus while open.
            if (input.Pressed(Keys.Left)) OpenMenu((_openIndex - 1 + Menus.Count) % Menus.Count);
            else if (input.Pressed(Keys.Right)) OpenMenu((_openIndex + 1) % Menus.Count);
            else if (input.Pressed(Keys.Escape)) CloseMenu();

            // Hover onto a different top title switches menus (classic behavior).
            if (_hovered >= 0 && _hovered != _openIndex) OpenMenu(_hovered);

            // Click on a top title toggles; click elsewhere outside the popup closes.
            if (input.LeftPressed)
            {
                if (_hovered == _openIndex) CloseMenu();
                else if (_hovered >= 0) OpenMenu(_hovered);
                else if (popupLayer != null && popupLayer.HitTest(input.Mouse) == null) CloseMenu();
            }
        }
        else
        {
            // Click a title to open.
            if (input.LeftPressed && _hovered >= 0 && (popupLayer == null || !popupLayer.IsOpen))
                OpenMenu(_hovered);

            // Alt+mnemonic opens a menu - but not over an open popup (a modal dialog owns the layer;
            // opening a menu here would overwrite Popup.Current and orphan the dialog), matching the
            // click-to-open guard above.
            if (input.Alt && (popupLayer == null || !popupLayer.IsOpen))
            {
                for (int i = 0; i < Menus.Count; i++)
                {
                    char key = Win31Renderer.MnemonicKey(Menus[i].Title);
                    if (key != '\0' && input.Pressed(LetterKey(key))) { OpenMenu(i); break; }
                }
            }

            if (DispatchShortcuts && !InputBlocked) DispatchShortcut(input);
        }
    }

    /// <summary>
    /// Act on the accelerators the items advertise in <see cref="MenuItemDef.Shortcut"/>. Off by
    /// default: the shortcut text has always been decorative, so an app may already bind the same
    /// keys itself - a focused <see cref="TextArea"/> handles Ctrl+X/C/V - and firing both would
    /// paste twice. Turn it on per app once its own bindings have been checked.
    /// </summary>
    public bool DispatchShortcuts;

    private void DispatchShortcut(InputState input)
    {
        foreach (var menu in Menus)
            foreach (var item in menu.Items)
            {
                if (item.IsSeparator || !item.Enabled || item.Shortcut == null || item.OnClick == null) continue;
                if (!Matches(item.Shortcut, input)) continue;
                item.OnClick.Invoke();
                return;
            }
    }

    /// <summary>Parses "Ctrl+Shift+S" style text and reports whether it is pressed right now.
    /// Modifiers not named must be UP, so Ctrl+S cannot fire a Ctrl+Shift+S item.</summary>
    private static bool Matches(string shortcut, InputState input)
    {
        bool ctrl = false, shift = false, alt = false;
        string keyName = "";
        foreach (var raw in shortcut.Split('+'))
        {
            string part = raw.Trim();
            if (part.Length == 0) continue;
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) ctrl = true;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) shift = true;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) alt = true;
            else keyName = part;
        }
        if (ctrl != input.Ctrl || shift != input.Shift || alt != input.Alt) return false;
        var key = ParseKey(keyName);
        return key != Keys.None && input.Pressed(key);
    }

    private static Keys ParseKey(string name)
    {
        if (name.Length == 1)
        {
            char c = char.ToLowerInvariant(name[0]);
            if (c is >= 'a' and <= 'z') return Keys.A + (c - 'a');
            if (c is >= '0' and <= '9') return Keys.D0 + (c - '0');
        }
        if (name.Length >= 2 && (name[0] == 'F' || name[0] == 'f')
            && int.TryParse(name.Substring(1), out int fn) && fn is >= 1 and <= 12)
            return Keys.F1 + (fn - 1);

        return name.ToLowerInvariant() switch
        {
            "del" or "delete" => Keys.Delete,
            "ins" or "insert" => Keys.Insert,
            "esc" or "escape" => Keys.Escape,
            "enter" or "return" => Keys.Enter,
            "tab" => Keys.Tab,
            "space" => Keys.Space,
            "home" => Keys.Home,
            "end" => Keys.End,
            "pgup" or "pageup" => Keys.PageUp,
            "pgdn" or "pagedown" => Keys.PageDown,
            _ => Keys.None,
        };
    }

    public void OpenMenu(int index)
    {
        var root = Root();
        if (root == null) return;
        _openIndex = index;
        var top = Menus[index];
        top.OnOpen?.Invoke(); // let dynamic menus rebuild their items
        _popup = new Menu(CloseMenu, top.Items)
        {
            Anchor = new Point(top.TitleRect.Left, Bounds.Bottom),
            ScreenWidth = root.Bounds.Width,
            ScreenHeight = root.Bounds.Height,
            MeasureWidth = MeasureItemWidth,
            Font = BarFont, // dropdown items match the menu-bar font
        };
        _popup.Layout();
        root.Popup.Open(_popup);
    }

    public void CloseMenu()
    {
        _openIndex = -1;
        _popup = null;
        Root()?.Popup.Close();
    }

    public override void Draw(Win31Renderer r)
    {
        r.Fill(Bounds, Theme.WindowBg); // 3.1 menu bar is white (95 is grey face)
        var font = BarFont ?? r.UiFont;
        for (int i = 0; i < Menus.Count; i++)
        {
            var m = Menus[i];
            bool active = i == _openIndex || (!IsOpen && i == _hovered);
            if (active) ThemeManager.Skin.DrawSelection(r, m.TitleRect, showArrow: false);
            var color = active ? Theme.TitleText : Theme.Text;
            int tx = m.TitleRect.X + TitlePadX;
            int ty = m.TitleRect.Y + (m.TitleRect.Height - font.LineHeight) / 2;
            r.DrawTextMnemonic(font, m.Title, tx, ty, color);
        }

        // 3.1 look: 1px rules top and bottom, framing the white bar against the caption above and
        // the content below. Drawn here rather than by each frame, so every window that has a menu
        // bar gets the same bar. The skin decides: a themed one turns them off.
        if (ThemeManager.Skin.DrawMenuSeparators)
        {
            r.HLine(Bounds.Left, Bounds.Top, Bounds.Width, Theme.Text);
            r.HLine(Bounds.Left, Bounds.Bottom - 1, Bounds.Width, Theme.Text);
        }
    }

    private static Keys LetterKey(char lower)
        => lower is >= 'a' and <= 'z' ? Keys.A + (lower - 'a') : Keys.None;
}

/// <summary>
/// A popup menu panel: RaisedThick on Face, 17px rows, right-aligned
/// shortcut text, 2px groove separators, navy/white highlight, disabled greying.
/// </summary>
public sealed class Menu : Widget
{
    // Row height follows the popup font when one is set (so rows fit larger text), else the skin metric.
    private int RowH => Font != null ? Font.LineHeight + 3 : Theme.MenuItemHeight;
    private const int SepH = 6;
    private const int LeftPad = 20;   // room for checkmark
    private const int RightPad = 12;
    private const int Gap = 24;       // between label and shortcut

    private readonly Action _onClose;
    private readonly List<MenuItemDef> _items;
    private readonly List<Rectangle> _rowRects = new();
    private int _selected = -1;

    public Point Anchor;
    public int ScreenWidth = 1024;
    public int ScreenHeight = 768;
    public BitmapFont? Font; // item font; null -> the UI font. When set it also drives measuring.

    // onClose lets any owner (the menu bar, or a Frame context menu) dismiss this popup.
    public Menu(Action onClose, List<MenuItemDef> items) { _onClose = onClose; _items = items; }

    public Func<string, int>? MeasureWidth; // set by Frame (UI font)

    private int W(string s) => Font != null ? Font.MeasureWidth(s) : MeasureWidth?.Invoke(s) ?? s.Length * 6;

    public override void Layout()
    {
        int maxLabel = 0, maxShortcut = 0;
        foreach (var it in _items)
        {
            if (it.IsSeparator) continue;
            var (disp, _) = Win31Renderer.ParseMnemonic(it.Label);
            maxLabel = Math.Max(maxLabel, W(disp));
            if (it.Shortcut != null) maxShortcut = Math.Max(maxShortcut, W(it.Shortcut));
        }
        int width = LeftPad + maxLabel + (maxShortcut > 0 ? Gap + maxShortcut : 0) + RightPad;

        int height = 4; // top bevel + pad
        _rowRects.Clear();
        int y = Anchor.Y + 2;
        int x = Anchor.X;
        foreach (var it in _items)
        {
            int h = it.IsSeparator ? SepH : RowH;
            _rowRects.Add(new Rectangle(x + 2, y, width - 4, h));
            y += h;
            height += h;
        }
        height += 2;

        // Clamp horizontally onto the screen.
        if (x + width > ScreenWidth) x = Math.Max(0, ScreenWidth - width);
        Bounds = new Rectangle(x, Anchor.Y, width, height);

        // Recompute row rects if x shifted.
        if (x != Anchor.X)
        {
            _rowRects.Clear();
            int yy = Anchor.Y + 2;
            foreach (var it in _items)
            {
                int h = it.IsSeparator ? SepH : RowH;
                _rowRects.Add(new Rectangle(x + 2, yy, width - 4, h));
                yy += h;
            }
        }
    }

    public override void Update(InputState input, GameTime t)
    {
        // Hover selection.
        _selected = -1;
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].IsSeparator || !_items[i].Enabled) continue;
            if (_rowRects[i].Contains(input.Mouse)) _selected = i;
        }

        if (input.Pressed(Keys.Down)) Move(+1);
        else if (input.Pressed(Keys.Up)) Move(-1);
        else if (input.Pressed(Keys.Enter)) { if (_selected >= 0) Activate(_selected); }

        if (input.LeftPressed && _selected >= 0 && _rowRects[_selected].Contains(input.Mouse))
            Activate(_selected);
    }

    private void Move(int dir)
    {
        int n = _items.Count;
        int start = _selected < 0 ? (dir > 0 ? -1 : 0) : _selected;
        for (int step = 1; step <= n; step++)
        {
            int i = ((start + dir * step) % n + n) % n;
            if (!_items[i].IsSeparator && _items[i].Enabled) { _selected = i; return; }
        }
    }

    private void Activate(int i)
    {
        var it = _items[i];
        if (it.IsSeparator || !it.Enabled) return;
        _onClose();             // close before invoking (action may open a dialog)
        it.OnClick?.Invoke();
    }

    public override void Draw(Win31Renderer r)
    {
        r.DrawPanel(Bounds, BevelStyle.RaisedThick, Theme.Face);
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            var row = _rowRects[i];
            if (it.IsSeparator)
            {
                // Full popup width (over the side border), the Win 3.1 look - not the inset row.
                ThemeManager.Skin.DrawMenuSeparator(r, new Rectangle(Bounds.X, row.Y, Bounds.Width, row.Height));
                continue;
            }

            bool sel = i == _selected;
            if (sel) ThemeManager.Skin.DrawSelection(r, row, showArrow: true);

            Color labelColor = !it.Enabled ? Theme.TextDisabled : sel ? Theme.TitleText : Theme.Text;
            Color shortcutColor = sel ? Theme.TitleText : Theme.TextDisabled;

            var font = Font ?? r.UiFont;
            int ty = row.Y + (row.Height - font.LineHeight) / 2;
            if (it.Checked) ThemeManager.Skin.DrawMenuCheck(r, row.X + 6, row.Y + row.Height / 2, labelColor);
            r.DrawTextMnemonic(font, it.Label, row.X + LeftPad, ty, labelColor);

            if (it.Shortcut != null)
            {
                int sw = font.MeasureWidth(it.Shortcut);
                r.DrawText(font, it.Shortcut, row.Right - RightPad - sw, ty, shortcutColor);
            }
        }
    }

    public override Widget? HitTest(Point p) => Bounds.Contains(p) ? this : null;
}
