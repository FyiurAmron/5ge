using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Eepch.Core;

/// <summary>
/// Turbo-BASIC XL style interpreter operating on text listings.  Statements execute one at a time
/// (Step) so the host can interleave rendering/audio/input.  Simulated time (Clock, seconds) advances
/// by a per-statement cost, which keeps SOUND envelopes and PAUSE at a believable speed.
/// </summary>
public sealed class Interp
{
    public readonly Machine M = new();
    public readonly ScreenEditor Scr;
    public readonly Gfx G;
    public readonly Pokey Audio = new();
    public readonly SymTab Sym = new();
    public readonly Queue<byte> Keys = new();
    public DiskSet Disks = new();
    public Action<string> ConsoleLine;      // text mirrored line by line (host console)
    public Action<string> Log;
    public double Clock;
    public double Speed = 1.0;
    public int ErrCode, ErrLine;
    public bool Halted;
    public string HaltReason = "";
    public bool Deg;
    public string ProgramName = "";
    public long StatementsExecuted;
    public Action<int, K> OnStep;      // optional execution trace (line number, statement kind)

    readonly Cpu6502 cpu;
    ProgramLine[] lines = Array.Empty<ProgramLine>();
    Dictionary<int, int> lineIndex = new();
    Dictionary<string, (int li, int si)> procs = new();
    List<(int line, byte[] item)> data = new();
    int dataPos;
    int li, si, curLine;
    int trapLine = -1;
    enum FK { For, Gosub, Exec, Do, While, Repeat }
    struct Frame { public FK Kind; public VarSlot Var; public double Limit, Step; public int Li, Si; }
    readonly List<Frame> stack = new();

    // blocking state
    public bool WaitKey;
    double wakeAt = -1;
    bool inputActive;
    readonly List<byte> inBuf = new();

    sealed class Chan
    {
        public string Name = ""; public bool Screen, Kbd, Write, Null;
        public byte[] Data = Array.Empty<byte>(); public int Pos; public MemoryStream Out;
    }
    readonly Chan[] chans = new Chan[8];
    readonly StringBuilder mirror = new();

    public Interp()
    {
        Scr = new ScreenEditor(M);
        G = new Gfx(M);
        cpu = new Cpu6502(M);
        M.Clock = () => Clock;
        M.KeyCode = () => Keys.Count > 0 ? Keys.Peek() : 255;
        Scr.OnPrinted = Mirror;
        G.SetMode(0);
    }

    void Mirror(byte b)
    {
        if (b == 0x9B) { ConsoleLine?.Invoke(mirror.ToString()); mirror.Clear(); return; }
        if (b == 0x7D) { return; }
        char c = AtasciiText.ToConsoleChar(b);
        if (c != '\0') mirror.Append(c);
    }

    // ------------------------------------------------------------------ program loading
    public void LoadListing(List<ListingLine> listing, string name)
    {
        ProgramName = name;
        var pl = new List<ProgramLine>();
        foreach (var l in listing)
            pl.Add(new ProgramLine { Number = l.Number, Stmts = LineParser.ParseLine(l.Text, Sym, l.Number) });
        pl.Sort((a, b) => a.Number.CompareTo(b.Number));
        lines = pl.ToArray();
        lineIndex = new Dictionary<int, int>();
        procs = new Dictionary<string, (int, int)>();
        data = new List<(int, byte[])>();
        for (int i = 0; i < lines.Length; i++)
        {
            lineIndex[lines[i].Number] = i;
            for (int j = 0; j < lines[i].Stmts.Length; j++)
            {
                var s = lines[i].Stmts[j];
                if (s.Kind == K.Proc) procs[s.Name] = (i, j + 1);
                else if (s.Kind == K.Data) foreach (var it in s.Data) data.Add((lines[i].Number, it));
            }
        }
    }

    public void ClearRuntime()
    {
        Sym.ClearAll(); stack.Clear(); trapLine = -1; dataPos = 0; li = 0; si = 0;
        Halted = false; HaltReason = ""; wakeAt = -1; inputActive = false; WaitKey = false;
        for (int i = 0; i < chans.Length; i++) chans[i] = null;
    }

