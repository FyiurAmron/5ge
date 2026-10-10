using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Eepch.Core;

public enum K
{
    Rem, Data, Let, Print, Input, Get, Read, If, Else, Endif, For, Next, Goto, Gosub, OnGoto, OnGosub, Return, Pop, End, Stop,
    Trap, Run, Do, Loop, While, Wend, Repeat, Until, Exit, Proc, Exec, EndProc, Dim, Clr, Poke, Dpoke, Move, MoveBack, Restore,
    Graphics, SetColor, Color, Plot, Drawto, Position, Locate, Fillto, Text, Sound, Pause, Open, Close, Bget, Bput, Put,
    TimeSet, Cls, Deg, Rad, Bye, Nop, SyntaxError, Fcolor
}

public sealed class LValue
{
    public VarSlot V; public Expr I1, I2; public bool HasIndex;
}

public sealed class PrintItem
{
    public Expr E; public char Sep;   // Sep: '\0', ';' or ','
}

public sealed class Stmt
{
    public K Kind;
    public int Line;
    public Expr A, B, C, D;
    public Expr[] L;
    public LValue[] Lv;
    public PrintItem[] P;
    public byte[][] Data;
    public string Name = "";
    public bool Flag;
    public VarSlot V;
    public LValue Target;
    public string Error = "";
}

public sealed class ProgramLine
{
    public int Number;
    public Stmt[] Stmts = Array.Empty<Stmt>();
}

public sealed class ParseException : Exception { public ParseException(string m) : base(m) { } }

/// <summary>Parses one listing line (text form) into statements.</summary>
public sealed class LineParser
{
    enum TT { End, Num, Str, Id, Sym }
    sealed class Tok { public TT T; public double N; public byte[] S = Array.Empty<byte>(); public string Text = ""; }

    readonly string s;
    int pos;
    Tok peeked;
    readonly SymTab sym;
    readonly int lineNo;

    LineParser(string text, SymTab symtab, int line) { s = text; sym = symtab; lineNo = line; }

    public static Stmt[] ParseLine(string text, SymTab sym, int lineNo)
    {
        var p = new LineParser(text, sym, lineNo);
        var list = new List<Stmt>();
        try
        {
            p.ParseStatements(list);
        }
        catch (ParseException e)
        {
            list.Add(new Stmt { Kind = K.SyntaxError, Line = lineNo, Error = e.Message });
        }
        return list.ToArray();
    }

    // ---------- lexer ----------
    Tok Peek() => peeked ??= Lex();
    Tok Next() { var t = Peek(); peeked = null; return t; }

