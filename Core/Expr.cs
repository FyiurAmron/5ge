using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Eepch.Core;

public sealed class BasicError : Exception
{
    public readonly int Code;
    public BasicError(int code, string msg = null) : base(msg ?? ("ERROR- " + code)) { Code = code; }
}

public sealed class VarSlot
{
    public string Name = "";
    public bool IsStr;
    public double Num;
    public byte[] Buf;      // string storage (capacity = DIM size)
    public int Len;
    public double[] Arr;    // numeric array
    public int D1, D2;      // DIM sizes (highest index)

    public void Clear() { Num = 0; Buf = null; Len = 0; Arr = null; D1 = D2 = 0; }

    public byte[] StrValue()
    {
        if (Buf == null) throw new BasicError(9);
        var r = new byte[Len]; Array.Copy(Buf, r, Len); return r;
    }

    public byte[] Slice(int n, int m)
    {
        if (Buf == null) throw new BasicError(9);
        if (n < 1 || m > Len || n > m + 1) throw new BasicError(5);
        var r = new byte[m - n + 1]; Array.Copy(Buf, n - 1, r, 0, r.Length); return r;
    }

    public void AssignAll(byte[] s)
    {
        if (Buf == null) throw new BasicError(9);
        int n = Math.Min(s.Length, Buf.Length);
        Array.Copy(s, Buf, n); Len = n;
    }

    /// <summary>A$(n,m)=s : copies into the range, grows the length if it extends past it. m &lt; 0 = open ended.</summary>
    public void AssignSlice(int n, int m, byte[] s)
    {
        if (Buf == null) throw new BasicError(9);
        if (m < 0) m = Math.Min(n + s.Length - 1, Buf.Length);
        if (n < 1 || m > Buf.Length || n > m + 1 || n > Buf.Length) throw new BasicError(5);
        int cnt = Math.Min(s.Length, m - n + 1);
        Array.Copy(s, 0, Buf, n - 1, cnt);
        if (n + cnt - 1 > Len) Len = n + cnt - 1;
    }

    public int ArrIndex(int i, int j)
    {
        if (Arr == null) throw new BasicError(9);
        if (i < 0 || i > D1 || j < 0 || j > D2) throw new BasicError(9);
        return D2 == 0 && j == 0 ? i : i * (D2 + 1) + j;
    }
}

public sealed class SymTab
{
    readonly Dictionary<string, VarSlot> map = new();
    public VarSlot Get(string name)
    {
        if (!map.TryGetValue(name, out var s)) map[name] = s = new VarSlot { Name = name, IsStr = name.EndsWith("$") };
        return s;
    }
    public void ClearAll() { foreach (var v in map.Values) v.Clear(); }
}

public static class Num
{
    public static int ToInt(double d)
    {
        double r = Math.Floor(d + 0.5);
        if (r > int.MaxValue) return int.MaxValue;
        if (r < int.MinValue) return int.MinValue;
        return (int)r;
    }

    public static string Format(double v)
    {
        if (v == 0) return "0";
        double a = Math.Abs(v);
        string s;
        if (a >= 1e10 || a < 1e-2)
        {
            s = v.ToString("0.########E+00", CultureInfo.InvariantCulture);
            return s;
        }
        if (v == Math.Floor(v)) return ((long)v).ToString(CultureInfo.InvariantCulture);
        int intDigits = (int)Math.Floor(Math.Log10(a)) + 1;
        s = Math.Round(v, Math.Max(0, 9 - Math.Max(intDigits, 0))).ToString("0.#########", CultureInfo.InvariantCulture);
        if (s.StartsWith("0.")) s = s.Substring(1);
        else if (s.StartsWith("-0.")) s = "-" + s.Substring(2);
        return s;
    }

