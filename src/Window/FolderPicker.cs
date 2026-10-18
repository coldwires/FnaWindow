using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace FnaWindow;

/// <summary>
/// The Windows folder picker (IFileOpenDialog with FOS_PICKFOLDERS). It runs on its own STA
/// thread so the game keeps drawing behind it, owned by the game's window so it stays in front
/// and the window takes no input while it is open. The answer arrives on the game thread through
/// <see cref="MainThread"/>: the chosen folder, or null if the user cancelled.
/// </summary>
public static class FolderPicker
{
    private static int _open;

    /// <summary>True while a picker is showing; a second <see cref="Show"/> is ignored until it closes.</summary>
    public static bool IsOpen => Volatile.Read(ref _open) == 1;

    /// <param name="owner">The game window's HWND, or zero for none.</param>
    /// <param name="start">The folder it opens in, if it exists.</param>
    public static void Show(IntPtr owner, string title, string? start, Action<string?> done)
    {
        if (Interlocked.Exchange(ref _open, 1) == 1) return;
        var thread = new Thread(() =>
        {
            string? picked = null;
            try { picked = Pick(owner, title, start); }
            catch (Exception) { picked = null; }
            Volatile.Write(ref _open, 0);
            MainThread.Post(() => done(picked));
        }) { IsBackground = true, Name = "FolderPicker" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40, FOS_PATHMUSTEXIST = 0x800;
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    private static string? Pick(IntPtr owner, string title, string? start)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialogClass();
        try
        {
            dialog.GetOptions(out uint options);
            dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
            dialog.SetTitle(title);
            if (start != null && System.IO.Directory.Exists(start))
            {
                var iid = typeof(IShellItem).GUID;
                if (SHCreateItemFromParsingName(start, IntPtr.Zero, ref iid, out var folder) == 0)
                {
                    dialog.SetFolder(folder);
                    Marshal.ReleaseComObject(folder);
                }
            }
            if (dialog.Show(owner) != 0) return null;   // cancelled, or failed
            dialog.GetResult(out var item);
            try
            {
                item.GetDisplayName(SIGDN_FILESYSPATH, out IntPtr name);
                try { return Marshal.PtrToStringUni(name); }
                finally { Marshal.FreeCoTaskMem(name); }
            }
            finally { Marshal.ReleaseComObject(item); }
        }
        finally { Marshal.ReleaseComObject(dialog); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid, out IShellItem item);

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogClass { }

    // IFileOpenDialog, declared up to GetResult: IModalWindow's Show, then IFileDialog in vtable order.
    [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint count, IntPtr specs);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem item);
        void SetFolder(IShellItem item);
        void GetFolder(out IShellItem item);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint sigdn, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }
}
