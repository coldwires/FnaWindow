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

    // -- Palette (mutable; see class summary) ------------------------------
    public static Color Face          = Rgb(0xC6, 0xC6, 0xC6); // all chrome surfaces
    public static Color LightEdge     = Rgb(0xFF, 0xFF, 0xFF); // raised top/left
    public static Color DarkEdge      = Rgb(0x40, 0x40, 0x40); // raised bottom/right (outer)
    public static Color MidEdge       = Rgb(0x84, 0x84, 0x84); // inner shadow / separators / thin bevel
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
    public static Color SyntaxNumber   = Rgb(0x00, 0x00, 0x00); // numeric literals
    public static Color SyntaxFunction = Rgb(0x00, 0x00, 0x00); // proc/function call names
    public static Color SyntaxMacro    = Rgb(0x00, 0x80, 0x00); // macro names, at use sites too
    public static Color SyntaxDirective = Rgb(0x00, 0x80, 0x00); // preprocessor directives
    public static Color SyntaxComment  = Rgb(0x80, 0x80, 0x80); // comments

    /// <summary>The faint band behind the caret's line in a text area. Derived from the palette (a
    /// touch of the selection color mixed into the text background) so it follows a theme swap
    /// instead of needing its own entry in every palette.</summary>
    public static Color EditorCurrentLine => Mix(WindowBg, TitleActive, 0.10f);

    /// <summary>The shade behind the row under the pointer in a list that opted into
    /// <see cref="ListBox.HoverHighlight"/>. Derived the same way, and a touch stronger than the
    /// caret band so the two do not read as the same thing in one window.</summary>
    public static Color HoverRow => Mix(WindowBg, TitleActive, 0.16f);

    /// <summary>The rule between cells in a grid of them (a spreadsheet, a table). Derived from the
    /// palette rather than declared by every palette: it is the text background pulled most of the
    /// way to the chrome shadow, so it stays a hairline on a light theme and does not glare on a
    /// dark one.</summary>
    public static Color GridLine => Mix(WindowBg, MidEdge, 0.55f);

    private static Color Mix(Color a, Color b, float t) => new(
        (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t),
        (int)(a.B + (b.B - a.B) * t));

    // -- Metrics -----------------------------------------------------------
    // Height/size metrics come from the active skin (a bigger-font skin needs taller rows); the
    // defaults are the classic Win 3.1 values held in the Skin base. The rest are consts.
    public static int TitleBarHeight      => ThemeManager.Skin.TitleBarHeight;
    public static int MenuBarHeight       => ThemeManager.Skin.MenuBarHeight;
    public static int MenuItemHeight      => ThemeManager.Skin.MenuItemHeight;
    public static int ToolbarHeight       => ThemeManager.Skin.ToolbarHeight;
    public static int ToolButtonSize      => ThemeManager.Skin.ToolButtonSize;
    public static int StatusBarHeight     => ThemeManager.Skin.StatusBarHeight;
    public static int ScrollBarThickness  => ThemeManager.Skin.ScrollBarThickness;
    public static int MdiChildTitleHeight => ThemeManager.Skin.MdiChildTitleHeight;

    public const int StatusCellPadX      = 6;
    public const int StatusCellPadY      = 2;
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
