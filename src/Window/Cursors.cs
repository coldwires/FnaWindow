using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>
/// The mouse cursor, as authored art. An app registers named cursors from RGBA pixels + a hotspot
/// (<see cref="Define"/>), then the window sets one per frame by region (arrow, I-beam over text,
/// resize over a window edge - see WindowGame's cursor resolve). These are real OS color cursors
/// (SDL3), so they render natively with no lag and no software-sprite compromise. If an app defines
/// no cursors, the OS default is left untouched. Windows/SDL3.
/// </summary>
public static class Cursors
{
    private const string SDL = "SDL3";
    // SDL_PIXELFORMAT_ABGR8888: on little-endian this is byte order R,G,B,A - matching XNA Color bytes.
    private const uint PIXELFORMAT_RGBA = 0x16762004;

    [DllImport(SDL)] private static extern IntPtr SDL_CreateSurfaceFrom(int w, int h, uint format, IntPtr pixels, int pitch);
    [DllImport(SDL)] private static extern IntPtr SDL_CreateColorCursor(IntPtr surface, int hotX, int hotY);
    [DllImport(SDL)] private static extern void SDL_DestroySurface(IntPtr surface);
    [DllImport(SDL)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SDL_SetCursor(IntPtr cursor);

    private static readonly Dictionary<string, IntPtr> _cursors = new();
    private static string? _current;

    /// <summary>True once at least one cursor is registered (the window only manages the cursor then).</summary>
    public static bool Any => _cursors.Count > 0;

    /// <summary>Register a named cursor from straight-alpha RGBA pixels and its hotspot pixel.</summary>
    public static void Define(string key, Color[] pixels, int w, int h, int hotX, int hotY)
    {
        if (_cursors.ContainsKey(key)) return;

        var bytes = new byte[w * h * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            bytes[i * 4] = c.R; bytes[i * 4 + 1] = c.G; bytes[i * 4 + 2] = c.B; bytes[i * 4 + 3] = c.A;
        }

        IntPtr buf = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buf, bytes.Length);
            IntPtr surface = SDL_CreateSurfaceFrom(w, h, PIXELFORMAT_RGBA, buf, w * 4);
            if (surface == IntPtr.Zero) return;
            IntPtr cursor = SDL_CreateColorCursor(surface, hotX, hotY); // copies the pixels into the cursor
            SDL_DestroySurface(surface);
            if (cursor != IntPtr.Zero) _cursors[key] = cursor;
        }
        finally { Marshal.FreeHGlobal(buf); } // safe: the cursor owns its own copy now
    }

    /// <summary>Set the cursor to a registered key. No-op if unknown or already current.</summary>
    public static void Set(string key)
    {
        if (key == _current) return;
        if (_cursors.TryGetValue(key, out var h)) { _current = key; SDL_SetCursor(h); }
    }
}
