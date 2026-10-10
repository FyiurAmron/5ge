using System;

namespace Eepch.Core;

/// <summary>
/// A small 6502 interpreter used ONLY for the tiny machine-code snippets Basic programs poke into memory
/// (display list interrupt handlers, USR routines).  Official opcodes, no cycle accuracy, no decimal mode.
/// </summary>
public sealed class Cpu6502
{
    readonly Machine m;
    public int A, X, Y, SP = 0xFF, PC;
    bool fC, fZ, fI, fD, fV, fN;

    public Cpu6502(Machine machine) { m = machine; }

    int Rd(int a) => m.Peek(a);
    void Wr(int a, int v) => m.Poke(a, v);
    int Rd16(int a) => Rd(a) | Rd((a + 1) & 0xFFFF) << 8;
    void Push(int v) { Wr(0x100 + SP, v & 255); SP = (SP - 1) & 255; }
    int Pull() { SP = (SP + 1) & 255; return Rd(0x100 + SP); }
    int Fetch() { int v = Rd(PC); PC = (PC + 1) & 0xFFFF; return v; }
    int Fetch16() { int l = Fetch(); return l | Fetch() << 8; }

    void SetNZ(int v) { fZ = (v & 255) == 0; fN = (v & 0x80) != 0; }
    int GetP() => (fN ? 0x80 : 0) | (fV ? 0x40 : 0) | 0x30 | (fD ? 8 : 0) | (fI ? 4 : 0) | (fZ ? 2 : 0) | (fC ? 1 : 0);
    void SetP(int p) { fN = (p & 0x80) != 0; fV = (p & 0x40) != 0; fD = (p & 8) != 0; fI = (p & 4) != 0; fZ = (p & 2) != 0; fC = (p & 1) != 0; }

    int EA(int mode, int op)
    {
        switch (mode)
        {
            case 0: { int z = (Fetch() + X) & 255; return Rd(z) | Rd((z + 1) & 255) << 8; }       // (zp,X)
            case 1: return Fetch();                                                              // zp
            case 3: return Fetch16();                                                            // abs
            case 4: { int z = Fetch(); return ((Rd(z) | Rd((z + 1) & 255) << 8) + Y) & 0xFFFF; }   // (zp),Y
            case 5: return (Fetch() + (op == 0x96 || op == 0xB6 ? Y : X)) & 255; // zp,X / zp,Y
            case 6: return (Fetch16() + Y) & 0xFFFF;                                             // abs,Y
            default: return (Fetch16() + (op == 0xBE ? Y : X)) & 0xFFFF;                         // abs,X / abs,Y
        }
    }

    void Adc(int v)
    {
        int r = A + v + (fC ? 1 : 0);
        fV = ((~(A ^ v) & (A ^ r)) & 0x80) != 0;
        fC = r > 255; A = r & 255; SetNZ(A);
    }

    void Cmp(int reg, int v) { int r = reg - v; fC = reg >= v; SetNZ(r); }

    void Branch(bool cond) { int off = (sbyte)Fetch(); if (cond) PC = (PC + off) & 0xFFFF; }