    public static bool TryParse(byte[] b, out double v, bool prefixOnly)
    {
        v = 0;
        int i = 0, n = b.Length;
        while (i < n && b[i] == 0x20) i++;
        int st = i;
        if (i < n && (b[i] == '+' || b[i] == '-')) i++;
        int digits = 0;
        while (i < n && b[i] >= '0' && b[i] <= '9') { i++; digits++; }
        if (i < n && b[i] == '.') { i++; while (i < n && b[i] >= '0' && b[i] <= '9') { i++; digits++; } }
        if (digits == 0) return false;
        if (i < n && (b[i] == 'E' || b[i] == 'e'))
        {
            int j = i + 1;
            if (j < n && (b[j] == '+' || b[j] == '-')) j++;
            int ed = 0;
            while (j < n && b[j] >= '0' && b[j] <= '9') { j++; ed++; }
            if (ed > 0) i = j;
        }
        int end = i;
        while (i < n && b[i] == 0x20) i++;
        if (!prefixOnly && i != n) return false;
        return double.TryParse(Encoding.ASCII.GetString(b, st, end - st), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }
}

public abstract class Expr
{
    public virtual bool IsStr => false;
    public abstract double Num(Interp it);
    public virtual byte[] Str(Interp it) => throw new BasicError(9, "type mismatch");
}

public sealed class ConstE : Expr
{
    readonly double v; public ConstE(double d) { v = d; }
    public double Value => v;
    public override double Num(Interp it) => v;
}

public sealed class StrConstE : Expr
{
    readonly byte[] v; public StrConstE(byte[] b) { v = b; }
    public override bool IsStr => true;
    public override double Num(Interp it) => throw new BasicError(9);
    public override byte[] Str(Interp it) => v;
}

public sealed class NumVarE : Expr
{
    readonly VarSlot s; public NumVarE(VarSlot slot) { s = slot; }
    public override double Num(Interp it) => s.Num;
}

public sealed class ArrE : Expr
{
    readonly VarSlot s; readonly Expr i, j;
    public ArrE(VarSlot slot, Expr a, Expr b) { s = slot; i = a; j = b; }
    public override double Num(Interp it)
    {
        int a = Eepch.Core.Num.ToInt(i.Num(it)), b = j == null ? 0 : Eepch.Core.Num.ToInt(j.Num(it));
        return s.Arr == null ? throw new BasicError(9) : s.Arr[s.ArrIndex(a, b)];
    }
}

public sealed class StrVarE : Expr
{
    readonly VarSlot s; public StrVarE(VarSlot slot) { s = slot; }
    public override bool IsStr => true;
    public override double Num(Interp it) => throw new BasicError(9);
    public override byte[] Str(Interp it) => s.StrValue();
}

public sealed class SliceE : Expr
{
    readonly VarSlot s; readonly Expr a, b;
    public SliceE(VarSlot slot, Expr from, Expr to) { s = slot; a = from; b = to; }
    public override bool IsStr => true;
    public override double Num(Interp it) => throw new BasicError(9);
    public override byte[] Str(Interp it)
    {
        int n = Eepch.Core.Num.ToInt(a.Num(it));
        int m = b == null ? s.Len : Eepch.Core.Num.ToInt(b.Num(it));
        return s.Slice(n, m);
    }
}

public sealed class NegE : Expr
{
    readonly Expr e; public NegE(Expr x) { e = x; }
    public override double Num(Interp it) => -e.Num(it);
}

public sealed class NotE : Expr
{
    readonly Expr e; public NotE(Expr x) { e = x; }
    public override double Num(Interp it) => e.Num(it) == 0 ? 1 : 0;
}

public enum BinOp { Add, Sub, Mul, Div, Pow, And, Or, Exor, IDiv, Mod, BitAnd, BitOr }

public sealed class BinE : Expr
{
    readonly BinOp op; readonly Expr l, r;
    public BinE(BinOp o, Expr a, Expr b) { op = o; l = a; r = b; }
    public override double Num(Interp it)
    {
        double a = l.Num(it), b = r.Num(it);
        switch (op)
        {
            case BinOp.Add: return Check(a + b);
            case BinOp.Sub: return Check(a - b);
            case BinOp.Mul: return Check(a * b);
            case BinOp.Div: if (b == 0) throw new BasicError(11); return Check(a / b);
            case BinOp.Pow:
                if (a == 0 && b <= 0) throw new BasicError(3);
                if (a < 0 && b != Math.Floor(b)) throw new BasicError(3);
                return Check(Math.Pow(a, b));
            case BinOp.And: return a != 0 && b != 0 ? 1 : 0;
            case BinOp.Or: return a != 0 || b != 0 ? 1 : 0;
            case BinOp.Exor: return (Eepch.Core.Num.ToInt(a) & 0xFFFF) ^ (Eepch.Core.Num.ToInt(b) & 0xFFFF);
            case BinOp.BitAnd: return (Eepch.Core.Num.ToInt(a) & 0xFFFF) & (Eepch.Core.Num.ToInt(b) & 0xFFFF);
            case BinOp.BitOr: return (Eepch.Core.Num.ToInt(a) & 0xFFFF) | (Eepch.Core.Num.ToInt(b) & 0xFFFF);
            case BinOp.IDiv: if ((int)b == 0) throw new BasicError(11); return Math.Truncate(a / b);
            default: if (b == 0) throw new BasicError(11); return a - b * Math.Truncate(a / b);
        }
    }