    byte[] LoadFileBytes(string name)
    {
        if (!Disks.TryGet(name, out var d)) throw new BasicError(170);
        return d;
    }

    /// <summary>RUN "D:FILE" semantics: load a BASIC file (tokenized or text), clear variables, start at the first line.</summary>
    public void RunFile(string name)
    {
        var bytes = LoadFileBytes(name);
        List<ListingLine> listing;
        try { listing = TbDetokenizer.LoadAny(bytes); }
        catch (FormatException) { throw new BasicError(21); }
        if (listing.Count == 0) throw new BasicError(21);
        LoadListing(listing, AtrImage.NormalizeName(name));
        ClearRuntime();
    }

    /// <summary>Resets memory, screen, sound and channels; leaves the interpreter halted with no program.</summary>
    public void PowerOn()
    {
        M.Reset(); G.SetMode(0);
        Audio.Reset(); Keys.Clear(); mirror.Clear();
        for (int i = 0; i < chans.Length; i++) chans[i] = null;
        Deg = false; Halted = true; HaltReason = ""; wakeAt = -1; inputActive = false; WaitKey = false;
        stack.Clear(); trapLine = -1; lines = Array.Empty<ProgramLine>(); li = si = 0;
    }

    /// <summary>Power-on, then what the real disk does (DOS boot -> MAIN1.SYS runtime -> RUN "D:MAIN2.SYS").</summary>
    public void ColdBoot(string firstProgram = "D:MAIN2.SYS")
    {
        PowerOn();
        try { RunFile(firstProgram); }
        catch (BasicError e)
        {
            Halt("ERROR- " + e.Code + " (cannot load " + firstProgram + ")");
        }
    }

    // ------------------------------------------------------------------ scheduling
    public void KeyIn(byte b) { Keys.Enqueue(b); }

    /// <summary>Runs statements until the simulated clock reaches <paramref name="now"/> (host real time, seconds).</summary>
    public void Pump(double now, int maxSteps = 200000)
    {
        int n = 0;
        while (n++ < maxSteps)
        {
            if (Halted) { Clock = Math.Max(Clock, now); return; }
            if (WaitKey && Keys.Count == 0) { Clock = Math.Max(Clock, now); return; }
            if (wakeAt > Clock)
            {
                if (wakeAt <= now) Clock = wakeAt; else { Clock = Math.Max(Clock, now); return; }
            }
            if (Clock >= now) return;
            Step();
        }
        Clock = Math.Max(Clock, now);
    }

    void Retry() { si--; }

    public void Step()
    {
        if (Halted) return;
        WaitKey = false;
        while (li < lines.Length && si >= lines[li].Stmts.Length) { li++; si = 0; }
        if (li >= lines.Length) { Halt("READY", true); return; }
        var line = lines[li];
        var s = line.Stmts[si];
        si++; curLine = line.Number;
        StatementsExecuted++;
        OnStep?.Invoke(curLine, s.Kind);
        try { Exec(s); }
        catch (BasicError e) { DoError(e.Code); }
        Clock += Cost(s) / Speed;
        if (M.ColdStartRequested) { Halted = true; HaltReason = "COLDSTART"; }
    }

    double Cost(Stmt s)
    {
        switch (s.Kind)
        {
            case K.Sound: return 0.0022;
            case K.Print: return 0.0008;
            case K.Graphics: return 0.02;
            case K.Open: return 0.03;
            case K.Close: return 0.005;
            case K.Rem: case K.Data: case K.Endif: return 0.00005;
            case K.Exec: case K.Gosub: case K.Return: case K.EndProc: return 0.00025;
            default: return 0.00015;
        }
    }

    void DoError(int code)
    {
        ErrCode = code; ErrLine = curLine;
        Log?.Invoke($"[error {code} at line {curLine}{(trapLine >= 0 ? ", trapped to " + trapLine : "")}]");
        if (trapLine >= 0)
        {
            int t = trapLine; trapLine = -1;
            try { JumpLine(t); return; } catch (BasicError) { }
        }
        Halt("ERROR- " + code + " AT LINE " + curLine);
    }

    void Halt(string msg, bool quiet = false)
    {
        Halted = true; HaltReason = msg;
        if (!quiet) { Scr.Put(0x9B); Scr.Put(msg); Scr.Put(0x9B); }
        Scr.Put("READY"); Scr.Put(0x9B);
        Log?.Invoke(msg);
    }

