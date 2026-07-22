using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace FnaWindow;

/// <summary>What a listed entry is. The order matters: it is the sort rank, so a parent link comes
/// first, then directories, then files, then drive roots.</summary>
public enum FsEntryKind
{
    Parent,
    Directory,
    File,
    Drive,
}

/// <summary>
/// One row of a directory listing. <see cref="Display"/> is already decorated the Win 3.1 way
/// (<c>[..]</c>, <c>[subdir]</c>, <c>[-c-]</c>); <see cref="Name"/> is the bare name for a caller
/// that wants to draw its own columns. Size and Modified are zero/default for anything that is not
/// a file, and also for a file whose metadata could not be read.
/// </summary>
public readonly record struct FsEntry(
    FsEntryKind Kind,
    string Name,
    string Display,
    string FullPath,
    long Size,
    DateTime Modified)
{
    public bool IsNavigable => Kind != FsEntryKind.File;
}

/// <summary>How to order a listing. Directories always sort before files whatever this says - a
/// file manager that mixes them is unusable.</summary>
public enum FsSort { Name, Size, Modified }

/// <summary>
/// Reads a directory into <see cref="FsEntry"/> rows. Every filesystem call here is wrapped,
/// because enumerating a directory fails for ordinary reasons (permissions, a disconnected network
/// path, a card reader with no card) and a file browser must show what it can rather than throw.
///
/// This is the plain, synchronous core. It does real I/O and can block for seconds on a network or
/// removable path, so UI code should not call it directly - use <see cref="DirectoryLoader"/>,
/// which runs it off-thread and marshals the result back.
/// </summary>
public static class DirectoryListing
{
    /// <summary>
    /// Lists <paramref name="dir"/>: a parent link, its subdirectories, the files matching
    /// <paramref name="pattern"/>, and optionally the drive roots. Never throws.
    /// </summary>
    /// <param name="pattern">Semicolon-separated globs, e.g. <c>*.cs;*.csproj</c>. <c>*.*</c> or
    /// <c>*</c> matches everything.</param>
    public static List<FsEntry> Read(string dir, string pattern = "*.*",
        bool includeHidden = false, bool includeDrives = true)
    {
        var list = new List<FsEntry>();

        try
        {
            var parent = Directory.GetParent(dir);
            if (parent != null)
                list.Add(new FsEntry(FsEntryKind.Parent, "..", "[..]", parent.FullName, 0, default));
        }
        catch { }

        foreach (string d in SafeDirs(dir))
        {
            if (!includeHidden && IsHidden(d)) continue;
            string name = Path.GetFileName(d);
            list.Add(new FsEntry(FsEntryKind.Directory, name, "[" + name + "]", d, 0, ModifiedOf(d)));
        }

        foreach (string f in SafeFiles(dir))
        {
            if (!includeHidden && IsHidden(f)) continue;
            string name = Path.GetFileName(f);
            if (!Matches(pattern, name)) continue;
            var (size, modified) = FileInfoOf(f);
            list.Add(new FsEntry(FsEntryKind.File, name, name, f, size, modified));
        }

        if (includeDrives)
            foreach (string drv in SafeDrives())
            {
                string letter = drv.TrimEnd('\\', '/').Replace(":", "");
                list.Add(new FsEntry(FsEntryKind.Drive, letter, "[-" + letter + "-]", drv, 0, default));
            }

        return list;
    }

    /// <summary>
    /// Re-orders a listing in place by <paramref name="key"/>. The kind rank is always the primary
    /// key, so the parent link stays first, directories stay above files, and drives stay last no
    /// matter which column the user sorted by; <paramref name="descending"/> flips only the
    /// comparison within a group.
    /// </summary>
    public static void Sort(List<FsEntry> list, FsSort key, bool descending = false)
    {
        list.Sort((a, b) =>
        {
            int rank = a.Kind.CompareTo(b.Kind);
            if (rank != 0) return rank;

            int cmp = key switch
            {
                FsSort.Size => a.Size.CompareTo(b.Size),
                FsSort.Modified => a.Modified.CompareTo(b.Modified),
                _ => 0,
            };
            // Name is the tie-break for every key, so equal sizes or equal timestamps still come out
            // in a stable, readable order rather than whatever the filesystem returned.
            if (cmp == 0) cmp = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return descending ? -cmp : cmp;
        });
    }