    static double Check(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) >= 1e98) throw new BasicError(3);
        return d;
    }
}

public sealed class CmpE : Expr
{
    readonly string op; readonly Expr l, r; readonly bool str;
    public CmpE(string o, Expr a, Expr b) { op = o; l = a; r = b; str = a.IsStr; }
    public override double Num(Interp it)
    {
        int c;
        if (str)
        {
            var a = l.Str(it); var b = r.Str(it);
            c = a.AsSpan().SequenceCompareTo(b);
        }
        else c = l.Num(it).CompareTo(r.Num(it));
        bool res = op switch { "=" => c == 0, "<>" => c != 0, "<" => c < 0, ">" => c > 0, "<=" => c <= 0, _ => c >= 0 };
        return res ? 1 : 0;
    }
}

public enum Fn
{
    Str, Chr, Usr, Asc, Val, Len, Adr, Atn, Cos, Peek, Sin, Rnd, Fre, Exp, Log, Clog, Sqr, Sgn, Abs, Int,
    Paddle, Stick, Ptrig, Strig, Dpeek, Instr, Inkey, Hex, Dec, Frac, TimeStr, Time, Rand, Trunc, Err, Erl, Uinstr
}

public sealed class FuncE : Expr
{
    public static readonly Dictionary<string, (Fn f, int min, int max, bool str)> Table = new()
    {
        ["STR$"] = (Fn.Str, 1, 1, true), ["CHR$"] = (Fn.Chr, 1, 1, true), ["USR"] = (Fn.Usr, 1, 9, false), ["ASC"] = (Fn.Asc, 1, 1, false),
        ["VAL"] = (Fn.Val, 1, 1, false), ["LEN"] = (Fn.Len, 1, 1, false), ["ADR"] = (Fn.Adr, 1, 1, false), ["ATN"] = (Fn.Atn, 1, 1, false),
        ["COS"] = (Fn.Cos, 1, 1, false), ["PEEK"] = (Fn.Peek, 1, 1, false), ["SIN"] = (Fn.Sin, 1, 1, false), ["RND"] = (Fn.Rnd, 0, 1, false),
        ["FRE"] = (Fn.Fre, 0, 1, false), ["EXP"] = (Fn.Exp, 1, 1, false), ["LOG"] = (Fn.Log, 1, 1, false), ["CLOG"] = (Fn.Clog, 1, 1, false),
        ["SQR"] = (Fn.Sqr, 1, 1, false), ["SGN"] = (Fn.Sgn, 1, 1, false), ["ABS"] = (Fn.Abs, 1, 1, false), ["INT"] = (Fn.Int, 1, 1, false),
        ["PADDLE"] = (Fn.Paddle, 1, 1, false), ["STICK"] = (Fn.Stick, 1, 1, false), ["PTRIG"] = (Fn.Ptrig, 1, 1, false), ["STRIG"] = (Fn.Strig, 1, 1, false),
        ["DPEEK"] = (Fn.Dpeek, 1, 1, false), ["INSTR"] = (Fn.Instr, 2, 3, false), ["INKEY$"] = (Fn.Inkey, 0, 0, true), ["HEX$"] = (Fn.Hex, 1, 1, true),
        ["DEC"] = (Fn.Dec, 1, 1, false), ["FRAC"] = (Fn.Frac, 1, 1, false), ["TIME$"] = (Fn.TimeStr, 0, 0, true), ["TIME"] = (Fn.Time, 0, 0, false),
        ["RAND"] = (Fn.Rand, 1, 1, false), ["TRUNC"] = (Fn.Trunc, 1, 1, false), ["ERR"] = (Fn.Err, 0, 0, false), ["ERL"] = (Fn.Erl, 0, 0, false),
        ["UINSTR"] = (Fn.Uinstr, 2, 3, false),
    };

    readonly Fn f; readonly Expr[] a; readonly bool str;
    public FuncE(Fn fn, Expr[] args, bool isStr) { f = fn; a = args; str = isStr; }
    public override bool IsStr => str;