    /// <summary>Runs from <paramref name="addr"/> until RTI, or RTS at depth 0, or BRK, or maxInstr. Returns false if cold start was requested.</summary>
    public bool Run(int addr, int maxInstr = 20000)
    {
        PC = addr; int depth = 0;
        for (int n = 0; n < maxInstr; n++)
        {
            if (PC >= 0xC000 && PC >= 0xE000)
            {
                if (PC == 0xE477 || PC == 0xE474) { m.ColdStartRequested = true; return false; }
                // any other OS ROM entry: behave like an immediate RTS
                if (depth == 0) return true;
                int l = Pull(), h = Pull(); PC = ((l | h << 8) + 1) & 0xFFFF; depth--;
                continue;
            }
            int op = Fetch();
            int cc = op & 3, bbb = (op >> 2) & 7, aaa = op >> 5;
            if (cc == 1)
            {
                if (op == 0x89) { PC++; continue; }
                int v;
                int ea = -1;
                if (bbb == 2) v = Fetch();
                else { ea = EA(bbb, op); v = aaa == 4 ? 0 : Rd(ea); }
                switch (aaa)
                {
                    case 0: A |= v; SetNZ(A); break;
                    case 1: A &= v; SetNZ(A); break;
                    case 2: A ^= v; SetNZ(A); break;
                    case 3: Adc(v); break;
                    case 4: Wr(ea, A); break;
                    case 5: A = v; SetNZ(A); break;
                    case 6: Cmp(A, v); break;
                    case 7: Adc(v ^ 255); break;
                }
                continue;
            }
            if (cc == 2)
            {
                if (bbb == 2 && aaa < 4)   // accumulator shifts
                {
                    A = Shift(aaa, A); continue;
                }
                if (op == 0x8A) { A = X; SetNZ(A); continue; }
                if (op == 0x9A) { SP = X; continue; }
                if (op == 0xAA) { X = A; SetNZ(X); continue; }
                if (op == 0xBA) { X = SP; SetNZ(X); continue; }
                if (op == 0xCA) { X = (X - 1) & 255; SetNZ(X); continue; }
                if (op == 0xEA) continue;
                if (aaa == 5 && bbb == 0) { X = Fetch(); SetNZ(X); continue; }   // LDX #
                int ea = EA(bbb, op);
                switch (aaa)
                {
                    case 4: Wr(ea, X); break;
                    case 5: X = Rd(ea); SetNZ(X); break;
                    case 6: { int v = (Rd(ea) - 1) & 255; Wr(ea, v); SetNZ(v); break; }
                    case 7: { int v = (Rd(ea) + 1) & 255; Wr(ea, v); SetNZ(v); break; }
                    default: { int v = Shift(aaa, Rd(ea)); Wr(ea, v); break; }
                }
                continue;
            }
            if (cc == 0)
            {
                switch (op)
                {
                    case 0x10: Branch(!fN); continue;
                    case 0x30: Branch(fN); continue;
                    case 0x50: Branch(!fV); continue;
                    case 0x70: Branch(fV); continue;
                    case 0x90: Branch(!fC); continue;
                    case 0xB0: Branch(fC); continue;
                    case 0xD0: Branch(!fZ); continue;
                    case 0xF0: Branch(fZ); continue;
                    case 0x00: return true;
                    case 0x08: Push(GetP()); continue;
                    case 0x28: SetP(Pull()); continue;
                    case 0x48: Push(A); continue;
                    case 0x68: A = Pull(); SetNZ(A); continue;
                    case 0x88: Y = (Y - 1) & 255; SetNZ(Y); continue;
                    case 0xA8: Y = A; SetNZ(Y); continue;
                    case 0xC8: Y = (Y + 1) & 255; SetNZ(Y); continue;
                    case 0xE8: X = (X + 1) & 255; SetNZ(X); continue;
                    case 0x18: fC = false; continue;
                    case 0x38: fC = true; continue;
                    case 0x58: fI = false; continue;
                    case 0x78: fI = true; continue;
                    case 0x98: A = Y; SetNZ(A); continue;
                    case 0xB8: fV = false; continue;
                    case 0xD8: fD = false; continue;
                    case 0xF8: fD = true; continue;
                    case 0x20: { int t = Fetch16(); int r = (PC - 1) & 0xFFFF; Push(r >> 8); Push(r & 255); PC = t; depth++; continue; }
                    case 0x40: return true;
                    case 0x60:
                        if (depth == 0) return true;
                        { int l = Pull(), h = Pull(); PC = ((l | h << 8) + 1) & 0xFFFF; depth--; }
                        continue;
                    case 0x4C: PC = Fetch16(); continue;
                    case 0x6C: { int p = Fetch16(); PC = Rd(p) | Rd((p & 0xFF00) | ((p + 1) & 0xFF)) << 8; continue; }
                }
                int mode = bbb;
                int val;
                int ea = -1;
                if (mode == 0) val = Fetch();      // immediate (LDY/CPY/CPX #)
                else { ea = mode switch { 1 => Fetch(), 3 => Fetch16(), 5 => (Fetch() + X) & 255, _ => (Fetch16() + X) & 0xFFFF }; val = Rd(ea); }
                switch (aaa)
                {
                    case 1: fZ = (A & val) == 0; fN = (val & 0x80) != 0; fV = (val & 0x40) != 0; break; // BIT
                    case 4: Wr(ea, Y); break;
                    case 5: Y = val; SetNZ(Y); break;
                    case 6: Cmp(Y, val); break;
                    case 7: Cmp(X, val); break;
                }
                continue;
            }
            return true; // unknown opcode: stop quietly
        }
        return true;
    }

    int Shift(int kind, int v)
    {
        int r;
        switch (kind)
        {
            case 0: fC = (v & 0x80) != 0; r = (v << 1) & 255; break;                                   // ASL
            case 1: { int c = fC ? 1 : 0; fC = (v & 0x80) != 0; r = ((v << 1) | c) & 255; break; }     // ROL
            case 2: fC = (v & 1) != 0; r = v >> 1; break;                                               // LSR
            default: { int c = fC ? 0x80 : 0; fC = (v & 1) != 0; r = (v >> 1) | c; break; }            // ROR
        }
        SetNZ(r); return r;
    }

    /// <summary>Atari BASIC USR(): arguments pushed (hi, lo), then count; result in FR0 ($D4/$D5).</summary>
    public int Usr(int addr, int[] args)
    {
        SP = 0xFF;
        foreach (int a in args) { Push((a >> 8) & 255); Push(a & 255); }
        Push(args.Length);
        Push(0xFF); Push(0xFE); // fake return address
        A = X = Y = 0;
        Run(addr, 200000);
        return m.Ram[0xD4] | m.Ram[0xD5] << 8;
    }
}
