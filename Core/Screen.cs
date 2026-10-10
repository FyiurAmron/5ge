using System;
using System.Collections.Generic;
using System.Text;

namespace Eepch.Core;

public sealed class ModeInfo
{
    public int Antic, Cols, RowsFull, RowsWin, BytesPerLine, Bpp;
    public bool Text;
}

/// <summary>GRAPHICS statement, display-list construction and pixel plotting (all inside Machine memory).</summary>
public sealed class Gfx
{
    public static readonly ModeInfo[] Modes = new ModeInfo[16];

    static Gfx()
    {
        ModeInfo M(int a, int c, int rf, int rw, int bpl, int bpp, bool t = false) => new ModeInfo { Antic = a, Cols = c, RowsFull = rf, RowsWin = rw, BytesPerLine = bpl, Bpp = bpp, Text = t };
        Modes[0] = M(2, 40, 24, 24, 40, 0, true);
        Modes[1] = M(6, 20, 24, 20, 20, 0, true);
        Modes[2] = M(7, 20, 12, 10, 20, 0, true);
        Modes[3] = M(8, 40, 24, 20, 10, 2);
        Modes[4] = M(9, 80, 48, 40, 10, 1);
        Modes[5] = M(0xA, 80, 48, 40, 20, 2);
        Modes[6] = M(0xB, 160, 96, 80, 20, 1);
        Modes[7] = M(0xD, 160, 96, 80, 40, 2);
        Modes[8] = M(0xF, 320, 192, 160, 40, 1);
        Modes[14] = M(0xC, 160, 192, 160, 20, 1);
        Modes[15] = M(0xE, 160, 192, 160, 40, 2);
    }

    readonly Machine m;
    public int DrawColor, FillColor, PosX, PosY;
    public int Mode;

    public Gfx(Machine machine) { m = machine; }

    public bool SetMode(int n)
    {
        int mode = n & 15;
        bool noWindow = (n & 16) != 0, noClear = (n & 32) != 0;
        var mi = Modes[mode];
        if (mi == null) return false;
        bool win = mode > 0 && !noWindow;
        int rows = win ? mi.RowsWin : mi.RowsFull;
        int gfxBytes = rows * mi.BytesPerLine;
        int total = gfxBytes + (win ? 160 : 0);
        int top = m.Ram[Machine.RAMTOP] * 256;
        int scr = top - total;

        var dl = new List<byte> { 0x70, 0x70, 0x70 };
        dl.Add((byte)(mi.Antic | 0x40)); dl.Add((byte)(scr & 255)); dl.Add((byte)(scr >> 8));
        for (int i = 1; i < rows; i++) dl.Add((byte)mi.Antic);
        if (win)
        {
            int tw = scr + gfxBytes;
            dl.Add(0x42); dl.Add((byte)(tw & 255)); dl.Add((byte)(tw >> 8));
            dl.Add(2); dl.Add(2); dl.Add(2);
        }
        int dla = scr - (dl.Count + 3);
        dl.Add(0x41); dl.Add((byte)(dla & 255)); dl.Add((byte)(dla >> 8));
        for (int i = 0; i < dl.Count; i++) m.Ram[dla + i] = dl[i];

        if (!noClear) Array.Clear(m.Ram, scr, total);
        m.Ram[Machine.SDLSTL] = (byte)(dla & 255); m.Ram[Machine.SDLSTL + 1] = (byte)(dla >> 8);
        m.Ram[Machine.SAVMSC] = (byte)(scr & 255); m.Ram[Machine.SAVMSC + 1] = (byte)(scr >> 8);
        int tx = win ? scr + gfxBytes : 0;
        m.Ram[Machine.TXTMSC] = (byte)(tx & 255); m.Ram[Machine.TXTMSC + 1] = (byte)(tx >> 8);
        m.Ram[Machine.DINDEX] = (byte)mode;
        m.Ram[Machine.ROWCRS] = 0; m.Ram[Machine.COLCRS] = 0; m.Ram[Machine.COLCRS + 1] = 0;
        m.Ram[Machine.TXTROW] = 0; m.Ram[Machine.TXTCOL] = 0; m.Ram[Machine.TXTCOL + 1] = 0;
        m.Ram[Machine.SDMCTL] = 0x22;
        m.DefaultColors();
        Mode = mode;
        return true;
    }

