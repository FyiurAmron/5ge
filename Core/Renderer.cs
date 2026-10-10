using System;

namespace Eepch.Core;

/// <summary>
/// Draws the Atari screen from Machine memory: walks the display list at SDLSTL, renders ANTIC text/graphics
/// modes and runs display list interrupts (through the mini 6502) so mid-screen colour changes work.
/// Frame buffer = 384x240 (PAL overscan area; standard 320-pixel playfield centred).
/// </summary>
public sealed class Renderer
{
    public const int W = 384, H = 240, PfX = 32, PfW = 320;
    public readonly byte[] Rgba = new byte[W * H * 4];
    readonly uint[] pal = new uint[256];
    readonly Machine m;
    readonly Cpu6502 cpu;
    readonly int[] col = new int[5];     // COLPF0..3, COLBK
    readonly int[] row = new int[PfW];
    int cursorAddr;
    public bool ShowCursor = true;

    static readonly int[] LineH = { 0, 0, 8, 10, 8, 16, 8, 16, 8, 4, 4, 2, 1, 2, 1, 1 };
    static readonly int[] Bpl = { 0, 0, 40, 40, 40, 40, 20, 20, 10, 10, 20, 20, 20, 40, 40, 40 };

    public Renderer(Machine machine)
    {
        m = machine; cpu = new Cpu6502(machine);
        BuildPalette();
    }

    void BuildPalette()
    {
        for (int c = 0; c < 256; c++)
        {
            int hue = c >> 4, lum = c & 15;
            double y = 0.06 + 0.94 * lum / 15.0, r, g, b;
            if (hue == 0) { r = g = b = y; }
            else
            {
                double ang = ((hue - 1) * 24.0 + 213.0 - 24.0) * Math.PI / 180.0;
                double sat = 0.26;
                double i = sat * Math.Cos(ang), q = sat * Math.Sin(ang);
                r = y + 0.956 * i + 0.621 * q; g = y - 0.272 * i - 0.647 * q; b = y - 1.106 * i + 1.703 * q;
            }
            static int C8(double d) => (int)Math.Round(Math.Clamp(d, 0, 1) * 255);
            pal[c] = (uint)(C8(r) | C8(g) << 8 | C8(b) << 16 | 255 << 24);
        }
    }

    void Put(int y, int x, int colour)
    {
        int o = (y * W + x) * 4; uint p = pal[colour & 255];
        Rgba[o] = (byte)p; Rgba[o + 1] = (byte)(p >> 8); Rgba[o + 2] = (byte)(p >> 16); Rgba[o + 3] = 255;
    }

    void FillScan(int y)
    {
        if (y < 0 || y >= H) return;
        for (int x = 0; x < W; x++) Put(y, x, col[4]);
    }

    void BlitRow(int y)
    {
        if (y < 0 || y >= H) return;
        for (int x = 0; x < PfX; x++) Put(y, x, col[4]);
        for (int x = 0; x < PfW; x++) Put(y, PfX + x, row[x]);
        for (int x = PfX + PfW; x < W; x++) Put(y, x, col[4]);
    }

    void RunDli()
    {
        int vec = m.DPeek(Machine.VDSLST);
        if (vec == 0) return;
        for (int i = 0; i < 5; i++) m.Ram[Machine.COLPF0 + i] = (byte)col[i];
        cpu.Run(vec, 5000);
        for (int i = 0; i < 5; i++) col[i] = m.Ram[Machine.COLPF0 + i];
    }

    public void Render()
    {
        for (int i = 0; i < 4; i++) col[i] = m.Ram[Machine.COLOR0 + i];
        col[4] = m.Ram[Machine.COLOR4];
        cursorAddr = ShowCursor ? m.CursorAddress() : -1;
        int dma = m.Ram[Machine.SDMCTL];
        int y = 0;
        if ((dma & 0x20) != 0 && (dma & 3) != 0)
        {
            bool dliOn = (m.Ram[Machine.NMIEN] & 0x80) != 0;
            int dl = m.Dlist, mem = 0;
            for (int guard = 0; guard < 6000 && y < H; guard++)
            {
                int ins = m.Ram[dl & 0xFFFF]; dl = (dl + 1) & 0xFFFF;
                int mode = ins & 15;
                if (mode == 0)
                {
                    int n = ((ins >> 4) & 7) + 1;
                    for (int i = 0; i < n; i++) FillScan(y++);
                    if ((ins & 0x80) != 0 && dliOn) RunDli();
                    continue;
                }
                if (mode == 1)
                {
                    int a = m.Ram[dl & 0xFFFF] | m.Ram[(dl + 1) & 0xFFFF] << 8;
                    if ((ins & 0x40) != 0) break;
                    dl = a; continue;
                }
                if ((ins & 0x40) != 0) { mem = m.Ram[dl & 0xFFFF] | m.Ram[(dl + 1) & 0xFFFF] << 8; dl = (dl + 2) & 0xFFFF; }
                int h = LineH[mode];
                for (int r = 0; r < h; r++) { BuildRow(mode, mem, r); BlitRow(y++); }
                mem = (mem + Bpl[mode]) & 0xFFFF;
                if ((ins & 0x80) != 0 && dliOn) RunDli();
            }
        }
        while (y < H) FillScan(y++);
    }

