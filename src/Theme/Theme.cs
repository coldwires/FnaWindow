using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>
/// The single source of truth for every color and metric. The palette fields are mutable
/// <c>static</c> (not <c>static readonly</c>) so <see cref="FnaWindow.ThemeManager"/> can reskin
/// the whole UI at runtime; they default to the classic Windows 3.1 palette. Metrics stay
/// <c>const</c>. Nothing elsewhere hardcodes a color; it comes from here.
/// </summary>
public static class Theme
{
    private static Color Rgb(int r, int g, int b) => new(r, g, b);

    // ── Palette (mutable; see class summary) ──────────────────────────────
    public static Color Face          = Rgb(0xC0, 0xC0, 0xC0); // all chrome surfaces
    public static Color LightEdge     = Rgb(0xFF, 0xFF, 0xFF); // raised top/left
    public static Color DarkEdge      = Rgb(0x40, 0x40, 0x40); // raised bottom/right (outer)
    public static Color MidEdge       = Rgb(0x80, 0x80, 0x80); // inner shadow / separators
    public static Color TitleActive   = Rgb(0x00, 0x00, 0x80); // active title bars, selection
    public static Color TitleInactive = Rgb(0x80, 0x80, 0x80); // inactive title bars
    public static Color TitleText     = Rgb(0xFF, 0xFF, 0xFF); // text on title bars/selections
    public static Color WindowBg      = Rgb(0xFF, 0xFF, 0xFF); // editor well, listboxes
    public static Color Text          = Rgb(0x00, 0x00, 0x00); // default text
    public static Color TextDisabled  = Rgb(0x80, 0x80, 0x80); // disabled menu items, hints
    public static Color Desktop       = Rgb(0x00, 0x80, 0x80); // MDI area (classic teal)
    public static Color SquiggleError = Rgb(0xFF, 0x00, 0x00); // severity Error
    public static Color SquiggleWarn  = Rgb(0x00, 0x80, 0x80); // severity Warning

    // Syntax colors
    public static Color SyntaxKeyword  = Rgb(0x00, 0x80, 0x00); // C# keywords
    public static Color SyntaxTypeName = Rgb(0x00, 0x00, 0x80); // resolved type names
    public static Color SyntaxString   = Rgb(0x80, 0x00, 0x00); // string/char literals
    public static Color SyntaxComment  = Rgb(0x80, 0x80, 0x80); // comments

    // ── Metrics ───────────────────────────────────────────────────────────
    public const int TitleBarHeight      = 20;
    public const int MenuBarHeight       = 19;
    public const int MenuItemHeight      = 17;
    public const int ToolbarHeight       = 26;
    public const int ToolButtonSize      = 22;
    public const int StatusBarHeight     = 20;
    public const int StatusCellPadX      = 6;
    public const int StatusCellPadY      = 2;
    public const int ScrollBarThickness  = 16;
    public const int MdiChildTitleHeight = 18;
    public const int WindowBorder        = 4;
    public const int EditorPaddingLeft   = 8;
    public const int EditorPaddingTop    = 6;
    public const int CaretBlinkMs        = 530;

    public const int EditorCellW = 8;
    public const int EditorCellH = 15;
}

/// <summary>Bevel styles. These define the chrome's raised/sunken look.</summary>
public enum BevelStyle
{
    RaisedThin,
    RaisedThick,
    SunkenThin,
    SunkenThick,
}
