using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SDL3;

namespace FnaWindow;

/// <summary>
/// SDL's open-file and open-folder dialog, for <see cref="FolderPicker"/> and <see cref="FilePicker"/>
/// away from Windows. SDL answers on a thread of its choosing once the dialog closes: the chosen
/// paths, an empty list if cancelled, or null if no dialog could be shown.
/// </summary>
internal static class SdlDialog
{
    // A dialog's callback and its type list, held until SDL calls back with them.
    private static readonly Dictionary<IntPtr, (SDL.SDL_DialogFileCallback callback, IntPtr filters, List<IntPtr> strings, Action<string[]?> done)> Pending = new();
    private static long _next;

    /// <returns>False if the dialog could not be started (done is then never called).</returns>
    public static bool Show(string title, string? start, bool folders, bool multiple,
        (string Name, string Pattern)[]? types, Action<string[]?> done)
    {
        uint props = 0;
        var strings = new List<IntPtr>();
        IntPtr filters = IntPtr.Zero;
        IntPtr key = new IntPtr(System.Threading.Interlocked.Increment(ref _next));
        try
        {
            props = SDL.SDL_CreateProperties();
            if (props == 0) return false;
            SDL.SDL_SetStringProperty(props, "SDL.filedialog.title", title);
            SDL.SDL_SetBooleanProperty(props, "SDL.filedialog.many", multiple);
            if (start != null && System.IO.Directory.Exists(start))
                SDL.SDL_SetStringProperty(props, "SDL.filedialog.location", start.TrimEnd('/') + "/");
            int n = !folders && types != null ? types.Length : 0;
            if (n > 0)
            {
                // SDL_DialogFileFilter: a name and patterns as "md;txt", without the "*." of Windows.
                filters = Marshal.AllocHGlobal(n * 2 * IntPtr.Size);
                for (int i = 0; i < n; i++)
                {
                    string pattern = string.Join(";", Array.ConvertAll(types![i].Pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                        p => p is "*" or "*.*" ? "*" : p.StartsWith("*.") ? p[2..] : p));
                    IntPtr name = Marshal.StringToCoTaskMemUTF8(types[i].Name), pat = Marshal.StringToCoTaskMemUTF8(pattern);
                    strings.Add(name); strings.Add(pat);
                    Marshal.WriteIntPtr(filters, 2 * i * IntPtr.Size, name);
                    Marshal.WriteIntPtr(filters, (2 * i + 1) * IntPtr.Size, pat);
                }
                SDL.SDL_SetPointerProperty(props, "SDL.filedialog.filters", filters);
                SDL.SDL_SetNumberProperty(props, "SDL.filedialog.nfilters", n);
            }
            SDL.SDL_DialogFileCallback callback = Answer;
            lock (Pending) Pending[key] = (callback, filters, strings, done);
            SDL.SDL_ShowFileDialogWithProperties(folders ? SDL.SDL_FileDialogType.SDL_FILEDIALOG_OPENFOLDER : SDL.SDL_FileDialogType.SDL_FILEDIALOG_OPENFILE,
                callback, key, props);
            return true;
        }
        catch
        {
            lock (Pending) Pending.Remove(key);
            Free(filters, strings);
            return false;
        }
        finally { if (props != 0) SDL.SDL_DestroyProperties(props); }
    }

    private static void Answer(IntPtr userdata, IntPtr filelist, int filter)
    {
        (SDL.SDL_DialogFileCallback callback, IntPtr filters, List<IntPtr> strings, Action<string[]?> done) entry;
        lock (Pending)
        {
            if (!Pending.Remove(userdata, out entry)) return;
        }
        string[]? picked = null;
        if (filelist != IntPtr.Zero)
        {
            var paths = new List<string>();
            for (int i = 0; ; i++)
            {
                IntPtr p = Marshal.ReadIntPtr(filelist, i * IntPtr.Size);
                if (p == IntPtr.Zero) break;
                if (Marshal.PtrToStringUTF8(p) is { Length: > 0 } s) paths.Add(s);
            }
            picked = paths.Count > 0 ? paths.ToArray() : null;
        }
        Free(entry.filters, entry.strings);
        entry.done(picked);
    }

    private static void Free(IntPtr filters, List<IntPtr> strings)
    {
        foreach (var s in strings) Marshal.FreeCoTaskMem(s);
        if (filters != IntPtr.Zero) Marshal.FreeHGlobal(filters);
    }
}
