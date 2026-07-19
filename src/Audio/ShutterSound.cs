using System;
using Microsoft.Xna.Framework.Audio;

namespace FnaWindow;

/// <summary>
/// A short procedural camera-shutter click, generated once and reused. Drives the audio half of
/// <see cref="WindowGame.CaptureScreenshot"/>. If no audio device is available the sound is
/// skipped and nothing throws, so it stays safe on headless machines and in CI.
/// </summary>
public sealed class ShutterSound
{
    private readonly SoundEffect? _effect;

    public ShutterSound()
    {
        try { _effect = Build(); }
        catch { _effect = null; } // no audio device: stay silent
    }

    public void Play()
    {
        try { _effect?.Play(0.5f, 0f, 0f); }
        catch { /* ignore playback failures */ }
    }

    // A "k-chk": two fast-decaying noise bursts (shutter open, then a louder close), high-passed
    // so they read as a crisp click rather than a low thud. Rendered to 16-bit mono PCM.
    private static SoundEffect Build()
    {
        const int rate = 44100;
        int n = rate * 150 / 1000;         // ~150 ms
        var s = new float[n];

        uint rng = 0x9E3779B9u;
        float Noise()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (int)rng / (float)int.MaxValue;
        }
        void Click(int start, int len, float amp)
        {
            for (int i = 0; i < len && start + i < n; i++)
            {
                float env = MathF.Exp(-6f * i / len);
                s[start + i] += Noise() * env * amp;
            }
        }
        Click(0, rate * 35 / 1000, 0.55f);              // open
        Click(rate * 60 / 1000, rate * 50 / 1000, 0.95f); // close

        float prevIn = 0f, prevOut = 0f; const float r = 0.9f;
        for (int i = 0; i < n; i++)
        {
            float x = s[i];
            float y = r * (prevOut + x - prevIn);
            prevOut = y; prevIn = x;
            s[i] = y;
        }

        var bytes = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            int v = (int)(Math.Clamp(s[i], -1f, 1f) * 32767);
            bytes[i * 2] = (byte)(v & 0xFF);
            bytes[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return new SoundEffect(bytes, rate, AudioChannels.Mono);
    }
}
