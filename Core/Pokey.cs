using System;
using System.Collections.Generic;

namespace Eepch.Core;

/// <summary>
/// Four-voice POKEY-flavoured tone/noise synth driven by the BASIC SOUND statement.  Changes carry the
/// interpreter's simulated time so that fast envelope loops keep their spacing.  Pure C#, host feeds the samples to its audio API.
/// </summary>
public sealed class Pokey
{
    public const int Rate = 44100;

    struct Voice
    {
        public int Pitch, Dist, Vol;
        public double Phase;
        public bool Out;
        public uint L17, L5, L4;
    }

    readonly Voice[] v = new Voice[4];
    readonly Queue<(long at, int voice, int pitch, int dist, int vol)> events = new();
    long cursor;
    readonly float[] ring = new float[1 << 17];
    long readPos, writePos;

    public Pokey() { Reset(); }

    public void Reset()
    {
        for (int i = 0; i < 4; i++) v[i] = new Voice { L17 = 0x1FFFF, L5 = 0x1F, L4 = 0xF };
        events.Clear(); cursor = 0; readPos = writePos = 0;
    }

    /// <summary>Queues a SOUND change at simulated time <paramref name="t"/> (seconds).</summary>
    public void Sound(double t, int voice, int pitch, int dist, int vol)
    {
        if (voice < 0 || voice > 3) return;
        events.Enqueue(((long)(t * Rate), voice, pitch & 255, dist & 15, vol & 15));
    }

    public void AllOff(double t) { for (int i = 0; i < 4; i++) Sound(t, i, 0, 0, 0); }

    /// <summary>Generate samples up to simulated time <paramref name="t"/>.</summary>
    public void RenderTo(double t)
    {
        long target = (long)(t * Rate);
        if (target - cursor > Rate) cursor = target - Rate; // never more than 1 s behind
        while (cursor < target)
        {
            while (events.Count > 0 && events.Peek().at <= cursor)
            {
                var e = events.Dequeue();
                v[e.voice].Pitch = e.pitch; v[e.voice].Dist = e.dist; v[e.voice].Vol = e.vol;
            }
            float mix = 0;
            for (int i = 0; i < 4; i++) mix += Step(ref v[i]);
            if (writePos - readPos >= ring.Length) readPos++;      // overflow: drop oldest
            ring[writePos++ & (ring.Length - 1)] = mix * 0.16f;
            cursor++;
        }
    }

    static float Step(ref Voice s)
    {
        if (s.Vol == 0) return 0;
        double f = 31960.0 / (s.Pitch + 1);
        s.Phase += 2.0 * f / Rate;
        int guard = 0;
        while (s.Phase >= 1.0 && guard++ < 64)
        {
            s.Phase -= 1.0;
            switch (s.Dist & 14)
            {
                case 8: s.Out = Lfsr17(ref s.L17); break;
                case 0: s.Out = Lfsr17(ref s.L17) && !s.Out ? true : Lfsr5(ref s.L5); break;
                case 2: case 6: s.Out = Lfsr5(ref s.L5); break;
                case 4: { bool b = Lfsr5(ref s.L5); s.Out = b && !s.Out; break; }
                case 14: s.Out = Lfsr4(ref s.L4); break;
                default: s.Out = !s.Out; break;   // 10, 12 pure tone
            }
        }
        float a = s.Vol / 15f;
        return s.Out ? a : -a;
    }

    static bool Lfsr17(ref uint r) { uint b = ((r >> 0) ^ (r >> 5)) & 1; r = (r >> 1) | (b << 16); return (r & 1) != 0; }
    static bool Lfsr5(ref uint r) { uint b = ((r >> 0) ^ (r >> 2)) & 1; r = (r >> 1) | (b << 4); return (r & 1) != 0; }
    static bool Lfsr4(ref uint r) { uint b = ((r >> 0) ^ (r >> 1)) & 1; r = (r >> 1) | (b << 3); return (r & 1) != 0; }

    public int Available => (int)(writePos - readPos);

    public int Read(float[] dst, int count)
    {
        int n = Math.Min(count, Available);
        for (int i = 0; i < n; i++) dst[i] = ring[readPos++ & (ring.Length - 1)];
        return n;
    }
}
