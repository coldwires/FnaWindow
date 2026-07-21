using System;
using System.Runtime.InteropServices;

namespace FnaWindow;

/// <summary>
/// Text clipboard shared by the whole process, backed by the OS clipboard via SDL3 - the same
/// native backend FNA already runs on (see WindowChrome) - with an in-process fallback.
///
/// Set writes to both, so text is shared with other applications; Get prefers live OS text and
/// falls back to the last in-process value when the OS clipboard is empty or unavailable (SDL not
/// up, headless capture mode, another platform). Every native call is best-effort, so an app never
/// loses cut/copy/paste inside its own window.
/// </summary>
public static class Clipboard
{
    private const string SDL = "SDL3";

    [DllImport(SDL)] private static extern IntPtr SDL_GetClipboardText();
    [DllImport(SDL)] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_HasClipboardText();
    [DllImport(SDL)] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetClipboardText([MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport(SDL)] private static extern void SDL_free(IntPtr mem);

    private static string _fallback = "";

    /// <summary>The clipboard text: OS text when there is any, else the last value set here.</summary>
    public static string Text
    {
        get => TryGet(out string t) && t.Length > 0 ? t : _fallback;
        set { _fallback = value ?? ""; TrySet(_fallback); }
    }

    /// <summary>Reads the OS clipboard. False when it is empty or unavailable.</summary>
    public static bool TryGet(out string text)
    {
        text = "";
        try
        {
            if (!SDL_HasClipboardText()) return false;
            IntPtr p = SDL_GetClipboardText(); // SDL hands back an owned copy; we must free it
            if (p == IntPtr.Zero) return false;
            try { text = Marshal.PtrToStringUTF8(p) ?? ""; }
            finally { SDL_free(p); }
            return true;
        }
        catch { return false; }
    }

    /// <summary>Writes the OS clipboard. False when it is unavailable.</summary>
    public static bool TrySet(string text)
    {
        try { return SDL_SetClipboardText(text ?? ""); }
        catch { return false; }
    }
}