    void JumpLine(int n)
    {
        int lo = 0, hi = lines.Length - 1, res = -1;
        while (lo <= hi) { int mid = (lo + hi) / 2; if (lines[mid].Number >= n) { res = mid; hi = mid - 1; } else lo = mid + 1; }
        if (res < 0) throw new BasicError(12);
        li = res; si = 0;
    }

    // ------------------------------------------------------------------ helpers
    static int I(double d) => Num.ToInt(d);

    public double ToRad(double d) => Deg ? d * Math.PI / 180.0 : d;
    public double FromRad(double d) => Deg ? d * 180.0 / Math.PI : d;

    public int CallUsr(int addr, int[] args)
    {
        if (addr == 0xE477 || addr == 0xE474) { M.ColdStartRequested = true; return 0; }
        return cpu.Usr(addr, args);
    }

    public double AddressOf(Expr e)
    {
        if (e.IsStr) { var b = e.Str(this); Array.Copy(b, 0, M.Ram, 0x0400, Math.Min(b.Length, 0x100)); return 0x0400; }
        return 0;
    }

    int Addr(double d)
    {
        int v = I(d);
        if (v < 0 || v > 65535) throw new BasicError(7);
        return v;
    }

    static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    void SkipBlock(bool stopAtElse)
    {
        int depth = 0, l = li, p = si;
        while (l < lines.Length)
        {
            var st = lines[l].Stmts;
            while (p < st.Length)
            {
                var x = st[p++];
                if (x.Kind == K.If && !x.Flag) depth++;
                else if (x.Kind == K.Endif) { if (depth == 0) { li = l; si = p; return; } depth--; }
                else if (x.Kind == K.Else && depth == 0 && stopAtElse) { li = l; si = p; return; }
            }
            l++; p = 0;
        }
        throw new BasicError(22);
    }

    void SkipTo(K open, K close, K alsoOpen = K.Nop)
    {
        int depth = 0, l = li, p = si;
        while (l < lines.Length)
        {
            var st = lines[l].Stmts;
            while (p < st.Length)
            {
                var x = st[p++];
                if (x.Kind == open) depth++;
                else if (x.Kind == close) { if (depth == 0) { li = l; si = p; return; } depth--; }
            }
            l++; p = 0;
        }
        throw new BasicError(22);
    }

    int FindFrame(params FK[] kinds)
    {
        for (int i = stack.Count - 1; i >= 0; i--)
            foreach (var k in kinds) if (stack[i].Kind == k) return i;
        return -1;
    }

    // ------------------------------------------------------------------ channels / output
    void Out(Chan c, byte[] b)
    {
        if (c == null || c.Screen) { Scr.Put(b); return; }
        if (c.Null) return;
        if (c.Out == null) throw new BasicError(131);
        c.Out.Write(b, 0, b.Length);
    }

    Chan GetChan(Expr e, bool required = true)
    {
        if (e == null) return null;
        int n = I(e.Num(this));
        if (n < 0 || n > 7) throw new BasicError(20);
        if (n == 0) return null;
        var c = chans[n];
        if (c == null && required) throw new BasicError(133);
        return c;
    }

    void AssignLv(LValue t, byte[] bytes)
    {
        var v = t.V;
        if (v.IsStr)
        {
            if (!t.HasIndex) v.AssignAll(bytes);
            else v.AssignSlice(I(t.I1.Num(this)), t.I2 == null ? -1 : I(t.I2.Num(this)), bytes);
        }
        else
        {
            if (!Num.TryParse(bytes, out double d, false)) throw new BasicError(8);
            AssignNum(t, d);
        }
    }

    void AssignNum(LValue t, double d)
    {
        var v = t.V;
        if (!t.HasIndex) v.Num = d;
        else v.Arr[v.ArrIndex(I(t.I1.Num(this)), t.I2 == null ? 0 : I(t.I2.Num(this)))] = d;
    }

