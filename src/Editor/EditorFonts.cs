using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework.Graphics;

namespace FnaWindow;

/// <summary>
/// The monospace fonts a user can pick for a <see cref="TextArea"/>. Every option shares the fixed
/// editor cell (<see cref="Theme.EditorCellW"/> x <see cref="Theme.EditorCellH"/>), so swapping one
/// for another needs no relayout - the next repaint just draws different glyphs. Selecting an
/// option sets <see cref="TextArea.FontOverride"/>; a null font means "the engine default"
/// (fixedsys), which avoids loading the same atlas twice.
///
/// Two ways an option gets on the list:
/// <list type="bullet">
/// <item><see cref="AddNamed"/> - an app registering an atlas it ships, under a name it chooses.</item>
/// <item><see cref="Load"/> - any other atlas sitting in the app's <c>Content/fonts</c> that fits
/// the editor cell, named after its file. A font dropped in there shows up with no code change and
/// leaves no trace in a build where it is absent.</item>
/// </list>
/// </summary>
public static class EditorFonts
{
    public sealed record Option(string Name, BitmapFont? Font);

    private static readonly List<Option> _all = new();
    private static readonly HashSet<string> _claimed = new(StringComparer.OrdinalIgnoreCase)
    {
        // The engine's own atlases: the editor default plus the UI/chrome faces, none of which
        // belong in a "pick your editor font" list as scanned entries.
        "fixedsys_12", "sserife_11", "sserife_11_bold", "sserife_13_bold",
    };

    public static IReadOnlyList<Option> All => _all;
    public static string CurrentName { get; private set; } = "";

    /// <summary>
    /// Registers an atlas the app ships, under a display name, before <see cref="Load"/> scans.
    /// The file is <c>Content/fonts/&lt;fileBaseName&gt;.{png,json}</c>; a missing or wrong-sized
    /// atlas is skipped, so an app can offer a font it does not always ship.
    /// </summary>
    public static void AddNamed(GraphicsDevice gd, string displayName, string fileBaseName)
    {
        _claimed.Add(fileBaseName);
        if (_all.Exists(o => o.Name == displayName)) return;
        var font = TryLoad(gd, Path.Combine(FontDir, fileBaseName));
        if (font == null) return;
        if (!FitsCell(font)) { font.Dispose(); return; }
        _all.Add(new Option(displayName, font));
    }

    /// <summary>
    /// Builds the list: the engine default, anything already registered with
    /// <see cref="AddNamed"/>, then every other atlas in <c>Content/fonts</c> that fits the cell.
    /// Selects <paramref name="preferred"/> if it is there, else the first option. Call once after
    /// the engine's LoadContent, when the GraphicsDevice is ready.
    /// </summary>
    public static void Load(GraphicsDevice gd, string? preferred = null)
    {
        if (!_all.Exists(o => o.Font == null)) _all.Add(new Option("Fixedsys", null));
        AddLocalFonts(gd, FontDir);

        if (!string.IsNullOrEmpty(preferred) && _all.Exists(o => o.Name == preferred)) Select(preferred!);
        else if (CurrentName.Length == 0) Select(_all[0].Name);
    }

    /// <summary>Makes text areas draw with the named font; an unknown name is ignored.</summary>
    public static void Select(string name)
    {
        foreach (var o in _all)
            if (o.Name == name)
            {
                TextArea.FontOverride = o.Font;
                CurrentName = name;
                return;
            }
    }

    private static string FontDir => Path.Combine(AppContext.BaseDirectory, "Content", "fonts");

    private static bool FitsCell(BitmapFont f)
        => f.LineHeight == Theme.EditorCellH && f.CellWidth == Theme.EditorCellW;

    private static void AddLocalFonts(GraphicsDevice gd, string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (string path in Directory.GetFiles(dir, "*.json"))
        {
            string baseName = Path.GetFileNameWithoutExtension(path);
            if (_claimed.Contains(baseName)) continue;      // engine faces + app-registered ones
            var font = TryLoad(gd, Path.Combine(dir, baseName));
            if (font == null) continue;
            if (!FitsCell(font)) { font.Dispose(); continue; } // must share the cell or it will not line up
            string name = Pretty(baseName);
            if (_all.Exists(o => o.Name == name)) { font.Dispose(); continue; }
            _all.Add(new Option(name, font));
        }
    }

    // "my_mono" -> "My Mono"; "vga" -> "Vga".
    private static string Pretty(string baseName)
    {
        var parts = baseName.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
            parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
        return string.Join(' ', parts);
    }

    private static BitmapFont? TryLoad(GraphicsDevice gd, string basePathNoExt)
    {
        try { return BitmapFont.Load(gd, basePathNoExt); }
        catch { return null; } // a missing atlas just drops that choice
    }
}