    /// <summary>True if <paramref name="name"/> matches any glob in the semicolon-separated
    /// <paramref name="pattern"/>. Only the <c>*.ext</c> and exact-name forms are supported, which is
    /// what a Win 3.1 filter box offers.</summary>
    public static bool Matches(string pattern, string name)
    {
        foreach (string patRaw in pattern.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string pat = patRaw.Trim();
            if (pat is "*.*" or "*") return true;
            if (pat.StartsWith("*.", StringComparison.Ordinal)
                && name.EndsWith(pat.Substring(1), StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(pat, name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>The directory to start in: the caller's choice if it exists, else the current one.</summary>
    public static string ResolveDir(string? initial)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
                return Path.GetFullPath(initial);
        }
        catch { }
        return Directory.GetCurrentDirectory();
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try { var a = Directory.GetDirectories(dir); Array.Sort(a, StringComparer.OrdinalIgnoreCase); return a; }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeFiles(string dir)
    {
        try { var a = Directory.GetFiles(dir); Array.Sort(a, StringComparer.OrdinalIgnoreCase); return a; }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeDrives()
    {
        var list = new List<string>();
        try
        {
            // Do NOT probe d.IsReady here: a not-ready optical/removable/disconnected-network drive
            // can block for seconds. Just list the roots; navigating to one validates it with
            // Directory.Exists at the point the user actually asks for it.
            foreach (var d in DriveInfo.GetDrives())
                try { list.Add(d.RootDirectory.FullName); } catch { }
        }
        catch { }
        return list;
    }

    private static bool IsHidden(string path)
    {
        try
        {
            var attr = File.GetAttributes(path);
            return (attr & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        }
        catch { return false; }
    }

    private static DateTime ModifiedOf(string path)
    {
        try { return Directory.GetLastWriteTime(path); } catch { return default; }
    }

    private static (long Size, DateTime Modified) FileInfoOf(string path)
    {
        try { var fi = new FileInfo(path); return (fi.Length, fi.LastWriteTime); }
        catch { return (0, default); }
    }
}

/// <summary>
/// Reads directories off the game thread and applies the result on it.
///
/// The generation counter is the point: a user holding Down through a directory tree starts a read
/// per folder, and those reads finish out of order. Each <see cref="Begin"/> stamps its read, and a
/// result whose stamp is stale is dropped instead of overwriting the folder the user is now looking
/// at. One loader instance per list.
/// </summary>
public sealed class DirectoryLoader
{
    private int _generation;

    /// <summary>True from <see cref="Begin"/> until that read applies, for a "reading..." hint.</summary>
    public bool Loading { get; private set; }

    /// <summary>
    /// Starts reading <paramref name="dir"/>. <paramref name="apply"/> runs on the game thread with
    /// the entries, and only if no later Begin has superseded this one.
    /// </summary>
    public void Begin(string dir, Action<List<FsEntry>> apply, string pattern = "*.*",
        bool includeHidden = false, bool includeDrives = true, FsSort sort = FsSort.Name,
        bool descending = false)
    {
        int gen = ++_generation;
        Loading = true;

        Task.Run(() =>
        {
            var list = DirectoryListing.Read(dir, pattern, includeHidden, includeDrives);
            if (sort != FsSort.Name || descending) DirectoryListing.Sort(list, sort, descending);
            MainThread.Post(() =>
            {
                if (gen != _generation) return; // a later navigation already won
                Loading = false;
                apply(list);
            });
        });
    }

    /// <summary>Abandons any in-flight read without starting a new one.</summary>
    public void Cancel()
    {
        _generation++;
        Loading = false;
    }
}
