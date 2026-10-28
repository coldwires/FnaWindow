using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace FnaWindow;

/// <summary>
/// The Windows folder picker (IFileOpenDialog with FOS_PICKFOLDERS). It runs on its own STA
/// thread so the game keeps drawing behind it, owned by the game's window so it stays in front
/// and the window takes no input while it is open. The answer arrives on the game thread through
/// <see cref="MainThread"/>: the chosen folder, or null if the user cancelled.
/// <see cref="FilePicker"/> is the same dialog for files.
/// </summary>
public static class FolderPicker
{
    private static int _open;

    /// <summary>True while a picker (folder or file) is showing; a second one is ignored until it closes.</summary>
    public static bool IsOpen => Volatile.Read(ref _open) == 1;

    /// <param name="owner">The game window's HWND, or zero for none.</param>
    /// <param name="start">The folder it opens in, if it exists.</param>
    public static void Show(IntPtr owner, string title, string? start, Action<string?> done)
        => Run(owner, title, start, folders: true, multiple: false, null, picked => done(picked?.Length > 0 ? picked[0] : null));

    /// <summary>Shows the dialog on its own STA thread and posts what was picked (null if cancelled)
    /// to the game thread.</summary>
    internal static void Run(IntPtr owner, string title, string? start, bool folders, bool multiple,
        (string Name, string Pattern)[]? types, Action<string[]?> done)
    {
        if (Interlocked.Exchange(ref _open, 1) == 1) return;
        var thread = new Thread(() =>
        {
            string[]? picked = null;
            try { picked = Pick(owner, title, start, folders, multiple, types); }
            catch (Exception) { picked = null; }
            Volatile.Write(ref _open, 0);
            MainThread.Post(() => done(picked));
        }) { IsBackground = true, Name = folders ? "FolderPicker" : "FilePicker" };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40, FOS_ALLOWMULTISELECT = 0x200,
        FOS_PATHMUSTEXIST = 0x800, FOS_FILEMUSTEXIST = 0x1000;
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    private static string[]? Pick(IntPtr owner, string title, string? start, bool folders, bool multiple, (string Name, string Pattern)[]? types)
    {
        if (!OperatingSystem.IsWindows()) return null;   // the shell's dialog exists only there
        var dialog = (IFileOpenDialog)new FileOpenDialogClass();
        // The type list (COMDLG_FILTERSPEC: two string pointers each) must outlive the dialog.
        var strings = new List<IntPtr>();
        IntPtr specs = IntPtr.Zero;
        try
        {
            dialog.GetOptions(out uint options);
            options |= FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST;
            options |= folders ? FOS_PICKFOLDERS : FOS_FILEMUSTEXIST;
            if (multiple) options |= FOS_ALLOWMULTISELECT;
            dialog.SetOptions(options);
            dialog.SetTitle(title);
            if (!folders && types is { Length: > 0 })
            {
                specs = Marshal.AllocHGlobal(types.Length * 2 * IntPtr.Size);
                for (int i = 0; i < types.Length; i++)
                {
                    IntPtr name = Marshal.StringToCoTaskMemUni(types[i].Name), pattern = Marshal.StringToCoTaskMemUni(types[i].Pattern);
                    strings.Add(name); strings.Add(pattern);
                    Marshal.WriteIntPtr(specs, (2 * i) * IntPtr.Size, name);
                    Marshal.WriteIntPtr(specs, (2 * i + 1) * IntPtr.Size, pattern);
                }
                dialog.SetFileTypes((uint)types.Length, specs);
            }
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
            var paths = new List<string>();
            if (multiple)
            {
                dialog.GetResults(out var items);
                try
                {
                    items.GetCount(out uint count);
                    for (uint i = 0; i < count; i++)
                    {
                        items.GetItemAt(i, out var item);
                        try { if (PathOf(item) is { } p) paths.Add(p); }
                        finally { Marshal.ReleaseComObject(item); }
                    }
                }
                finally { Marshal.ReleaseComObject(items); }
            }
            else
            {
                dialog.GetResult(out var item);
                try { if (PathOf(item) is { } p) paths.Add(p); }
                finally { Marshal.ReleaseComObject(item); }
            }
            return paths.ToArray();
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
            foreach (var s in strings) Marshal.FreeCoTaskMem(s);
            if (specs != IntPtr.Zero) Marshal.FreeHGlobal(specs);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? PathOf(IShellItem item)
    {
        item.GetDisplayName(SIGDN_FILESYSPATH, out IntPtr name);
        try { return Marshal.PtrToStringUni(name); }
        finally { Marshal.FreeCoTaskMem(name); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid, out IShellItem item);

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogClass { }

    // IFileOpenDialog: IModalWindow's Show, then IFileDialog in vtable order, then its own two.
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
        void AddPlace(IShellItem item, int place);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr filter);
        void GetResults(out IShellItemArray items);
        void GetSelectedItems(out IShellItemArray items);
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

    [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IntPtr result);
        void GetPropertyStore(int flags, ref Guid riid, out IntPtr store);
        void GetPropertyDescriptionList(IntPtr key, ref Guid riid, out IntPtr list);
        void GetAttributes(int attribFlags, uint mask, out uint attributes);
        void GetCount(out uint count);
        void GetItemAt(uint index, out IShellItem item);
        void EnumItems(out IntPtr items);
    }
}

/// <summary>
/// The Windows file picker: the same dialog as <see cref="FolderPicker"/>, for existing files, one
/// or several (Ctrl or Shift click). The answer arrives on the game thread: the chosen files, or null
/// if the user cancelled.
/// </summary>
public static class FilePicker
{
    /// <param name="owner">The game window's HWND, or zero for none.</param>
    /// <param name="start">The folder it opens in, if it exists.</param>
    /// <param name="multiple">Let several files be chosen at once.</param>
    /// <param name="types">The dialog's type list, first one shown: a name and its patterns, as
    /// ("Notes (*.md)", "*.md") or ("Images", "*.png;*.jpg"). None shows every file.</param>
    public static void Show(IntPtr owner, string title, string? start, bool multiple, Action<string[]?> done,
        params (string Name, string Pattern)[] types)
        => FolderPicker.Run(owner, title, start, folders: false, multiple, types, done);
}