    int Width => Modes[Mode].Cols;
    int Height => (m.Ram[Machine.TXTMSC] | m.Ram[Machine.TXTMSC + 1] << 8) != 0 ? Modes[Mode].RowsWin : Modes[Mode].RowsFull;

    public bool InRange(int x, int y) => Modes[Mode] != null && !Modes[Mode].Text && x >= 0 && y >= 0 && x < Width && y < Height;
    public bool Graphical => Modes[Mode] != null && !Modes[Mode].Text;

    public void Plot(int x, int y, int c)
    {
        if (!InRange(x, y)) return;
        var mi = Modes[Mode];
        int a = m.Savmsc + y * mi.BytesPerLine;
        if (mi.Bpp == 1)
        {
            a += x >> 3; int sh = 7 - (x & 7);
            m.Ram[a] = (byte)((m.Ram[a] & ~(1 << sh)) | ((c & 1) << sh));
        }
        else
        {
            a += x >> 2; int sh = 6 - 2 * (x & 3);
            m.Ram[a] = (byte)((m.Ram[a] & ~(3 << sh)) | ((c & 3) << sh));
        }
    }

    public int Point(int x, int y)
    {
        if (!InRange(x, y)) return 0;
        var mi = Modes[Mode];
        int a = m.Savmsc + y * mi.BytesPerLine;
        if (mi.Bpp == 1) return (m.Ram[a + (x >> 3)] >> (7 - (x & 7))) & 1;
        return (m.Ram[a + (x >> 2)] >> (6 - 2 * (x & 3))) & 3;
    }

