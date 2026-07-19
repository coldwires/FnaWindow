using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>
/// A full palette: the chrome colors, plus optional syntax and squiggle colors for apps that
/// render code. The syntax/squiggle fields are nullable; leave them null on a palette that does
/// not render code and <see cref="ThemeManager.Apply"/> leaves those colors untouched. A palette
/// that does render code should set them so a theme switch recolors the code too.
/// </summary>
public sealed record Palette(
    string Name,
    Color Face, Color LightEdge, Color DarkEdge, Color MidEdge,
    Color TitleActive, Color TitleInactive, Color TitleText,
    Color WindowBg, Color Text, Color TextDisabled, Color Desktop,
    Color? SyntaxKeyword = null, Color? SyntaxTypeName = null,
    Color? SyntaxString = null, Color? SyntaxComment = null,
    Color? SquiggleError = null, Color? SquiggleWarn = null);

/// <summary>
/// Swaps the whole UI palette at runtime by writing into <see cref="Theme"/>'s mutable static
/// colors, then raising <see cref="Changed"/> so views can rebuild any cached colors. Because
/// every widget reads <see cref="Theme"/> at draw time, the whole UI reskins instantly.
///
/// Ships a few built-in palettes; define your own <see cref="Palette"/> and either call
/// <see cref="Apply"/> directly or <see cref="Register"/> it so it shows up in <see cref="All"/>
/// (handy for a Themes menu).
/// </summary>
public static class ThemeManager
{
    /// <summary>Raised after a palette is applied.</summary>
    public static event Action? Changed;
    public static string Current { get; private set; } = "Windows 3.1";

    private static Color C(int rgb) => new((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    public static readonly Palette Win31 = new(
        "Windows 3.1",
        Face: C(0xC0C0C0), LightEdge: C(0xFFFFFF), DarkEdge: C(0x404040), MidEdge: C(0x808080),
        TitleActive: C(0x000080), TitleInactive: C(0x808080), TitleText: C(0xFFFFFF),
        WindowBg: C(0xFFFFFF), Text: C(0x000000), TextDisabled: C(0x808080), Desktop: C(0x008080),
        SyntaxKeyword: C(0x008000), SyntaxTypeName: C(0x000080), SyntaxString: C(0x800000),
        SyntaxComment: C(0x808080), SquiggleError: C(0xFF0000), SquiggleWarn: C(0x008080));

    public static readonly Palette Midnight = new(
        "Midnight",
        Face: C(0x2E2E38), LightEdge: C(0x55555F), DarkEdge: C(0x101014), MidEdge: C(0x6A6A78),
        TitleActive: C(0x3A3A55), TitleInactive: C(0x2A2A30), TitleText: C(0xFFFFFF),
        WindowBg: C(0x14141C), Text: C(0xD8D8E0), TextDisabled: C(0x7A7A8A), Desktop: C(0x10102A),
        SyntaxKeyword: C(0x7AA6FF), SyntaxTypeName: C(0x6FE0FF), SyntaxString: C(0xE0A060),
        SyntaxComment: C(0x6A6A78), SquiggleError: C(0xE85050), SquiggleWarn: C(0xC8C830));

    public static readonly Palette Slate = new(
        "Slate",
        Face: C(0xB8C0CC), LightEdge: C(0xFFFFFF), DarkEdge: C(0x54606E), MidEdge: C(0x8894A2),
        TitleActive: C(0x30506E), TitleInactive: C(0x8894A2), TitleText: C(0xFFFFFF),
        WindowBg: C(0xF4F6F8), Text: C(0x10161C), TextDisabled: C(0x76808C), Desktop: C(0x88A0B8),
        SyntaxKeyword: C(0x0A5A2A), SyntaxTypeName: C(0x1A3A7A), SyntaxString: C(0x8A2A2A),
        SyntaxComment: C(0x6A7684), SquiggleError: C(0xC01818), SquiggleWarn: C(0x9A6A0A));

    // Built-ins first; apps can Register more.
    private static readonly List<Palette> _all = new() { Win31, Midnight, Slate };

    /// <summary>All palettes (built-in plus any registered). Handy for building a Themes menu.</summary>
    public static IReadOnlyList<Palette> All => _all;

    /// <summary>Add a palette to <see cref="All"/> (ignored if a palette with the same name exists).</summary>
    public static void Register(Palette p)
    {
        foreach (var x in _all) if (x.Name == p.Name) return;
        _all.Add(p);
    }

    public static Palette? ByName(string name)
    {
        foreach (var p in _all) if (p.Name == name) return p;
        return null;
    }

    public static void Apply(Palette p)
    {
        Theme.Face = p.Face;
        Theme.LightEdge = p.LightEdge;
        Theme.DarkEdge = p.DarkEdge;
        Theme.MidEdge = p.MidEdge;
        Theme.TitleActive = p.TitleActive;
        Theme.TitleInactive = p.TitleInactive;
        Theme.TitleText = p.TitleText;
        Theme.WindowBg = p.WindowBg;
        Theme.Text = p.Text;
        Theme.TextDisabled = p.TextDisabled;
        Theme.Desktop = p.Desktop;

        // Code colors: only overwrite when the palette provides them.
        if (p.SyntaxKeyword is { } k) Theme.SyntaxKeyword = k;
        if (p.SyntaxTypeName is { } t) Theme.SyntaxTypeName = t;
        if (p.SyntaxString is { } s) Theme.SyntaxString = s;
        if (p.SyntaxComment is { } c) Theme.SyntaxComment = c;
        if (p.SquiggleError is { } e) Theme.SquiggleError = e;
        if (p.SquiggleWarn is { } w) Theme.SquiggleWarn = w;

        Current = p.Name;
        Changed?.Invoke();
    }
}
