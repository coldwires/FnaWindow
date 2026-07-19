using System;
using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>A full chrome palette. Add your own and pass it to <see cref="ThemeManager.Apply"/>.</summary>
public sealed record Palette(
    string Name,
    Color Face, Color LightEdge, Color DarkEdge, Color MidEdge,
    Color TitleActive, Color TitleInactive, Color TitleText,
    Color WindowBg, Color Text, Color TextDisabled, Color Desktop);

/// <summary>
/// Swaps the whole UI palette at runtime by writing into <see cref="Theme"/>'s mutable static
/// colors, then raising <see cref="Changed"/> so views can rebuild any cached colors. Ships a
/// few example palettes; define your own <see cref="Palette"/> and call <see cref="Apply"/>.
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
        WindowBg: C(0xFFFFFF), Text: C(0x000000), TextDisabled: C(0x808080), Desktop: C(0x008080));

    public static readonly Palette Midnight = new(
        "Midnight",
        Face: C(0x2E2E38), LightEdge: C(0x55555F), DarkEdge: C(0x101014), MidEdge: C(0x6A6A78),
        TitleActive: C(0x3A3A55), TitleInactive: C(0x2A2A30), TitleText: C(0xFFFFFF),
        WindowBg: C(0x14141C), Text: C(0xD8D8E0), TextDisabled: C(0x7A7A8A), Desktop: C(0x10102A));

    public static readonly Palette Slate = new(
        "Slate",
        Face: C(0xB8C0CC), LightEdge: C(0xFFFFFF), DarkEdge: C(0x54606E), MidEdge: C(0x8894A2),
        TitleActive: C(0x30506E), TitleInactive: C(0x8894A2), TitleText: C(0xFFFFFF),
        WindowBg: C(0xF4F6F8), Text: C(0x10161C), TextDisabled: C(0x76808C), Desktop: C(0x88A0B8));

    /// <summary>The built-in palettes (handy for building a Themes menu).</summary>
    public static readonly Palette[] All = { Win31, Midnight, Slate };

    public static Palette? ByName(string name)
    {
        foreach (var p in All) if (p.Name == name) return p;
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
        Current = p.Name;
        Changed?.Invoke();
    }
}