    // ------------------------------------------------------------------ the statements
    void Exec(Stmt s)
    {
        switch (s.Kind)
        {
            case K.Rem: case K.Data: case K.Nop: case K.Endif: case K.Fcolor: break;
            case K.SyntaxError: throw new BasicError(17, s.Error);
            case K.Let:
            {
                var t = s.Target; var v = t.V;
                if (v.IsStr)
                {
                    var src = s.A.Str(this);
                    if (!t.HasIndex) v.AssignAll(src);
                    else v.AssignSlice(I(t.I1.Num(this)), t.I2 == null ? -1 : I(t.I2.Num(this)), src);
                }
                else AssignNum(t, s.A.Num(this));
                break;
            }
            case K.Print: ExecPrint(s); break;
            case K.Input: ExecInput(s); break;
            case K.Get:
            {
                var c = GetChan(s.A, false);
                if (c != null && !c.Kbd && !c.Screen)
                {
                    if (c.Pos >= c.Data.Length) throw new BasicError(136);
                    AssignNum(s.Target, c.Data[c.Pos++]); break;
                }
                if (Keys.Count == 0) { Retry(); WaitKey = true; break; }
                AssignNum(s.Target, Keys.Dequeue());
                break;
            }
            case K.Read:
                foreach (var lv in s.Lv)
                {
                    if (dataPos >= data.Count) throw new BasicError(6);
                    AssignLv(lv, data[dataPos++].item);
                }
                break;
            case K.If:
                if (s.A.Num(this) != 0) break;
                if (s.Flag) si = lines[li].Stmts.Length; else SkipBlock(true);
                break;
            case K.Else: SkipBlock(false); break;
            case K.For:
            {
                var v = s.V;
                v.Num = s.A.Num(this);
                double lim = s.B.Num(this), step = s.C == null ? 1 : s.C.Num(this);
                // A fresh FOR on the same variable replaces an older loop entry, but only when nothing but
                // abandoned FOR entries sit above it (never discard EXEC/GOSUB return frames).
                for (int i = stack.Count - 1; i >= 0; i--)
                {
                    if (stack[i].Kind != FK.For) break;
                    if (stack[i].Var == v) { stack.RemoveRange(i, stack.Count - i); break; }
                }
                stack.Add(new Frame { Kind = FK.For, Var = v, Limit = lim, Step = step, Li = li, Si = si });
                break;
            }
            case K.Next:
            {
                int idx = -1;
                for (int i = stack.Count - 1; i >= 0; i--)
                    if (stack[i].Kind == FK.For && (s.V == null || stack[i].Var == s.V)) { idx = i; break; }
                if (idx < 0) throw new BasicError(13);
                if (idx < stack.Count - 1) stack.RemoveRange(idx + 1, stack.Count - idx - 1);
                var f = stack[idx];
                f.Var.Num += f.Step;
                if (f.Step >= 0 ? f.Var.Num <= f.Limit : f.Var.Num >= f.Limit) { li = f.Li; si = f.Si; }
                else stack.RemoveAt(idx);
                break;
            }
            case K.Goto: JumpLine(I(s.A.Num(this))); break;
            case K.Gosub:
                stack.Add(new Frame { Kind = FK.Gosub, Li = li, Si = si });
                JumpLine(I(s.A.Num(this)));
                break;
            case K.OnGoto: case K.OnGosub:
            {
                int n = I(s.A.Num(this));
                if (n < 1 || n > s.L.Length) break;
                if (s.Kind == K.OnGosub) stack.Add(new Frame { Kind = FK.Gosub, Li = li, Si = si });
                JumpLine(I(s.L[n - 1].Num(this)));
                break;
            }
            case K.Return:
            {
                int i = FindFrame(FK.Gosub);
                if (i < 0) throw new BasicError(16);
                var f = stack[i]; stack.RemoveRange(i, stack.Count - i); li = f.Li; si = f.Si;
                break;
            }
            case K.Pop:
                if (stack.Count == 0) throw new BasicError(16);
                stack.RemoveAt(stack.Count - 1);
                break;
            case K.End: Halt("READY", true); break;
            case K.Bye: Halt("READY", true); break;
            case K.Stop: Halt("STOPPED AT LINE " + curLine); break;
            case K.Trap:
            {
                int t = I(s.A.Num(this));
                trapLine = t < 0 || t > 32767 ? -1 : t;
                break;
            }
            case K.Run:
                if (s.A != null)
                {
                    string name = Encoding.ASCII.GetString(s.A.Str(this));
                    RunFile(name);
                }
                else ClearRuntime();
                break;
            case K.Do: stack.Add(new Frame { Kind = FK.Do, Li = li, Si = si }); break;
            case K.Loop:
            {
                int i = FindFrame(FK.Do);
                if (i < 0) throw new BasicError(16);
                if (i < stack.Count - 1) stack.RemoveRange(i + 1, stack.Count - i - 1);
                li = stack[i].Li; si = stack[i].Si;
                break;
            }
            case K.While:
                if (s.A.Num(this) != 0) stack.Add(new Frame { Kind = FK.While, Li = li, Si = si - 1 });
                else SkipTo(K.While, K.Wend);
                break;
            case K.Wend:
            {
                int i = FindFrame(FK.While);
                if (i < 0) throw new BasicError(16);
                var f = stack[i]; stack.RemoveRange(i, stack.Count - i); li = f.Li; si = f.Si;
                break;
            }
            case K.Repeat: stack.Add(new Frame { Kind = FK.Repeat, Li = li, Si = si }); break;
            case K.Until:
            {
                int i = FindFrame(FK.Repeat);
                if (i < 0) throw new BasicError(16);
                if (i < stack.Count - 1) stack.RemoveRange(i + 1, stack.Count - i - 1);
                if (s.A.Num(this) != 0) stack.RemoveAt(i); else { li = stack[i].Li; si = stack[i].Si; }
                break;
            }
            case K.Exit:
            {
                int i = FindFrame(FK.Do, FK.While, FK.Repeat, FK.For);
                if (i < 0) throw new BasicError(16);
                var k = stack[i].Kind;
                stack.RemoveRange(i, stack.Count - i);
                if (k == FK.Do) SkipTo(K.Do, K.Loop);
                else if (k == FK.While) SkipTo(K.While, K.Wend);
                else if (k == FK.Repeat) SkipTo(K.Repeat, K.Until);
                else SkipTo(K.For, K.Next);
                break;
            }
            case K.Proc:
                SkipTo(K.Proc, K.EndProc);
                break;
            case K.Exec:
            {
                if (!procs.TryGetValue(s.Name, out var p)) throw new BasicError(20);
                stack.Add(new Frame { Kind = FK.Exec, Li = li, Si = si });
                li = p.li; si = p.si;
                break;
            }
            case K.EndProc:
            {
                int i = FindFrame(FK.Exec);
                if (i < 0) throw new BasicError(16);
                var f = stack[i]; stack.RemoveRange(i, stack.Count - i); li = f.Li; si = f.Si;
                break;
            }
            case K.Dim:
                foreach (var lv in s.Lv)
                {
                    int n1 = I(lv.I1.Num(this)), n2 = lv.I2 == null ? 0 : I(lv.I2.Num(this));
                    var v = lv.V;
                    if (n1 < 0 || n2 < 0 || n1 > 32767) throw new BasicError(9);
                    if (v.IsStr) { v.Buf = new byte[Math.Max(1, n1)]; v.Len = 0; }
                    else { v.D1 = n1; v.D2 = n2; v.Arr = new double[(n1 + 1) * (n2 + 1)]; }
                }
                break;
            case K.Clr: Sym.ClearAll(); break;
            case K.Poke:
            {
                int a = Addr(s.A.Num(this)), v = I(s.B.Num(this));
                if (v < 0 || v > 255) throw new BasicError(7);
                M.Poke(a, v);
                break;
            }
            case K.Dpoke:
            {
                int a = Addr(s.A.Num(this)), v = I(s.B.Num(this));
                if (v < 0 || v > 65535) throw new BasicError(7);
                M.DPoke(a, v);
                break;
            }
            case K.Move:
            {
                int a = Addr(s.A.Num(this)), b = Addr(s.B.Num(this)), n = I(s.C.Num(this));
                if (n < 0 || n > 65535) throw new BasicError(7);
                var tmp = new byte[n];
                for (int i = 0; i < n; i++) tmp[i] = (byte)M.Peek(a + i);
                for (int i = 0; i < n; i++) M.Poke(b + i, tmp[i]);
                break;
            }
            case K.Restore:
            {
                if (s.A == null) { dataPos = 0; break; }
                int n = I(s.A.Num(this)), lo = 0, hi = data.Count;
                while (lo < hi) { int mid = (lo + hi) / 2; if (data[mid].line >= n) hi = mid; else lo = mid + 1; }
                dataPos = lo;
                break;
            }
            case K.Graphics:
                if (!G.SetMode(I(s.A.Num(this)))) throw new BasicError(141);
                break;
            case K.SetColor:
            {
                int r = I(s.A.Num(this)), h = I(s.B.Num(this)), l = I(s.C.Num(this));
                if (r < 0 || r > 4) throw new BasicError(3);
                M.Ram[Machine.COLOR0 + r] = (byte)((h & 15) * 16 + (l & 15));
                break;
            }
            case K.Color: G.DrawColor = I(s.A.Num(this)); break;
            case K.Plot:
            {
                int x = I(s.A.Num(this)), y = I(s.B.Num(this));
                if (!G.InRange(x, y)) throw new BasicError(141);
                G.PosX = x; G.PosY = y; G.Plot(x, y, G.DrawColor);
                break;
            }
            case K.Drawto: case K.Fillto:
            {
                int x = I(s.A.Num(this)), y = I(s.B.Num(this));
                if (!G.Graphical) throw new BasicError(141);
                G.Line(G.PosX, G.PosY, x, y, G.DrawColor);
                G.PosX = x; G.PosY = y;
                break;
            }
            case K.Position:
            {
                int x = I(s.A.Num(this)), y = I(s.B.Num(this));
                G.PosX = x; G.PosY = y;
                Scr.SetCursor(x, y);
                break;
            }
            case K.Locate:
                AssignNum(s.Target, G.Point(I(s.A.Num(this)), I(s.B.Num(this))));
                break;
            case K.Text:
            {
                int x = I(s.A.Num(this)), y = I(s.B.Num(this));
                G.DrawText(x, y, s.C.Str(this));
                break;
            }
            case K.Sound:
                if (s.L.Length == 0) { Audio.AllOff(Clock); break; }
                if (s.L.Length != 4) throw new BasicError(17);
                Audio.Sound(Clock, I(s.L[0].Num(this)), I(s.L[1].Num(this)), I(s.L[2].Num(this)), I(s.L[3].Num(this)));
                break;
            case K.Pause:
            {
                if (wakeAt < 0) wakeAt = Clock + s.A.Num(this) / 50.0;
                if (Clock < wakeAt) Retry(); else wakeAt = -1;
                break;
            }
            case K.Open: ExecOpen(s); break;
            case K.Close:
                if (s.A == null) { for (int i = 1; i < chans.Length; i++) CloseChan(i); }
                else CloseChan(I(s.A.Num(this)));
                break;
            case K.Bget:
            {
                var c = GetChan(s.A);
                int a = Addr(s.B.Num(this)), n = I(s.C.Num(this));
                if (c.Data == null || c.Write) throw new BasicError(131);
                int avail = Math.Min(n, c.Data.Length - c.Pos);
                for (int i = 0; i < avail; i++) M.Poke(a + i, c.Data[c.Pos + i]);
                c.Pos += avail;
                Clock += n * 0.00005;
                if (avail < n) throw new BasicError(136);
                break;
            }
            case K.Bput:
            {
                var c = GetChan(s.A);
                int a = Addr(s.B.Num(this)), n = I(s.C.Num(this));
                var b = new byte[n]; for (int i = 0; i < n; i++) b[i] = (byte)M.Peek(a + i);
                Out(c, b);
                break;
            }
            case K.Put:
            {
                var c = GetChan(s.A);
                Out(c, new[] { (byte)(I(s.B.Num(this)) & 255) });
                break;
            }
            case K.TimeSet: break;
            case K.Cls: Scr.Put(0x7D); break;
            case K.Deg: Deg = true; break;
            case K.Rad: Deg = false; break;
            default: throw new BasicError(17);
        }
    }

