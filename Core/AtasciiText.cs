using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Eepch.Core;

/// <summary>
/// Conversion between raw ATASCII bytes and readable listing text.
///
/// The game ships its own charset (POLITEM.CHR) that puts the Polish letters on the
/// ATASCII control codes (the game tells the player: "Ctrl+A = ą"), so in listings
/// those codes are shown as real Unicode letters.  Everything else that is not
/// printable ASCII is written as \xNN, inverse-video text runs are wrapped in \I ... \i
/// and a literal backslash is written as \\.
/// </summary>
public static class AtasciiText
{
    static readonly char[] PolishByCode = new char[32];

    static AtasciiText()
    {
        PolishByCode[0x00] = 'ź';
        PolishByCode[0x01] = 'ą';
        PolishByCode[0x03] = 'ć';
        PolishByCode[0x05] = 'ę';
        PolishByCode[0x0C] = 'ł';
        PolishByCode[0x0E] = 'ń';
        PolishByCode[0x0F] = 'ó';
        PolishByCode[0x13] = 'ś';
        PolishByCode[0x1A] = 'ż';
    }

    public static bool IsPolish(byte b) { b &= 0x7F; return b < 32 && PolishByCode[b] != 0; }

    public static char PolishChar(byte b) { b &= 0x7F; return b < 32 ? PolishByCode[b] : '\0'; }

    /// <summary>Maps a typed Unicode character to an ATASCII byte (keyboard input).</summary>
    public static bool TryFromChar(char c, out byte b)
    {
        switch (c)
        {
            case 'ź': case 'Ź': b = 0x00; return true;
            case 'ą': case 'Ą': b = 0x01; return true;
            case 'ć': case 'Ć': b = 0x03; return true;
            case 'ę': case 'Ę': b = 0x05; return true;
            case 'ł': case 'Ł': b = 0x0C; return true;
            case 'ń': case 'Ń': b = 0x0E; return true;
            case 'ó': case 'Ó': b = 0x0F; return true;
            case 'ś': case 'Ś': b = 0x13; return true;
            case 'ż': case 'Ż': b = 0x1A; return true;
        }
        if (c >= 0x20 && c <= 0x7E) { b = (byte)c; return true; }
        b = 0; return false;
    }

    /// <summary>Raw bytes -> listing text.</summary>
    public static string Encode(ReadOnlySpan<byte> bytes, bool escapeQuote)
    {
        bool binary = false;
        foreach (byte b in bytes)
        {
            int v = b & 0x7F;
            if ((v < 0x20 && !IsPolish(b)) || v == 0x7F) { binary = true; break; }
        }
        var sb = new StringBuilder(bytes.Length + 8);
        if (binary)
        {
            foreach (byte b in bytes)
            {
                if (b >= 0x20 && b <= 0x7E && b != (byte)'\\' && !(escapeQuote && b == (byte)'"')) sb.Append((char)b);
                else sb.Append("\\x").Append(((int)b).ToString("X2"));
            }
            return sb.ToString();
        }
        bool inv = false;
        foreach (byte b in bytes)
        {
            bool hi = (b & 0x80) != 0;
            if (hi != inv) { sb.Append(hi ? "\\I" : "\\i"); inv = hi; }
            int v = b & 0x7F;
            if (v < 0x20) sb.Append(PolishByCode[v]);
            else if (v == '\\') sb.Append("\\\\");
            else if (escapeQuote && v == '"') sb.Append("\\x22");
            else sb.Append((char)v);
        }
        if (inv) sb.Append("\\i");
        return sb.ToString();
    }

    /// <summary>Listing text -> raw bytes.</summary>
    public static byte[] Decode(string s)
    {
        var o = new List<byte>(s.Length);
        bool inv = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                char n = s[i + 1];
                if (n == 'x' && i + 3 < s.Length
                    && int.TryParse(s.AsSpan(i + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hv))
                {
                    o.Add((byte)hv); i += 3; continue;
                }
                if (n == 'I') { inv = true; i++; continue; }
                if (n == 'i') { inv = false; i++; continue; }
                if (n == '\\') { o.Add((byte)('\\' | (inv ? 0x80 : 0))); i++; continue; }
            }
            if (!TryFromChar(c, out byte b)) b = (byte)'?';
            if (inv) b |= 0x80;
            o.Add(b);
        }
        return o.ToArray();
    }

    /// <summary>Display char for the host console mirror (control codes dropped).</summary>
    public static char ToConsoleChar(byte b)
    {
        char p = PolishChar(b);
        if (p != '\0') return p;
        int v = b & 0x7F;
        return v >= 0x20 && v < 0x7F ? (char)v : '\0';
    }
}
