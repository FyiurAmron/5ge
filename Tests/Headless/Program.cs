using System;
using System.IO;
using System.Text;
using Eepch.Core;

static class P
{
    static Interp it;
    static double now;

    static string ScreenText()
    {
        var sb = new StringBuilder();
        int b = it.M.Savmsc;
        for (int r = 0; r < 24; r++)
        {
            var line = new StringBuilder();
            for (int c = 0; c < 40; c++)
            {
                int sc = it.M.Ram[b + r * 40 + c] & 0x7F;
                int at = sc < 0x40 ? sc + 0x20 : sc < 0x60 ? sc - 0x40 : sc;
                char ch = AtasciiText.PolishChar((byte)at);
                if (ch == '\0') ch = at >= 0x20 && at < 0x7F ? (char)at : ' ';
                line.Append(ch);
            }
            sb.AppendLine(line.ToString().TrimEnd());
        }
        return sb.ToString();
    }

    static void Run(double secs)
    {
        double end = now + secs;
        while (now < end) { now += 1 / 50.0; it.Pump(now); if (it.Halted && it.M.ColdStartRequested) break; }
    }

    static void Type(string s)
    {
        foreach (char c in s)
        {
            if (c == '\n') it.KeyIn(0x9B);
            else if (AtasciiText.TryFromChar(c, out var b)) it.KeyIn(b);
            Run(0.02);
        }
    }

    static void Shot(string name)
    {
        var r = new Renderer(it.M); r.Render();
        using var f = File.Create(name);
        var hdr = Encoding.ASCII.GetBytes($"P6\n{Renderer.W} {Renderer.H}\n255\n"); f.Write(hdr);
        for (int i = 0; i < Renderer.W * Renderer.H; i++) { f.WriteByte(r.Rgba[i * 4]); f.WriteByte(r.Rgba[i * 4 + 1]); f.WriteByte(r.Rgba[i * 4 + 2]); }
    }

    static void Main(string[] a)
    {
        it = new Interp();
        var ds = new DiskSet();
        foreach (var p in a[0].Split(',')) ds.Images.Add(AtrImage.Load(p, File.ReadAllBytes(p)));
        if (Environment.GetEnvironmentVariable("OVERLAY") is string od) foreach (var f in Directory.GetFiles(od)) ds.Overlay[AtrImage.NormalizeName(Path.GetFileName(f))] = File.ReadAllBytes(f);
        it.Disks = ds;
        it.Log = m => Console.WriteLine("[halt] " + m);
        if (Environment.GetEnvironmentVariable("DEMO") != null)
        {
            it.ColdBoot("D:" + Environment.GetEnvironmentVariable("DEMO"));
            for (int i = 0; i < 50 * 20; i++) { now += 0.02; it.Pump(now); }
            Shot("/tmp/shot_demo.ppm"); Console.WriteLine("halted=" + it.Halted + " " + it.HaltReason); return;
        }
        if (Environment.GetEnvironmentVariable("LISTDIR") is string ld)
        {
            Directory.CreateDirectory(ld);
            foreach (var n in ds.AllNames())
                if (ds.TryGet(n, out var d) && TbDetokenizer.LooksTokenized(d))
                    File.WriteAllText(Path.Combine(ld, n + ".lst"), TbDetokenizer.ToText(TbDetokenizer.Detokenize(d)), new UTF8Encoding(false));
            return;
        }
        it.ColdBoot();
        if (Environment.GetEnvironmentVariable("TEMPO") != null)
        {
            var ts = new System.Collections.Generic.List<double>();
            it.OnStep = (l, k) => { if (l == 70 && k == K.Let) ts.Add(it.Clock); };
            for (int i = 0; i < 50 * 20; i++) { now += 0.02; it.Pump(now); }
            Console.WriteLine("steps " + ts.Count + " first " + string.Join(",", ts.GetRange(0, Math.Min(6, ts.Count)).ConvertAll(x => x.ToString("F3"))));
            if (ts.Count > 64) Console.WriteLine("64 steps take " + (ts[64] - ts[0]).ToString("F2") + " s, step=" + ((ts[64] - ts[0]) / 64).ToString("F3"));
            return;
        }
        if (Environment.GetEnvironmentVariable("WAV") != null)
        {
            var samples = new System.Collections.Generic.List<short>();
            var buf = new float[4096];
            for (int i = 0; i < 50 * 12; i++) { now += 0.02; it.Pump(now); it.Audio.RenderTo(it.Clock); int n; while ((n = it.Audio.Read(buf, buf.Length)) > 0) for (int k = 0; k < n; k++) samples.Add((short)(Math.Clamp(buf[k], -1f, 1f) * 30000)); }
            using var w = new BinaryWriter(File.Create("/tmp/title.wav"));
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + samples.Count * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Pokey.Rate); w.Write(Pokey.Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(samples.Count * 2); foreach (var x in samples) w.Write(x);
            Console.WriteLine("wav samples " + samples.Count + " sim clock " + it.Clock);
            return;
        }
        Run(3);
        Console.WriteLine("--- title screen"); Console.Write(ScreenText()); Shot("/tmp/shot_title.ppm");
        it.M.ConsoleKeys = 1; Run(0.3); it.M.ConsoleKeys = 0;
        Run(2);
        Console.WriteLine("--- after START (prog " + it.ProgramName + ")"); Console.Write(ScreenText());
        it.KeyIn(0x9B); Run(3);
        Console.WriteLine("--- after RETURN"); Console.Write(ScreenText()); Shot("/tmp/shot_game0.ppm");
        for (int i = 2; i < a.Length; i++)
        {
            if (Environment.GetEnvironmentVariable("TRACE") == a[i]) it.OnStep = (l, k) => Console.Write(l + " ");
            Type(a[i] + "\n"); Run(1.5); it.OnStep = null;
            Console.WriteLine("--- after '" + a[i] + "'"); Console.Write(ScreenText());
        }
        Shot("/tmp/shot_game1.ppm");
        Console.WriteLine("halted=" + it.Halted + " " + it.HaltReason + " stmts=" + it.StatementsExecuted);
    }
}
