using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Eepch.Core;

public sealed class DiskFile
{
    public string Name = "";   // NAME.EXT (no trailing dot)
    public int Start;
    public int SectorCount;
    public int Flags;
    public override string ToString() => $"{Name} ({SectorCount} sectors @ {Start})";
}

/// <summary>An Atari .ATR disk image with a DOS 2.x file system reader (directory location is auto-detected).</summary>
public sealed class AtrImage
{
    readonly byte[] raw;
    readonly int secSize;
    readonly int secCount;
    public string Label { get; }
    public List<DiskFile> Files { get; } = new();

    AtrImage(string label, byte[] data)
    {
        Label = label;
        raw = data;
        if (data.Length < 16 || data[0] != 0x96 || data[1] != 0x02) throw new InvalidDataException("Not an ATR image: " + label);
        int paras = data[2] | data[3] << 8 | data[6] << 16;
        secSize = data[4] | data[5] << 8;
        if (secSize != 128 && secSize != 256 && secSize != 512) secSize = 128;
        int bytes = Math.Min(paras * 16, data.Length - 16);
        secCount = bytes <= 384 ? bytes / 128 : 3 + (bytes - 384) / secSize;
    }

    public static AtrImage Load(string label, byte[] data)
    {
        var img = new AtrImage(label, data);
        img.ParseDirectory();
        return img;
    }

    int SectorOffset(int n)
    {
        if (n < 1 || n > secCount) return -1;
        return n <= 3 ? 16 + (n - 1) * 128 : 16 + 384 + (n - 4) * secSize;
    }

    int SectorLen(int n) => n <= 3 ? 128 : secSize;

    public ReadOnlySpan<byte> Sector(int n)
    {
        int o = SectorOffset(n);
        if (o < 0 || o + SectorLen(n) > raw.Length) return ReadOnlySpan<byte>.Empty;
        return new ReadOnlySpan<byte>(raw, o, SectorLen(n));
    }

    static bool NameChar(byte c) => (c >= (byte)'A' && c <= (byte)'Z') || (c >= (byte)'0' && c <= (byte)'9') || c == (byte)'_' || c == (byte)' ';

    List<DiskFile> TryDirectory(int firstSector)
    {
        var list = new List<DiskFile>();
        for (int s = firstSector; s < firstSector + 8; s++)
        {
            var b = Sector(s);
            if (b.Length == 0) break;
            for (int i = 0; i < 8; i++)
            {
                var e = b.Slice(i * 16, 16);
                int fl = e[0];
                if (fl == 0 || (fl & 0x80) != 0) continue; // unused / deleted
                bool ok = e[5] >= (byte)'A' && e[5] <= (byte)'Z';
                for (int k = 5; k < 16 && ok; k++) ok = NameChar(e[k]);
                int cnt = e[1] | e[2] << 8, start = e[3] | e[4] << 8;
                if (!ok || start < 1 || start > secCount || cnt > secCount) continue;
                string nm = System.Text.Encoding.ASCII.GetString(e.Slice(5, 8)).Trim();
                string ex = System.Text.Encoding.ASCII.GetString(e.Slice(13, 3)).Trim();
                list.Add(new DiskFile { Name = ex.Length > 0 ? nm + "." + ex : nm, Start = start, SectorCount = cnt, Flags = fl });
            }
        }
        return list;
    }

    void ParseDirectory()
    {
        // Standard DOS 2 directory: 361..368. This game's disks keep it at 384..391.
        List<DiskFile> best = new();
        foreach (int s in new[] { 361, 384 })
        {
            var l = TryDirectory(s);
            if (l.Count > best.Count) best = l;
        }
        Files.AddRange(best);
    }

    public byte[] ReadFile(DiskFile f)
    {
        var ms = new MemoryStream();
        int n = f.Start, guard = 0;
        while (n != 0 && guard++ < secCount + 2)
        {
            var b = Sector(n);
            if (b.Length == 0) break;
            int sl = SectorLen(n);
            int len = b[sl - 1];
            int next = ((b[sl - 3] & 3) << 8) | b[sl - 2];
            if (len > sl - 3) break;
            ms.Write(b.Slice(0, len));
            n = next;
        }
        return ms.ToArray();
    }

    public static string NormalizeName(string n)
    {
        int c = n.IndexOf(':');
        if (c >= 0) n = n.Substring(c + 1);
        n = n.Trim().ToUpperInvariant();
        if (n.EndsWith(".")) n = n.Substring(0, n.Length - 1);
        return n;
    }

    public bool TryGet(string name, out byte[] data)
    {
        string nn = NormalizeName(name);
        foreach (var f in Files)
            if (f.Name == nn) { data = ReadFile(f); return true; }
        data = Array.Empty<byte>();
        return false;
    }
}

/// <summary>The drives seen by "D:" - all inserted sides are searched (first hit wins).
/// Host overlay files take precedence (drop an edited .lst listing in under the original name).</summary>
public sealed class DiskSet
{
    public List<AtrImage> Images { get; } = new();
    public Dictionary<string, byte[]> Overlay { get; } = new();
    public Dictionary<string, byte[]> Written { get; } = new();

    public bool TryGet(string name, out byte[] data)
    {
        string nn = AtrImage.NormalizeName(name);
        if (Written.TryGetValue(nn, out var d1)) { data = d1; return true; }
        if (Overlay.TryGetValue(nn, out var d2)) { data = d2; return true; }
        foreach (var im in Images)
            if (im.TryGet(nn, out var d3)) { data = d3; return true; }
        data = Array.Empty<byte>();
        return false;
    }

    public IEnumerable<string> AllNames() =>
        Images.SelectMany(i => i.Files.Select(f => f.Name)).Concat(Overlay.Keys).Distinct();
}
