using System;
using System.IO;
using System.Runtime.InteropServices;

namespace FnaWindow;

/// <summary>
/// Makes the engine's own <c>[DllImport("SDL3")]</c> calls reach the SDL that FNA loaded.
///
/// On Windows "SDL3" finds SDL3.dll beside the app, the same file FNA uses. On Linux .NET looks
/// for libSDL3.so, does not find the libSDL3.so.0 shipped beside the app, and falls through to the
/// system's /usr/lib/libSDL3.so: a second copy of SDL in the process that was never initialised. It
/// answers SDL_GetMouseFocus with null, so every click was taken for a click on another window and
/// dropped, and the clipboard, cursors and window icon went to it too. This maps "SDL3" to the
/// same file FNA.dll.config names.
/// </summary>
internal static class NativeSdl
{
    private static int _installed;

    public static void Install()
    {
        if (OperatingSystem.IsWindows() || System.Threading.Interlocked.Exchange(ref _installed, 1) == 1) return;
        string name = OperatingSystem.IsMacOS() ? "libSDL3.0.dylib" : "libSDL3.so.0";
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(NativeSdl).Assembly, (lib, asm, path) =>
            {
                if (lib != "SDL3") return IntPtr.Zero;   // anything else resolves as usual
                string beside = Path.Combine(AppContext.BaseDirectory, name);
                if (File.Exists(beside) && NativeLibrary.TryLoad(beside, out var h)) return h;
                return NativeLibrary.TryLoad(name, out var sys) ? sys : IntPtr.Zero;
            });
        }
        catch (InvalidOperationException) { /* a resolver is already set for this assembly */ }
    }
}
