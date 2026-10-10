using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Eepch.Core;

public sealed class ListingLine
{
    public int Number;
    public string Text = "";
    public override string ToString() => Number + " " + Text;
}

/// <summary>
/// Turbo-BASIC XL token tables (taken from the runtime's own name tables in MAIN1.SYS) and the
/// loader that turns a tokenized SAVE file into human readable text lines.
/// </summary>
public static class TbDetokenizer
{
    // statement tokens 0x00..0x64
    public static readonly string[] Statements =
    {
        "REM","DATA","INPUT","COLOR","LIST","ENTER","LET","IF","FOR","NEXT","GOTO","GO TO","GOSUB","TRAP","BYE","CONT",
        "COM","CLOSE","CLR","DEG","DIM","END","NEW","OPEN","LOAD","SAVE","STATUS","NOTE","POINT","XIO","ON","POKE",
        "PRINT","RAD","READ","RESTORE","RETURN","RUN","STOP","POP","?","GET","PUT","GRAPHICS","PLOT","POSITION","DOS","DRAWTO",
        "SETCOLOR","LOCATE","SOUND","LPRINT","CSAVE","CLOAD","","ERROR-",
        "DPOKE","MOVE","-MOVE","*F","REPEAT","UNTIL","WHILE","WEND","ELSE","ENDIF","BPUT","BGET","FILLTO","DO","LOOP","EXIT",
        "DIR","LOCK","UNLOCK","RENAME","DELETE","PAUSE","TIME$=","PROC","EXEC","ENDPROC","FCOLOR","*L","--","RENUM","DEL","DUMP",
        "TRACE","TEXT","BLOAD","BRUN","GO#","#","*B","PAINT","CLS","DSOUND","CIRCLE","%PUT","%GET",
    };

    enum K { Stmt, WordOp, Op, Func, Var, Num, Str }

    // operator / function tokens 0x12..0x6D
    static readonly string[] OpNames = new string[0x6E];

    static TbDetokenizer()
    {
        void S(int t, string n) => OpNames[t] = n;
        S(0x12, ","); S(0x13, "$"); S(0x14, ":"); S(0x15, ";"); S(0x16, "<EOL>"); S(0x17, "GOTO"); S(0x18, "GOSUB");
        S(0x19, "TO"); S(0x1A, "STEP"); S(0x1B, "THEN"); S(0x1C, "#"); S(0x1D, "<="); S(0x1E, "<>"); S(0x1F, ">=");
        S(0x20, "<"); S(0x21, ">"); S(0x22, "="); S(0x23, "^"); S(0x24, "*"); S(0x25, "+"); S(0x26, "-"); S(0x27, "/");
        S(0x28, "NOT"); S(0x29, "OR"); S(0x2A, "AND"); S(0x2B, "("); S(0x2C, ")"); S(0x2D, "="); S(0x2E, "=");
        S(0x2F, "<="); S(0x30, "<>"); S(0x31, ">="); S(0x32, "<"); S(0x33, ">"); S(0x34, "="); S(0x35, "+"); S(0x36, "-");
        S(0x37, "("); S(0x38, "("); S(0x39, "("); S(0x3A, "("); S(0x3B, "("); S(0x3C, ",");
        string[] fn = { "STR$","CHR$","USR","ASC","VAL","LEN","ADR","ATN","COS","PEEK","SIN","RND","FRE","EXP","LOG","CLOG","SQR","SGN","ABS","INT",
            "PADDLE","STICK","PTRIG","STRIG","DPEEK","&","!","INSTR","INKEY$","EXOR","HEX$","DEC","DIV","FRAC","TIME$","TIME","MOD","EXEC","RND","RAND",
            "TRUNC","%0","%1","%2","%3","GO#","UINSTR","ERR","ERL" };
        for (int i = 0; i < fn.Length; i++) S(0x3D + i, fn[i]);
    }

