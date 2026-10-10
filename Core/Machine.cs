using System;

namespace Eepch.Core;

/// <summary>
/// 64 KB of memory plus the handful of OS page-0/2 variables and hardware registers that Atari BASIC
/// programs touch with PEEK/POKE.  This is NOT an emulator: there is no OS ROM and no CPU running
/// BASIC; the memory only exists so that screen RAM, display lists, charsets and colours behave
/// the way the program expects.
/// </summary>
public sealed class Machine
{
    // ---- OS variables (shadow registers etc.) ----
    public const int LMARGN = 82, RMARGN = 83, ROWCRS = 84, COLCRS = 85, DINDEX = 87, SAVMSC = 88, RAMTOP = 106;
    public const int SDLSTL = 560, SDMCTL = 559, COLOR0 = 708, COLOR4 = 712, CHBAS = 756, CRSINH = 752;
    public const int TXTROW = 656, TXTCOL = 657, TXTMSC = 660, VDSLST = 512, NMIEN = 0xD40E, CONSOL = 0xD01F;
    public const int COLPF0 = 0xD016, COLPF1 = 0xD017, COLPF2 = 0xD018, COLPF3 = 0xD019, COLBK = 0xD01A;

    public readonly byte[] Ram = new byte[65536];
    public byte[] RomFont = DefaultFont.Data;
    public int ConsoleKeys;            // bit0 START, bit1 SELECT, bit2 OPTION (1 = pressed)
    public Func<double> Clock = () => 0;
    public int JiffyOffset;
    public bool ColdStartRequested;
    public readonly Random Rng = new Random(1991);
    public int StickState = 15;        // joystick 0 (15 = centred)
    public int TrigState = 1;          // 1 = not pressed
    public Func<int> KeyCode = () => 255;

    public Machine() { Reset(); }

    public void Reset()
    {
        Array.Clear(Ram);
        Ram[RAMTOP] = 0xC0;
        Ram[LMARGN] = 2; Ram[RMARGN] = 39;
        Ram[SDMCTL] = 0x22;
        Ram[CHBAS] = 0xE0;
        Ram[SDLSTL] = 0; Ram[SDLSTL + 1] = 0;
        DefaultColors();
        Ram[0x2E4] = 0; // no-op placeholder
        ColdStartRequested = false;
    }

    public void DefaultColors()
    {
        Ram[COLOR0] = 0x28; Ram[COLOR0 + 1] = 0xCA; Ram[COLOR0 + 2] = 0x94; Ram[COLOR0 + 3] = 0x46; Ram[COLOR4] = 0x00;
    }

    public int Peek(int a)
    {
        a &= 0xFFFF;
        if (a >= 18 && a <= 20)
        {
            long j = ((long)(Clock() * 50.0) + JiffyOffset) & 0xFFFFFF;
            return a == 18 ? (int)(j >> 16) & 255 : a == 19 ? (int)(j >> 8) & 255 : (int)j & 255;
        }
        switch (a)
        {
            case CONSOL: return 7 & ~(ConsoleKeys & 7);
            case 0xD20A: return Rng.Next(256);
            case 0xD40B: return (int)(Clock() * 15600) % 131 * 2 & 255;
            case 0xD209: case 764: return KeyCode();
            case 632: case 0xD300: return StickState | 0xF0;
            case 644: case 0xD010: return TrigState;
            case 624: case 625: case 626: case 627: return 228;
            case 636: case 637: case 638: case 639: return 1;
        }
        return Ram[a];
    }

    public void Poke(int a, int v)
    {
        a &= 0xFFFF; v &= 255;
        if (a >= 18 && a <= 20)
        {
            int cur = Peek(18) << 16 | Peek(19) << 8 | Peek(20);
            int nw = a == 18 ? (cur & 0xFFFF) | v << 16 : a == 19 ? (cur & 0xFF00FF) | v << 8 : (cur & 0xFFFF00) | v;
            JiffyOffset += nw - cur;
            return;
        }
        Ram[a] = (byte)v;
    }

    public int DPeek(int a) => Peek(a) | Peek(a + 1) << 8;
    public void DPoke(int a, int v) { Poke(a, v & 255); Poke(a + 1, (v >> 8) & 255); }

    // ---- helpers used by the renderer/screen editor ----
    public int Dlist => Ram[SDLSTL] | Ram[SDLSTL + 1] << 8;
    public int Savmsc => Ram[SAVMSC] | Ram[SAVMSC + 1] << 8;
    public int Txtmsc => Ram[TXTMSC] | Ram[TXTMSC + 1] << 8;

    /// <summary>Address of the screen cell under the text cursor, or -1 if hidden.</summary>
    public int CursorAddress()
    {
        if (Ram[CRSINH] != 0) return -1;
        if (Ram[DINDEX] == 0)
        {
            int row = Ram[ROWCRS], col = Ram[COLCRS] | Ram[COLCRS + 1] << 8;
            return Savmsc + row * 40 + col;
        }
        return Txtmsc + Ram[TXTROW] * 40 + (Ram[TXTCOL] | Ram[TXTCOL + 1] << 8);
    }

    /// <summary>Returns the 8 pixel rows for glyph <paramref name="code"/> (0..127) from the charset at page <paramref name="page"/>.</summary>
    public byte GlyphRow(int page, int code, int row)
    {
        int a = page * 256 + code * 8 + row;
        if (page >= 0xE0 && page < 0xE4) return RomFont[(a - 0xE000) & 1023];
        return Ram[a & 0xFFFF];
    }
}
