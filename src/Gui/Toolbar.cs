using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>A toolbar button - icon (procedural glyph) or text - that depresses on click.</summary>
public sealed class ToolButton : Widget
{
    public string? Text;
    public Action<Win31Renderer, Rectangle>? Icon;
    public Action? OnClick;
    private bool _armed;

    public ToolButton(Action? onClick) { OnClick = onClick; }

    public override void Update(InputState input, GameTime t)
    {
        if (Root()?.Popup.IsOpen == true) { _armed = false; return; }
        if (!Enabled) { _armed = false; return; }

        if (input.LeftPressed && Bounds.Contains(input.Mouse)) _armed = true;
        if (input.LeftReleased)
        {
            if (_armed && Bounds.Contains(input.Mouse)) OnClick?.Invoke();
            _armed = false;
        }
    }

    public bool IsPressed => _armed && Bounds.Contains(GlobalMouse);
    // Toolbar sets this each frame so Draw knows the pointer without holding InputState.
    public Point GlobalMouse;

    public override void Draw(Win31Renderer r)
    {
        bool down = _armed && Bounds.Contains(GlobalMouse) && Enabled;
        r.DrawPanel(Bounds, down ? BevelStyle.SunkenThin : BevelStyle.RaisedThin, Theme.Face);

        var inner = Bounds;
        if (down) inner.Offset(1, 1);

        if (Icon != null)
        {
            Icon(r, inner);
        }
        else if (Text != null)
        {
            var color = Enabled ? Theme.Text : Theme.TextDisabled;
            int tw = r.UiFont.MeasureWidth(Text);
            int tx = inner.X + (inner.Width - tw) / 2;
            int ty = inner.Y + (inner.Height - r.UiFont.LineHeight) / 2;
            r.DrawText(r.UiFont, Text, tx, ty, color);
        }
    }
}

/// <summary>
/// The toolbar row: New / Open / Save icon buttons, a gap, then
/// text buttons for "dotnet build" and "▶ Run".
/// </summary>
public sealed class Toolbar : Widget
{
    private readonly List<ToolButton> _buttons = new();

    public ToolButton New = null!, Open = null!, Save = null!, Build = null!, Run = null!;

    public Toolbar()
    {
        New = Add(new ToolButton(null) { Icon = Icons.NewDoc });
        Open = Add(new ToolButton(null) { Icon = Icons.OpenFolder });
        Save = Add(new ToolButton(null) { Icon = Icons.Diskette });
        Build = Add(new ToolButton(null) { Text = "dotnet build" });
        Run = Add(new ToolButton(null) { Icon = Icons.RunLabel });
    }

    private ToolButton Add(ToolButton b) { _buttons.Add(b); base.Add(b); return b; }

    /// <summary>Skin-aware UI-text measure, set by the consumer, so text buttons size to the active
    /// font (a bigger-font skin needs wider buttons). Falls back to a rough estimate if unset.</summary>
    public Func<string, int>? MeasureText;

    public override void Layout()
    {
        int sz = Theme.ToolButtonSize;
        int y = Bounds.Y + (Bounds.Height - sz) / 2;
        int x = Bounds.X + 3;

        void Place(ToolButton b, int w) { b.Bounds = new Rectangle(x, y, w, sz); x += w + 1; }
        int TextW(string s) => MeasureText?.Invoke(s) ?? s.Length * 7;

        Place(New, sz); Place(Open, sz); Place(Save, sz);
        x += 6; // gap before text buttons

        // Text buttons sized to their label at the active font.
        Place(Build, TextW(Build.Text ?? "") + 16);
        Place(Run, 16 + TextW("Run") + 10); // play triangle + "Run" label
    }

    public override void Update(InputState input, GameTime t)
    {
        foreach (var b in _buttons) { b.GlobalMouse = input.Mouse; b.Update(input, t); }
    }

    public override void Draw(Win31Renderer r)
    {
        r.Fill(Bounds, Theme.Face);
        foreach (var b in _buttons) b.Draw(r);
    }
}

/// <summary>Tiny 1-bit toolbar glyphs, drawn procedurally to avoid a sprite sheet.</summary>
internal static class Icons
{
    public static void NewDoc(Win31Renderer r, Rectangle b)
    {
        int w = 12, h = 14;
        int x = b.X + (b.Width - w) / 2, y = b.Y + (b.Height - h) / 2;
        r.Fill(x, y, w, h, Theme.WindowBg);
        r.FrameRect(new Rectangle(x, y, w, h), Theme.Text);
        // dog-ear fold
        r.Fill(x + w - 4, y, 4, 4, Theme.Face);
        for (int i = 0; i < 4; i++) r.Fill(x + w - 4 + i, y + i, 1, 1, Theme.Text);
        r.VLine(x + w - 4, y, 4, Theme.Text);
        r.HLine(x + w - 4, y + 3, 4, Theme.Text);
    }

    public static void OpenFolder(Win31Renderer r, Rectangle b)
    {
        int w = 14, h = 10;
        int x = b.X + (b.Width - w) / 2, y = b.Y + (b.Height - h) / 2 + 1;
        // back tab
        r.Fill(x, y - 2, 6, 3, Theme.MidEdge);
        // body
        r.DrawPanel(new Rectangle(x, y, w, h), BevelStyle.RaisedThin, Theme.Face);
        r.FrameRect(new Rectangle(x, y, w, h), Theme.Text);
    }

    public static void RunLabel(Win31Renderer r, Rectangle b)
    {
        // Green play triangle + "Run" text (▶ isn't in the ASCII atlas).
        int th = 9;
        int tx = b.X + 8, ty = b.Y + (b.Height - th) / 2;
        for (int i = 0; i < 5; i++)
        {
            int col = i < 3 ? i : 4 - i; // 0,1,2,1,0 → triangle height at each x
            r.Fill(tx + i, ty + (th / 2) - col, 1, 2 * col + 1, Theme.SyntaxKeyword);
        }
        string s = "Run";
        int sx = tx + 8;
        int sy = b.Y + (b.Height - r.UiFont.LineHeight) / 2;
        r.DrawText(r.UiFont, s, sx, sy, Theme.Text);
    }

    public static void Diskette(Win31Renderer r, Rectangle b)
    {
        int w = 13, h = 13;
        int x = b.X + (b.Width - w) / 2, y = b.Y + (b.Height - h) / 2;
        r.Fill(x, y, w, h, Theme.TitleActive);
        r.FrameRect(new Rectangle(x, y, w, h), Theme.Text);
        // label
        r.Fill(x + 3, y + 6, w - 6, 5, Theme.WindowBg);
        // shutter
        r.Fill(x + w - 6, y + 1, 3, 4, Theme.Face);
    }
}