    static double Bcd(byte[] f, int p)
    {
        int e = f[p];
        bool allZero = true;
        for (int i = 1; i < 6; i++) if (f[p + i] != 0) allZero = false;
        if (allZero) return 0;
        var sb = new StringBuilder();
        for (int i = 1; i < 6; i++) { sb.Append((char)('0' + (f[p + i] >> 4))); sb.Append((char)('0' + (f[p + i] & 15))); }
        int exp10 = 2 * ((e & 0x7F) - 0x40 - 4);
        double v = double.Parse(sb + "E" + exp10.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return (e & 0x80) != 0 ? -v : v;
    }

    public static string FormatNumber(double v)
    {
        if (v == Math.Floor(v) && Math.Abs(v) < 1e15) return ((long)v).ToString(CultureInfo.InvariantCulture);
        return v.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>True if the file looks like a tokenized Atari/Turbo BASIC SAVE file.</summary>
    public static bool LooksTokenized(byte[] f)
    {
        if (f.Length < 16 || f[0] != 0 || f[1] != 0) return false;
        int vntp = f[2] | f[3] << 8, vntd = f[4] | f[5] << 8;
        return vntd > vntp && vntd - vntp < f.Length;
    }

    public static List<ListingLine> Detokenize(byte[] f)
    {
        if (!LooksTokenized(f)) throw new FormatException("Not a tokenized BASIC file");
        int W(int i) => f[i * 2] | f[i * 2 + 1] << 8;
        int vntp = W(1), vntd = W(2), stmtab = W(4), stmcur = W(5);
        var names = new List<string>();
        var cur = new StringBuilder();
        for (int i = 14; i < 14 + (vntd - vntp) && i < f.Length; i++)
        {
            cur.Append((char)(f[i] & 0x7F));
            if ((f[i] & 0x80) != 0)
            {
                string n = cur.ToString(); cur.Clear();
                if (n.EndsWith("(")) n = n.Substring(0, n.Length - 1);
                names.Add(n);
            }
        }
        int pos = 14 + (stmtab - vntp);
        int end = Math.Min(14 + (stmcur - vntp), f.Length);
        var result = new List<ListingLine>();
        while (pos + 3 <= end)
        {
            int ln = f[pos] | f[pos + 1] << 8, ll = f[pos + 2];
            if (ln >= 0x8000 || ll < 4 || pos + ll > f.Length) break;
            var stmts = new List<string>();
            bool prevThen = false;
            var line = new StringBuilder();
            int j = pos + 3;
            while (j < pos + ll)
            {
                int stEnd = pos + f[j], tok = f[j + 1], k = j + 2;
                if (stEnd <= j || stEnd > pos + ll) break;
                string body;
                if (tok == 0 || tok == 1 || tok == 0x54)
                {
                    int e2 = stEnd;
                    if (e2 > k && f[e2 - 1] == 0x9B) e2--;
                    var raw = new byte[Math.Max(0, e2 - k)];
                    Array.Copy(f, k, raw, 0, raw.Length);
                    string kw = Statements[tok];
                    body = raw.Length == 0 ? kw : kw + " " + AtasciiText.Encode(raw, false);
                    prevThen = false;
                }
                else
                {
                    var toks = new List<(string t, K k)>();
                    string nm = tok < Statements.Length ? Statements[tok] : "?T" + tok.ToString("X2");
                    if (nm.Length > 0) toks.Add((nm, K.Stmt));
                    while (k < stEnd)
                    {
                        int t = f[k++];
                        if (t == 0x0E) { toks.Add((FormatNumber(Bcd(f, k)), K.Num)); k += 6; }
                        else if (t == 0x0F)
                        {
                            int n = f[k++];
                            var sb2 = new byte[n]; Array.Copy(f, k, sb2, 0, n); k += n;
                            toks.Add(("\"" + AtasciiText.Encode(sb2, true) + "\"", K.Str));
                        }
                        else if (t >= 0x80) toks.Add((t - 0x80 < names.Count ? names[t - 0x80] : "V" + (t - 0x80), K.Var));
                        else if (t == 0x14 || t == 0x16) { /* statement terminators */ }
                        else if (t < OpNames.Length && OpNames[t] != null)
                        {
                            string o = OpNames[t];
                            K kind = K.Op;
                            if (t is 0x17 or 0x18 or 0x19 or 0x1A or 0x1B or 0x28 or 0x29 or 0x2A or 0x5A or 0x5D or 0x61) kind = K.WordOp;
                            else if (t >= 0x3D && t != 0x56 && t != 0x57) kind = K.Func;
                            toks.Add((o, kind));
                        }
                        else toks.Add(("?O" + t.ToString("X2"), K.Op));
                    }
                    body = Render(toks);
                    prevThen = toks.Count > 0 && toks[^1].t == "THEN" && toks[^1].k == K.WordOp;
                }
                if (line.Length > 0) line.Append(stmts.Count > 0 && prevThenOfLast(stmts) ? " " : ":");
                line.Append(body);
                stmts.Add(body);
                lastThen = prevThen;
                j = stEnd;
            }
            result.Add(new ListingLine { Number = ln, Text = line.ToString() });
            pos += ll;
        }
        return result;
    }

    static bool lastThen;
    static bool prevThenOfLast(List<string> _) => lastThen;

    static string Render(List<(string t, K k)> toks)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < toks.Count; i++)
        {
            var (t, k) = toks[i];
            if (i > 0)
            {
                var (pt, pk) = toks[i - 1];
                bool word(K x) => x is K.Var or K.Num or K.Func or K.Stmt or K.WordOp;
                bool sp = pk == K.Stmt || pk == K.WordOp || k == K.WordOp
                          || (word(pk) && word(k)) || (pk == K.Str && word(k)) || (word(pk) && k == K.Str && pk != K.Func)
                          || (pt == "-" && t.StartsWith("-")) || (pt == "+" && t.StartsWith("+"))
                          || (pk == K.Num && t == "$");
                if (pk == K.Func && k == K.Str) sp = false;
                if (sp) sb.Append(' ');
            }
            sb.Append(t);
        }
        return sb.ToString();
    }

    public static string ToText(List<ListingLine> lines)
    {
        var sb = new StringBuilder();
        foreach (var l in lines) sb.Append(l.Number).Append(' ').Append(l.Text).Append('\n');
        return sb.ToString();
    }

    /// <summary>Parses a listing in text form ("10 PRINT ..." per line).</summary>
    public static List<ListingLine> ParseText(string text)
    {
        var res = new List<ListingLine>();
        foreach (var raw in text.Split('\n'))
        {
            string l = raw.TrimEnd('\r');
            int i = 0;
            while (i < l.Length && char.IsDigit(l[i])) i++;
            if (i == 0) continue;
            res.Add(new ListingLine { Number = int.Parse(l.AsSpan(0, i), NumberStyles.None, CultureInfo.InvariantCulture), Text = i < l.Length ? l.Substring(i + 1 > l.Length ? l.Length : i + 1) : "" });
        }
        res.Sort((a, b) => a.Number.CompareTo(b.Number));
        return res;
    }

    /// <summary>Loads either a tokenized file or a text listing.</summary>
    public static List<ListingLine> LoadAny(byte[] data)
    {
        if (LooksTokenized(data)) return Detokenize(data);
        return ParseText(Encoding.UTF8.GetString(data).TrimStart('\uFEFF'));
    }
}