    public override double Num(Interp it)
    {
        switch (f)
        {
            case Fn.Usr:
            {
                int addr = Eepch.Core.Num.ToInt(a[0].Num(it));
                var args = new int[a.Length - 1];
                for (int i = 1; i < a.Length; i++) args[i - 1] = Eepch.Core.Num.ToInt(a[i].Num(it)) & 0xFFFF;
                return it.CallUsr(addr, args);
            }
            case Fn.Asc: { var s = a[0].Str(it); if (s.Length == 0) throw new BasicError(5); return s[0]; }
            case Fn.Val:
            {
                var s = a[0].Str(it);
                if (!Eepch.Core.Num.TryParse(s, out double v, false)) throw new BasicError(18);
                return v;
            }
            case Fn.Len: return a[0].Str(it).Length;
            case Fn.Adr: return it.AddressOf(a[0]);
            case Fn.Atn: return it.FromRad(Math.Atan(a[0].Num(it)));
            case Fn.Cos: return Math.Cos(it.ToRad(a[0].Num(it)));
            case Fn.Sin: return Math.Sin(it.ToRad(a[0].Num(it)));
            case Fn.Peek: return it.M.Peek(CheckAddr(a[0].Num(it)));
            case Fn.Dpeek: return it.M.DPeek(CheckAddr(a[0].Num(it)));
            case Fn.Rnd: return it.M.Rng.NextDouble();
            case Fn.Rand: { int n = Eepch.Core.Num.ToInt(a[0].Num(it)); return n <= 0 ? 0 : it.M.Rng.Next(n); }
            case Fn.Fre: return 20000;
            case Fn.Exp: return Math.Exp(a[0].Num(it));
            case Fn.Log: { double d = a[0].Num(it); if (d <= 0) throw new BasicError(3); return Math.Log(d); }
            case Fn.Clog: { double d = a[0].Num(it); if (d <= 0) throw new BasicError(3); return Math.Log10(d); }
            case Fn.Sqr: { double d = a[0].Num(it); if (d < 0) throw new BasicError(3); return Math.Sqrt(d); }
            case Fn.Sgn: return Math.Sign(a[0].Num(it));
            case Fn.Abs: return Math.Abs(a[0].Num(it));
            case Fn.Int: return Math.Floor(a[0].Num(it));
            case Fn.Trunc: return Math.Truncate(a[0].Num(it));
            case Fn.Frac: { double d = a[0].Num(it); return d - Math.Truncate(d); }
            case Fn.Paddle: return 228;
            case Fn.Ptrig: return 1;
            case Fn.Stick: return it.M.StickState;
            case Fn.Strig: return it.M.TrigState;
            case Fn.Dec:
            {
                string s = Encoding.ASCII.GetString(a[0].Str(it)).Trim();
                if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) throw new BasicError(18);
                return v;
            }
            case Fn.Time: return Math.Floor(it.Clock * 50.0) % 16777216;
            case Fn.Err: return it.ErrCode;
            case Fn.Erl: return it.ErrLine;
            case Fn.Instr:
            case Fn.Uinstr:
            {
                var s = a[0].Str(it); var t = a[1].Str(it);
                int start = a.Length > 2 ? Eepch.Core.Num.ToInt(a[2].Num(it)) : 1;
                if (start < 1) throw new BasicError(5);
                if (t.Length == 0) return start;
                for (int i = start - 1; i + t.Length <= s.Length; i++)
                {
                    bool ok = true;
                    for (int k = 0; k < t.Length && ok; k++)
                    {
                        byte x = s[i + k], y = t[k];
                        if (f == Fn.Uinstr) { x = Up(x); y = Up(y); }
                        ok = x == y;
                    }
                    if (ok) return i + 1;
                }
                return 0;
            }
            default: throw new BasicError(9);
        }
    }

    static byte Up(byte b) => b >= 'a' && b <= 'z' ? (byte)(b - 32) : b;

    static int CheckAddr(double d)
    {
        int v = Eepch.Core.Num.ToInt(d);
        if (v < 0 || v > 65535) throw new BasicError(7);
        return v;
    }

    public override byte[] Str(Interp it)
    {
        switch (f)
        {
            case Fn.Str: return Encoding.ASCII.GetBytes(Eepch.Core.Num.Format(a[0].Num(it)));
            case Fn.Chr:
            {
                int v = Eepch.Core.Num.ToInt(a[0].Num(it));
                if (v < 0 || v > 255) throw new BasicError(3);
                return new[] { (byte)v };
            }
            case Fn.Hex:
            {
                int v = Eepch.Core.Num.ToInt(a[0].Num(it));
                if (v < 0 || v > 65535) throw new BasicError(3);
                return Encoding.ASCII.GetBytes(v.ToString("X4"));
            }
            case Fn.Inkey: return it.Keys.Count > 0 ? new[] { it.Keys.Dequeue() } : Array.Empty<byte>();
            case Fn.TimeStr:
            {
                int s = (int)it.Clock; return Encoding.ASCII.GetBytes($"{s / 3600 % 24:00}:{s / 60 % 60:00}:{s % 60:00}");
            }
            default: throw new BasicError(9);
        }
    }
}