    public void Line(int x0, int y0, int x1, int y1, int c)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        for (int guard = 0; guard < 100000; guard++)
        {
            Plot(x0, y0, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    public void DrawText(int x, int y, byte[] s)
    {
        int page = m.Ram[Machine.CHBAS];
        foreach (byte ch in s)
        {
            int sc = ScreenEditor.ToScreenCode(ch) & 0x7F;
            for (int r = 0; r < 8; r++)
            {
                int g = m.GlyphRow(page, sc, r);
                for (int b = 0; b < 8; b++) if ((g & (0x80 >> b)) != 0) Plot(x + b, y + r, DrawColor);
            }
            x += 8;
        }
    }
}

/// <summary>The "E:" screen editor: prints ATASCII into screen memory of the current text area and tracks the cursor in OS variables.</summary>
public sealed class ScreenEditor
{
    readonly Machine m;
    bool esc;
    public Action<byte> OnPrinted;     // raw byte mirror for the host console
    public Action OnBell;

    public ScreenEditor(Machine machine) { m = machine; }

    public static int ToScreenCode(byte c)
    {
        int v = c & 0x7F;
        int sc = v < 0x20 ? v + 0x40 : v < 0x60 ? v - 0x20 : v;
        return sc | (c & 0x80);
    }

    // text area geometry (mode 0: whole screen; other modes: the 4-line text window)
    bool Window => m.Ram[Machine.DINDEX] != 0;
    int Base => Window ? m.Txtmsc : m.Savmsc;
    int Rows => Window ? 4 : 24;
    const int Cols = 40;

    int Row { get => Window ? m.Ram[Machine.TXTROW] : m.Ram[Machine.ROWCRS]; set { if (Window) m.Ram[Machine.TXTROW] = (byte)value; else m.Ram[Machine.ROWCRS] = (byte)value; } }
    int Col
    {
        get => Window ? m.Ram[Machine.TXTCOL] | m.Ram[Machine.TXTCOL + 1] << 8 : m.Ram[Machine.COLCRS] | m.Ram[Machine.COLCRS + 1] << 8;
        set { int a = Window ? Machine.TXTCOL : Machine.COLCRS; m.Ram[a] = (byte)(value & 255); m.Ram[a + 1] = (byte)(value >> 8); }
    }
    int LMargin => m.Ram[Machine.LMARGN];
    int RMargin => Math.Min((int)m.Ram[Machine.RMARGN], Cols - 1);

    public int CursorRow => Row;
    public int CursorCol => Col;

    public void SetCursor(int col, int row)
    {
        Col = Math.Clamp(col, 0, Cols - 1); Row = Math.Clamp(row, 0, Rows - 1);
    }

    public void Clear()
    {
        Array.Clear(m.Ram, Base, Rows * Cols);
        Row = 0; Col = LMargin;
    }

    void Scroll()
    {
        int b = Base;
        Array.Copy(m.Ram, b + Cols, m.Ram, b, (Rows - 1) * Cols);
        Array.Clear(m.Ram, b + (Rows - 1) * Cols, Cols);
    }

    void NewLine()
    {
        Col = LMargin;
        if (Row + 1 >= Rows) Scroll(); else Row = Row + 1;
    }

    void Glyph(byte c)
    {
        m.Ram[Base + Row * Cols + Col] = (byte)ToScreenCode(c);
        if (Col >= RMargin) NewLine(); else Col = Col + 1;
    }

    public void PutGlyphForEcho(byte c) { OnPrinted?.Invoke(c); Glyph(c); }

    public void Put(byte c)
    {
        OnPrinted?.Invoke(c);
        if (esc) { esc = false; Glyph(c); return; }
        switch (c)
        {
            case 0x1B: esc = true; break;
            case 0x7D: Clear(); break;
            case 0x1C: Row = Row == 0 ? Rows - 1 : Row - 1; break;
            case 0x1D: Row = Row == Rows - 1 ? 0 : Row + 1; break;
            case 0x1E: Col = Col <= LMargin ? RMargin : Col - 1; break;
            case 0x1F: Col = Col >= RMargin ? LMargin : Col + 1; break;
            case 0x7E:
                if (Col > LMargin) { Col = Col - 1; m.Ram[Base + Row * Cols + Col] = 0; }
                break;
            case 0x7F: Col = Math.Min(RMargin, (Col / 8 + 1) * 8); break;
            case 0x9B: NewLine(); break;
            case 0x9C:
                { int b = Base + Row * Cols; Array.Copy(m.Ram, b + Cols, m.Ram, b, (Rows - 1 - Row) * Cols); Array.Clear(m.Ram, Base + (Rows - 1) * Cols, Cols); Col = LMargin; }
                break;
            case 0x9D:
                { int b = Base + Row * Cols; Array.Copy(m.Ram, b, m.Ram, b + Cols, (Rows - 1 - Row) * Cols); Array.Clear(m.Ram, b, Cols); }
                break;
            case 0xFD: OnBell?.Invoke(); break;
            case 0xFE:
                { int b = Base + Row * Cols; for (int i = Col; i < Cols - 1; i++) m.Ram[b + i] = m.Ram[b + i + 1]; m.Ram[b + Cols - 1] = 0; }
                break;
            case 0xFF:
                { int b = Base + Row * Cols; for (int i = Cols - 1; i > Col; i--) m.Ram[b + i] = m.Ram[b + i - 1]; m.Ram[b + Col] = 0; }
                break;
            default: Glyph(c); break;
        }
    }

    public void Put(byte[] s) { foreach (var b in s) Put(b); }
    public void Put(string ascii) { foreach (char ch in ascii) Put((byte)ch); }

    /// <summary>Current column for PRINT comma tabbing (10 column stops).</summary>
    public int TabColumn => Col;
}