    void ExecPrint(Stmt s)
    {
        var ch = GetChan(s.A, false);
        bool screen = ch == null || ch.Screen;
        bool newline = true;
        foreach (var it in s.P)
        {
            if (it.E != null)
            {
                byte[] b = it.E.IsStr ? it.E.Str(this) : Ascii(Num.Format(it.E.Num(this)));
                Out(ch, b);
            }
            if (it.Sep == ';') newline = false;
            else if (it.Sep == ',')
            {
                newline = false;
                if (screen) { int n = 10 - Scr.TabColumn % 10; for (int i = 0; i < n; i++) Scr.Put(0x20); }
                else Out(ch, new byte[] { 0x20 });
            }
            else newline = true;
        }
        if (newline) Out(ch, new byte[] { 0x9B });
    }

    void ExecInput(Stmt s)
    {
        var ch = GetChan(s.A, false);
        if (ch != null && !ch.Screen && !ch.Kbd)
        {
            if (ch.Pos >= ch.Data.Length) throw new BasicError(136);
            int st = ch.Pos;
            while (ch.Pos < ch.Data.Length && ch.Data[ch.Pos] != 0x9B) ch.Pos++;
            var line = new byte[ch.Pos - st]; Array.Copy(ch.Data, st, line, 0, line.Length);
            ch.Pos = Math.Min(ch.Pos + 1, ch.Data.Length);
            AssignInput(s, line);
            return;
        }
        if (!inputActive)
        {
            inputActive = true; inBuf.Clear();
            if (s.B != null) Scr.Put(s.B.Str(this)); else Scr.Put("? ");
        }
        bool done = false;
        while (Keys.Count > 0 && !done)
        {
            byte k = Keys.Dequeue();
            if (k == 0x9B) { Scr.Put(0x9B); done = true; }
            else if (k == 0x7E) { if (inBuf.Count > 0) { inBuf.RemoveAt(inBuf.Count - 1); Scr.Put(0x7E); } }
            else if ((k >= 0x1B && k <= 0x1F) || k == 0x7D || k >= 0x9C && k != 0x9B) { /* editing codes ignored */ }
            else if (inBuf.Count < 119) { inBuf.Add(k); Scr.PutGlyphForEcho(k); }
        }
        if (!done) { Retry(); WaitKey = true; return; }
        inputActive = false;
        AssignInput(s, inBuf.ToArray());
    }

