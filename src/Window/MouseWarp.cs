using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;

namespace FnaWindow;

/// <summary>
/// Warps the OS cursor for continuous-grab gestures (the Blender-style middle-drag wrap): when
/// a drag reaches the client-area edge the cursor teleports to the opposite edge, so the
/// gesture never runs out of desk. The caller shifts its drag anchor by the same jump, keeping
/// the motion seamless. Windows-only; elsewhere it does nothing and the drag stops at the edge.
/// </summary>
internal static class MouseWarp
{
    /// <summary>If <paramref name="m"/> sits within a hair of <paramref name="client"/>'s edge,
    /// warp to the opposite edge (slightly inset, so it does not immediately re-trigger) and
    /// report the new client-space position.</summary>
    public static bool WrapInClient(Point m, Rectangle client, out Point warped)
    {
        warped = m;
        if (!OperatingSystem.IsWindows()) return false;
        const int Edge = 2, Inset = 4;
        int x = m.X, y = m.Y;
        if (m.X <= client.X + Edge) x = client.Right - 1 - Inset;
        else if (m.X >= client.Right - 1 - Edge) x = client.X + Inset;
        if (m.Y <= client.Y + Edge) y = client.Bottom - 1 - Inset;
        else if (m.Y >= client.Bottom - 1 - Edge) y = client.Y + Inset;
        if (x == m.X && y == m.Y) return false;

        // The gesture's window is the foreground window: a pan starts with a press in it.
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        var p = new POINT { X = x, Y = y };
        if (!ClientToScreen(hwnd, ref p) || !SetCursorPos(p.X, p.Y)) return false;
        warped = new Point(x, y);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT p);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
}