    Tok Lex()
    {
        while (pos < s.Length && s[pos] == ' ') pos++;
        if (pos >= s.Length) return new Tok { T = TT.End };
        char c = s[pos];
        if (char.IsDigit(c) || (c == '.' && pos + 1 < s.Length && char.IsDigit(s[pos + 1])))
        {
            int st = pos;
            while (pos < s.Length && char.IsDigit(s[pos])) pos++;
            if (pos < s.Length && s[pos] == '.') { pos++; while (pos < s.Length && char.IsDigit(s[pos])) pos++; }
            if (pos < s.Length && (s[pos] == 'E' || s[pos] == 'e'))
            {
                int j = pos + 1;
                if (j < s.Length && (s[j] == '+' || s[j] == '-')) j++;
                if (j < s.Length && char.IsDigit(s[j])) { while (j < s.Length && char.IsDigit(s[j])) j++; pos = j; }
            }
            return new Tok { T = TT.Num, N = double.Parse(s.AsSpan(st, pos - st), NumberStyles.Float, CultureInfo.InvariantCulture) };
        }
        if (c == '$' && pos + 1 < s.Length && Uri.IsHexDigit(s[pos + 1]))
        {
            int st = ++pos;
            while (pos < s.Length && Uri.IsHexDigit(s[pos])) pos++;
            return new Tok { T = TT.Num, N = int.Parse(s.AsSpan(st, pos - st), NumberStyles.HexNumber) };
        }
        if (c == '%' && pos + 1 < s.Length && char.IsDigit(s[pos + 1]))
        {
            pos++; int st = pos;
            while (pos < s.Length && char.IsDigit(s[pos])) pos++;
            return new Tok { T = TT.Num, N = double.Parse(s.AsSpan(st, pos - st), CultureInfo.InvariantCulture) };
        }
        if (c == '"')
        {
            int e = s.IndexOf('"', pos + 1);
            string body = e < 0 ? s.Substring(pos + 1) : s.Substring(pos + 1, e - pos - 1);
            pos = e < 0 ? s.Length : e + 1;
            return new Tok { T = TT.Str, S = AtasciiText.Decode(body) };
        }
        if (char.IsLetter(c) || c == '_')
        {
            int st = pos;
            while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] == '_')) pos++;
            if (pos < s.Length && s[pos] == '$') pos++;
            return new Tok { T = TT.Id, Text = s.Substring(st, pos - st) };
        }
        if (pos + 1 < s.Length)
        {
            string two = s.Substring(pos, 2);
            if (two == "<=" || two == ">=" || two == "<>") { pos += 2; return new Tok { T = TT.Sym, Text = two }; }
        }
        pos++;
        return new Tok { T = TT.Sym, Text = c.ToString() };
    }

    bool IsSym(string t) { var k = Peek(); return k.T == TT.Sym && k.Text == t; }
    bool IsId(string t) { var k = Peek(); return k.T == TT.Id && string.Equals(k.Text, t, StringComparison.OrdinalIgnoreCase); }
    bool AtEnd() => Peek().T == TT.End || IsSym(":");

    void Expect(string t)
    {
        if (!IsSym(t)) throw new ParseException("expected '" + t + "'");
        Next();
    }

    // ---------- statements ----------
    void ParseStatements(List<Stmt> list)
    {
        while (true)
        {
            while (IsSym(":")) Next();
            if (Peek().T == TT.End) return;
            ParseStatement(list);
            if (Peek().T == TT.End) return;
            if (!IsSym(":") && !(list.Count > 0 && list[^1].Kind == K.If && !list[^1].Flag)) { /* statements may follow THEN directly */ }
        }
    }

    Stmt Add(List<Stmt> list, K k)
    {
        var st = new Stmt { Kind = k, Line = lineNo };
        list.Add(st);
        return st;
    }

    string RawRest()
    {
        // text after the keyword, minus exactly one separating space
        string r = pos < s.Length ? s.Substring(pos) : "";
        if (r.StartsWith(" ")) r = r.Substring(1);
        pos = s.Length; peeked = null;
        return r;
    }

    void ParseStatement(List<Stmt> list)
    {
        var t = Peek();
        if (t.T == TT.Sym)
        {
            if (t.Text == "?") { Next(); ParsePrint(Add(list, K.Print)); return; }
            if (t.Text == "-" ) { Next(); if (IsSym("-")) { Next(); RawRest(); Add(list, K.Rem); return; } }
            throw new ParseException("unexpected '" + t.Text + "'");
        }
        if (t.T != TT.Id) throw new ParseException("statement expected");
        string u = t.Text.ToUpperInvariant();
        switch (u)
        {
            case "REM": Next(); RawRest(); Add(list, K.Rem); return;
            case "DATA":
            {
                Next(); string raw = RawRest();
                var st = Add(list, K.Data);
                var bytes = AtasciiText.Decode(raw);
                var items = new List<byte[]>();
                int a = 0;
                for (int i = 0; i <= bytes.Length; i++)
                    if (i == bytes.Length || bytes[i] == (byte)',') { var it = new byte[i - a]; Array.Copy(bytes, a, it, 0, it.Length); items.Add(it); a = i + 1; }
                st.Data = items.ToArray();
                return;
            }
            case "PRINT": Next(); ParsePrint(Add(list, K.Print)); return;
            case "LET": Next(); ParseLet(list); return;
            case "INPUT": Next(); ParseInput(Add(list, K.Input)); return;
            case "GET":
            {
                Next(); var st = Add(list, K.Get);
                if (IsSym("#")) { Next(); st.A = ParseExpr(); Expect(","); }
                st.Target = ParseLValue();
                return;
            }
            case "PUT":
            {
                Next(); var st = Add(list, K.Put);
                Expect("#"); st.A = ParseExpr(); Expect(","); st.B = ParseExpr();
                return;
            }
            case "READ":
            {
                Next(); var st = Add(list, K.Read);
                var l = new List<LValue> { ParseLValue() };
                while (IsSym(",")) { Next(); l.Add(ParseLValue()); }
                st.Lv = l.ToArray();
                return;
            }
            case "IF":
            {
                Next(); var st = Add(list, K.If);
                st.A = ParseExpr();
                if (IsId("THEN"))
                {
                    Next(); st.Flag = true;       // Flag = has THEN (single-line form)
                    if (Peek().T == TT.Num) { var g = Add(list, K.Goto); g.A = new ConstE(Next().N); }
                }
                return;
            }
            case "ELSE": Next(); Add(list, K.Else); return;
            case "ENDIF": Next(); Add(list, K.Endif); return;
            case "FOR":
            {
                Next(); var st = Add(list, K.For);
                var id = Next(); if (id.T != TT.Id) throw new ParseException("FOR var");
                st.V = sym.Get(id.Text);
                Expect("="); st.A = ParseExpr();
                if (!IsId("TO")) throw new ParseException("TO expected"); Next();
                st.B = ParseExpr();
                if (IsId("STEP")) { Next(); st.C = ParseExpr(); }
                return;
            }
            case "NEXT":
            {
                Next(); var st = Add(list, K.Next);
                if (Peek().T == TT.Id) st.V = sym.Get(Next().Text);
                return;
            }
            case "GOTO": case "GO": { Next(); if (u == "GO" && IsId("TO")) Next(); var st = Add(list, K.Goto); st.A = ParseExpr(); return; }
            case "GOSUB": { Next(); var st = Add(list, K.Gosub); st.A = ParseExpr(); return; }
            case "ON":
            {
                Next(); var sel = ParseExpr();
                bool sub;
                if (IsId("GOTO")) sub = false; else if (IsId("GOSUB")) sub = true; else throw new ParseException("ON ... GOTO/GOSUB");
                Next();
                var st = Add(list, sub ? K.OnGosub : K.OnGoto); st.A = sel;
                var l = new List<Expr> { ParseExpr() };
                while (IsSym(",")) { Next(); l.Add(ParseExpr()); }
                st.L = l.ToArray();
                return;
            }
            case "RETURN": Next(); Add(list, K.Return); return;
            case "POP": Next(); Add(list, K.Pop); return;
            case "END": Next(); Add(list, K.End); return;
            case "STOP": Next(); Add(list, K.Stop); return;
            case "BYE": case "DOS": Next(); Add(list, K.Bye); return;
            case "TRAP": { Next(); var st = Add(list, K.Trap); st.A = ParseExpr(); return; }
            case "RUN": { Next(); var st = Add(list, K.Run); if (!AtEnd()) st.A = ParseExpr(); return; }
            case "DO": Next(); Add(list, K.Do); return;
            case "LOOP": Next(); Add(list, K.Loop); return;
            case "WHILE": { Next(); var st = Add(list, K.While); st.A = ParseExpr(); return; }
            case "WEND": Next(); Add(list, K.Wend); return;
            case "REPEAT": Next(); Add(list, K.Repeat); return;
            case "UNTIL": { Next(); var st = Add(list, K.Until); st.A = ParseExpr(); return; }
            case "EXIT": Next(); Add(list, K.Exit); return;
            case "PROC": { Next(); var st = Add(list, K.Proc); st.Name = ReadName(); return; }
            case "EXEC": { Next(); var st = Add(list, K.Exec); st.Name = ReadName(); return; }
            case "ENDPROC": Next(); Add(list, K.EndProc); return;
            case "DIM":
            {
                Next(); var st = Add(list, K.Dim);
                var vs = new List<LValue>();
                do
                {
                    if (vs.Count > 0) Next();
                    var id = Next(); if (id.T != TT.Id) throw new ParseException("DIM name");
                    var lv = new LValue { V = sym.Get(id.Text), HasIndex = true };
                    Expect("("); lv.I1 = ParseExpr();
                    if (IsSym(",")) { Next(); lv.I2 = ParseExpr(); }
                    Expect(")");
                    vs.Add(lv);
                } while (IsSym(","));
                st.Lv = vs.ToArray();
                return;
            }
            case "CLR": Next(); Add(list, K.Clr); return;
            case "CLS": Next(); Add(list, K.Cls); return;
            case "DEG": Next(); Add(list, K.Deg); return;
            case "RAD": Next(); Add(list, K.Rad); return;
            case "POKE": { Next(); var st = Add(list, K.Poke); st.A = ParseExpr(); Expect(","); st.B = ParseExpr(); return; }
            case "DPOKE": { Next(); var st = Add(list, K.Dpoke); st.A = ParseExpr(); Expect(","); st.B = ParseExpr(); return; }
            case "MOVE": case "-MOVE": { Next(); var st = Add(list, K.Move); st.A = ParseExpr(); Expect(","); st.B = ParseExpr(); Expect(","); st.C = ParseExpr(); return; }
            case "RESTORE": { Next(); var st = Add(list, K.Restore); if (!AtEnd()) st.A = ParseExpr(); return; }
            case "GRAPHICS": { Next(); var st = Add(list, K.Graphics); st.A = ParseExpr(); return; }
            case "SETCOLOR": { Next(); var st = Add(list, K.SetColor); st.A = ParseExpr(); Expect(","); st.B = ParseExpr(); Expect(","); st.C = ParseExpr(); return; }
            case "COLOR": { Next(); var st = Add(list, K.Color); st.A = ParseExpr(); return; }
            case "FCOLOR": { Next(); var st = Add(list, K.Fcolor); st.A = ParseExpr(); return; }
            case "PLOT": { Next(); var st = Add(list, K.Plot); st.A = ParseExpr(); Expect(","); st.B = ParseExpr(); return; }
            case "DRAWTO": { Next(); var st = Add(list, K.Drawto); st.A = ParseExpr(); Expect(","); st.B = ParseExpr(); return; }
            case "FILLTO": { Next(); var st = Add(list, K.Fillto); st.A = ParseExpr(); Expect(","); st.B = ParseExpr(); return; }
            case "POSITION": { Next(); var st = Add(list, K.Position); st.A = ParseExpr(); Expect(","); st.B = ParseExpr(); return; }
            case "LOCATE": { Next(); var st = Add(list, K.Locate); st.A = ParseExpr(); Expect(","); st.B = ParseExpr(); Expect(","); st.Target = ParseLValue(); return; }
            case "TEXT": { Next(); var st = Add(list, K.Text); st.A = ParseExpr(); Expect(","); st.B = ParseExpr(); Expect(","); st.C = ParseExpr(); return; }
            case "SOUND":
            {
                Next(); var st = Add(list, K.Sound);
                if (AtEnd()) { st.L = Array.Empty<Expr>(); return; }
                var l = new List<Expr> { ParseExpr() };
                while (IsSym(",")) { Next(); l.Add(ParseExpr()); }
                st.L = l.ToArray();
                return;
            }
            case "PAUSE": { Next(); var st = Add(list, K.Pause); st.A = ParseExpr(); return; }
            case "OPEN":
            {
                Next(); var st = Add(list, K.Open);
                Expect("#"); st.A = ParseExpr(); Expect(","); st.B = ParseExpr(); Expect(","); st.C = ParseExpr(); Expect(","); st.D = ParseExpr();
                return;
            }
            case "CLOSE": { Next(); var st = Add(list, K.Close); if (IsSym("#")) { Next(); st.A = ParseExpr(); } return; }
            case "BGET": case "BPUT":
            {
                Next(); var st = Add(list, u == "BGET" ? K.Bget : K.Bput);
                Expect("#"); st.A = ParseExpr(); Expect(","); st.B = ParseExpr(); Expect(","); st.C = ParseExpr();
                return;
            }
            case "TIME$":
                if (true)
                {
                    Next(); Expect("="); var st = Add(list, K.TimeSet); st.A = ParseExpr(); return;
                }
            case "XIO": case "NOTE": case "POINT": case "STATUS": case "LIST": case "NEW": case "ENTER": case "LOAD": case "SAVE": case "CONT":
            case "DSOUND": case "TRACE": case "DUMP": case "DIR": case "COM":
                Next(); SkipToEnd(); Add(list, K.Nop); return;
        }
        ParseLet(list);
    }

    void SkipToEnd() { while (!AtEnd()) Next(); }

    string ReadName()
    {
        var t = Next();
        if (t.T != TT.Id) throw new ParseException("name expected");
        return t.Text.ToUpperInvariant();
    }

    LValue ParseLValue()
    {
        var id = Next();
        if (id.T != TT.Id) throw new ParseException("variable expected");
        var lv = new LValue { V = sym.Get(id.Text) };
        if (IsSym("("))
        {
            Next(); lv.HasIndex = true; lv.I1 = ParseExpr();
            if (IsSym(",")) { Next(); lv.I2 = ParseExpr(); }
            Expect(")");
        }
        return lv;
    }

    void ParseLet(List<Stmt> list)
    {
        var st = Add(list, K.Let);
        st.Target = ParseLValue();
        Expect("=");
        st.A = ParseExpr();
        if (st.Target.V.IsStr != st.A.IsStr) throw new ParseException("type mismatch");
    }

    void ParsePrint(Stmt st)
    {
        if (IsSym("#")) { Next(); st.A = ParseExpr(); if (IsSym(",") || IsSym(";")) Next(); }
        var items = new List<PrintItem>();
        while (!AtEnd())
        {
            var it = new PrintItem();
            if (!IsSym(";") && !IsSym(",")) it.E = ParseExpr();
            if (IsSym(";")) { Next(); it.Sep = ';'; }
            else if (IsSym(",")) { Next(); it.Sep = ','; }
            items.Add(it);
            if (it.E == null && it.Sep == '\0') break;
        }
        st.P = items.ToArray();
    }

    void ParseInput(Stmt st)
    {
        if (IsSym("#")) { Next(); st.A = ParseExpr(); Expect(","); }
        if (Peek().T == TT.Str)
        {
            st.B = new StrConstE(Next().S);
            if (IsSym(",") || IsSym(";")) Next();
        }
        var l = new List<LValue> { ParseLValue() };
        while (IsSym(",")) { Next(); l.Add(ParseLValue()); }
        st.Lv = l.ToArray();
    }

    // ---------- expressions ----------
    public Expr ParseExpr() => ParseOr();

    Expr ParseOr()
    {
        var l = ParseAnd();
        while (IsId("OR")) { Next(); l = new BinE(BinOp.Or, l, ParseAnd()); }
        return l;
    }

    Expr ParseAnd()
    {
        var l = ParseNot();
        while (IsId("AND")) { Next(); l = new BinE(BinOp.And, l, ParseNot()); }
        return l;
    }

    Expr ParseNot()
    {
        if (IsId("NOT")) { Next(); return new NotE(ParseNot()); }
        return ParseCmp();
    }

    Expr ParseCmp()
    {
        var l = ParseAdd();
        while (Peek().T == TT.Sym && (Peek().Text is "=" or "<>" or "<" or ">" or "<=" or ">="))
        {
            string op = Next().Text;
            var r = ParseAdd();
            if (l.IsStr != r.IsStr) throw new ParseException("type mismatch");
            l = new CmpE(op, l, r);
        }
        return l;
    }

    Expr ParseAdd()
    {
        var l = ParseMul();
        while (true)
        {
            if (IsSym("+")) { Next(); l = new BinE(BinOp.Add, l, ParseMul()); }
            else if (IsSym("-")) { Next(); l = new BinE(BinOp.Sub, l, ParseMul()); }
            else if (IsSym("!")) { Next(); l = new BinE(BinOp.BitOr, l, ParseMul()); }
            else if (IsId("EXOR")) { Next(); l = new BinE(BinOp.Exor, l, ParseMul()); }
            else return l;
        }
    }

    Expr ParseMul()
    {
        var l = ParsePow();
        while (true)
        {
            if (IsSym("*")) { Next(); l = new BinE(BinOp.Mul, l, ParsePow()); }
            else if (IsSym("/")) { Next(); l = new BinE(BinOp.Div, l, ParsePow()); }
            else if (IsSym("&")) { Next(); l = new BinE(BinOp.BitAnd, l, ParsePow()); }
            else if (IsId("DIV")) { Next(); l = new BinE(BinOp.IDiv, l, ParsePow()); }
            else if (IsId("MOD")) { Next(); l = new BinE(BinOp.Mod, l, ParsePow()); }
            else return l;
        }
    }

    Expr ParsePow()
    {
        var l = ParseUnary();
        while (IsSym("^")) { Next(); l = new BinE(BinOp.Pow, l, ParseUnary()); }
        return l;
    }

    Expr ParseUnary()
    {
        if (IsSym("-")) { Next(); var e = ParseUnary(); return e is ConstE c ? new ConstE(-c.Value) : new NegE(e); }
        if (IsSym("+")) { Next(); return ParseUnary(); }
        return ParsePrimary();
    }

    Expr ParsePrimary()
    {
        var t = Next();
        switch (t.T)
        {
            case TT.Num: return new ConstE(t.N);
            case TT.Str: return new StrConstE(t.S);
            case TT.Sym:
                if (t.Text == "(") { var e = ParseExpr(); Expect(")"); return e; }
                throw new ParseException("unexpected '" + t.Text + "'");
            case TT.Id:
            {
                string u = t.Text.ToUpperInvariant();
                if (FuncE.Table.TryGetValue(u, out var fi))
                {
                    var args = new List<Expr>();
                    if (IsSym("("))
                    {
                        Next();
                        if (!IsSym(")")) { args.Add(ParseExpr()); while (IsSym(",")) { Next(); args.Add(ParseExpr()); } }
                        Expect(")");
                    }
                    if (args.Count < fi.min || args.Count > fi.max) throw new ParseException("wrong argument count for " + u);
                    return new FuncE(fi.f, args.ToArray(), fi.str);
                }
                var slot = sym.Get(t.Text);
                if (IsSym("("))
                {
                    Next();
                    var a = ParseExpr(); Expr b = null;
                    if (IsSym(",")) { Next(); b = ParseExpr(); }
                    Expect(")");
                    return slot.IsStr ? new SliceE(slot, a, b) : new ArrE(slot, a, b);
                }
                return slot.IsStr ? new StrVarE(slot) : new NumVarE(slot);
            }
        }
        throw new ParseException("expression expected");
    }
}