    void AssignInput(Stmt s, byte[] line)
    {
        if (s.Lv.Length == 1) { AssignLv(s.Lv[0], line); return; }
        int a = 0;
        for (int n = 0; n < s.Lv.Length; n++)
        {
            int e = a;
            while (e < line.Length && line[e] != (byte)',') e++;
            var part = new byte[e - a]; Array.Copy(line, a, part, 0, part.Length);
            AssignLv(s.Lv[n], part);
            a = Math.Min(e + 1, line.Length);
        }
    }

    void ExecOpen(Stmt s)
    {
        int n = I(s.A.Num(this)), mode = I(s.B.Num(this));
        string name = Encoding.ASCII.GetString(s.D.Str(this));
        if (n < 1 || n > 7) throw new BasicError(20);
        CloseChan(n);
        var c = new Chan { Name = name };
        string up = name.ToUpperInvariant();
        if (up.StartsWith("E:") || up.StartsWith("S:")) c.Screen = true;
        else if (up.StartsWith("K:")) c.Kbd = true;
        else if (up.StartsWith("P:") || up.StartsWith("R:")) c.Null = true;
        else
        {
            if ((mode & 8) != 0 && (mode & 4) == 0) { c.Write = true; c.Out = new MemoryStream(); if ((mode & 1) != 0 && Disks.TryGet(name, out var old)) c.Out.Write(old, 0, old.Length); }
            else
            {
                if (!Disks.TryGet(name, out var d)) throw new BasicError(170);
                c.Data = d;
                if ((mode & 8) != 0) { c.Write = true; c.Out = new MemoryStream(); c.Out.Write(d, 0, d.Length); }
            }
        }
        chans[n] = c;
    }

    void CloseChan(int n)
    {
        if (n < 1 || n > 7) return;
        var c = chans[n];
        if (c == null) return;
        if (c.Write && c.Out != null) Disks.Written[AtrImage.NormalizeName(c.Name)] = c.Out.ToArray();
        chans[n] = null;
    }
}