    void BuildRow(int mode, int mem, int r)
    {
        int page = m.Ram[Machine.CHBAS];
        int bk = col[4];
        switch (mode)
        {
            case 2:
            case 3:
            {
                int fg = (col[2] & 0xF0) | (col[1] & 0x0F), bg = col[2];
                for (int c = 0; c < 40; c++)
                {
                    int a = (mem + c) & 0xFFFF, code = m.Ram[a];
                    int gr = r;
                    int g = 0;
                    if (mode == 3)
                    {
                        bool low = (code & 0x7F) >= 0x60;
                        gr = low ? r - 2 : r;
                        g = gr >= 0 && gr < 8 ? m.GlyphRow(page, code & 0x7F, gr) : 0;
                    }
                    else g = m.GlyphRow(page, code & 0x7F, r);
                    if ((code & 0x80) != 0) g ^= 0xFF;
                    if (a == cursorAddr) g ^= 0xFF;
                    for (int b = 0; b < 8; b++) row[c * 8 + b] = (g & (0x80 >> b)) != 0 ? fg : bg;
                }
                break;
            }
            case 4:
            case 5:
            {
                int gr = mode == 5 ? r >> 1 : r;
                for (int c = 0; c < 40; c++)
                {
                    int code = m.Ram[(mem + c) & 0xFFFF];
                    int g = m.GlyphRow(page, code & 0x7F, gr);
                    for (int p = 0; p < 4; p++)
                    {
                        int v = (g >> (6 - 2 * p)) & 3;
                        int cc = v == 0 ? bk : v == 1 ? col[0] : v == 2 ? col[1] : ((code & 0x80) != 0 ? col[3] : col[2]);
                        row[c * 8 + p * 2] = cc; row[c * 8 + p * 2 + 1] = cc;
                    }
                }
                break;
            }
            case 6:
            case 7:
            {
                int gr = mode == 7 ? r >> 1 : r;
                for (int c = 0; c < 20; c++)
                {
                    int code = m.Ram[(mem + c) & 0xFFFF];
                    int g = m.GlyphRow(page, code & 0x3F, gr);
                    int fg = col[code >> 6];
                    for (int b = 0; b < 8; b++)
                    {
                        int cc = (g & (0x80 >> b)) != 0 ? fg : bk;
                        row[c * 16 + b * 2] = cc; row[c * 16 + b * 2 + 1] = cc;
                    }
                }
                break;
            }
            default:
                BuildGraphicsRow(mode, mem);
                break;
        }
    }

    void BuildGraphicsRow(int mode, int mem)
    {
        int bk = col[4];
        int pxw; bool twoBit; int npx;
        switch (mode)
        {
            case 8: pxw = 8; twoBit = true; npx = 40; break;
            case 9: pxw = 4; twoBit = false; npx = 80; break;
            case 10: pxw = 4; twoBit = true; npx = 80; break;
            case 11: case 12: pxw = 2; twoBit = false; npx = 160; break;
            case 13: case 14: pxw = 2; twoBit = true; npx = 160; break;
            default: pxw = 1; twoBit = false; npx = 320; break;   // 15 (F) hires
        }
        int fgHi = (col[2] & 0xF0) | (col[1] & 0x0F);
        for (int p = 0; p < npx; p++)
        {
            int cc;
            if (twoBit)
            {
                int b = m.Ram[(mem + (p >> 2)) & 0xFFFF];
                int v = (b >> (6 - 2 * (p & 3))) & 3;
                cc = v == 0 ? bk : col[v - 1];
            }
            else
            {
                int b = m.Ram[(mem + (p >> 3)) & 0xFFFF];
                bool on = (b & (0x80 >> (p & 7))) != 0;
                cc = mode == 15 ? (on ? fgHi : col[2]) : (on ? col[0] : bk);
            }
            for (int k = 0; k < pxw; k++) row[p * pxw + k] = cc;
        }
    }
}
