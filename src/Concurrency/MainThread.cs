using System;
using System.Collections.Concurrent;

namespace FnaWindow;

/// <summary>
/// Marshals work from background threads onto the game (UI) thread. Worker threads call
/// <see cref="Post"/>; <see cref="WindowGame"/> calls <see cref="Drain"/> once per frame, so
/// queued actions run on the thread that owns the widgets and the GraphicsDevice. Nothing on
/// the UI thread blocks on a worker.
///
/// FNA is single-threaded for rendering: create textures, touch widgets, and draw only on the
/// game thread. So the pattern is: do the heavy work (process I/O, parsing, file scans, indexing)
/// on your own threads, then <see cref="Post"/> the result back here to apply it safely.
/// </summary>
public static class MainThread
{
    private static readonly ConcurrentQueue<Action> Queue = new();

    /// <summary>Queue an action to run on the game thread at the start of the next frame.
    /// Safe to call from any thread.</summary>
    public static void Post(Action action) => Queue.Enqueue(action);

    /// <summary>Run everything queued since the last call, on the calling (game) thread. Returns
    /// true if any action ran. A throwing handler is swallowed so it can't kill the loop.</summary>
    public static bool Drain()
    {
        bool any = false;
        while (Queue.TryDequeue(out var a))
        {
            any = true;
            try { a(); } catch { /* a bad handler must not kill the loop */ }
        }
        return any;
    }
}
