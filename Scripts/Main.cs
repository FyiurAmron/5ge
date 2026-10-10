using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Eepch.Core;
using Godot;

namespace Eepch;

/// <summary>
/// Godot host for the Turbo BASIC interpreter: owns the Interp, turns its memory into a 384x240 texture every frame,
/// feeds keyboard input in, streams the POKEY synth out through an AudioStreamGenerator and mirrors the text console to stdout.
/// </summary>
public partial class Main : Control
{
	// ---- core ----
	Interp _it;
	Renderer _renderer;
	double _now;
	bool _turbo;
	double _rebootAt = -1;

	// ---- display ----
	ImageTexture _tex;
	Image _img;
	Label _hud;
	double _hudHideAt;

	// ---- audio ----
	AudioStreamPlayer _player;
	AudioStreamGeneratorPlayback _playback;
	readonly float[] _audioBuf = new float[8192];

	// ---- menu / versions ----
	sealed record GameVersion(string Name, List<string> Paths);
	readonly List<GameVersion> _versions = new();
	sealed record MenuItem(string Label, Action Run);
	readonly List<MenuItem> _menu = new();
	bool _inMenu = true;
	GameVersion _current;

	// ---- stdin console input (--stdin) ----
	readonly ConcurrentQueue<string> _stdinLines = new();
	bool _stdinEnabled;

	public override void _Ready()
	{
		BuildUi();
		BuildAudio();

		_it = new Interp();
		_renderer = new Renderer(_it.M);
		_it.ConsoleLine = line => GD.Print(line);          // console output: everything the game prints
		_it.Log = msg => GD.Print("[interpreter] " + msg);
		_it.Scr.OnBell = () => { };

		foreach (var a in OS.GetCmdlineUserArgs())
		{
			if (a == "--stdin") _stdinEnabled = true;
			if (a == "--turbo") _turbo = true;
		}
		if (_stdinEnabled) StartStdinThread();

		DiscoverVersions();
		var args = OS.GetCmdlineUserArgs();
		foreach (var a in args)
			if (a.StartsWith("--version=") && int.TryParse(a.Substring(10), out int vi) && vi >= 1 && vi <= _versions.Count)
			{ StartVersion(_versions[vi - 1]); return; }
		if (_versions.Count == 1) StartVersion(_versions[0]); else ShowMenu();
	}

	// ------------------------------------------------------------------ UI
	void BuildUi()
	{
		var bg = new ColorRect { Color = Colors.Black };
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(bg);

		var aspect = new AspectRatioContainer { Ratio = 4.0f / 3.0f, StretchMode = AspectRatioContainer.StretchModeEnum.Fit };
		aspect.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(aspect);

		_img = Image.CreateEmpty(Renderer.W, Renderer.H, false, Image.Format.Rgba8);
		_tex = ImageTexture.CreateFromImage(_img);
		var rect = new TextureRect
		{
			Texture = _tex,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
		};
		aspect.AddChild(rect);

		_hud = new Label { Text = HelpText(), Modulate = new Color(1, 1, 1, 0.8f) };
		_hud.AddThemeFontSizeOverride("font_size", 14);
		_hud.AddThemeColorOverride("font_shadow_color", Colors.Black);
		_hud.SetAnchorsPreset(LayoutPreset.BottomLeft);
		_hud.Position = new Vector2(8, -26);
		AddChild(_hud);
		_hudHideAt = 8;
	}

	static string HelpText() =>
		"F2 START  F3 SELECT  F4 OPTION | F5 reboot | F9 dump listings | F10 mute | F11 turbo | F12 menu | Alt+Enter fullscreen | Ctrl+letter = Polish letters (Ctrl+A=ą ...)";

	// ------------------------------------------------------------------ audio
	bool _muted;

	void BuildAudio()
	{
		var gen = new AudioStreamGenerator { MixRate = Pokey.Rate, BufferLength = 0.25f };
		_player = new AudioStreamPlayer { Stream = gen };
		AddChild(_player);
		_player.Play();
		_playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
	}

	void PumpAudio()
	{
		_it.Audio.RenderTo(_it.Clock);
		int free = _playback.GetFramesAvailable();
		while (free > 0 && _it.Audio.Available > 0)
		{
			int n = _it.Audio.Read(_audioBuf, Math.Min(free, _audioBuf.Length));
			if (n <= 0) break;
			var frames = new Vector2[n];
			float gain = _muted ? 0f : 0.7f;
			for (int i = 0; i < n; i++) { float s = _audioBuf[i] * gain; frames[i] = new Vector2(s, s); }
			_playback.PushBuffer(frames);
			free -= n;
		}
		// never let the queue grow beyond ~0.3 s (the sim clock and audio device clock drift apart)
		while (_it.Audio.Available > Pokey.Rate * 0.3) _it.Audio.Read(_audioBuf, _audioBuf.Length);
	}

	// ------------------------------------------------------------------ disks / versions
	static byte[] ReadAll(string path)
	{
		using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
		if (f == null) return null;
		return f.GetBuffer((long)f.GetLength());
	}

	static IEnumerable<string> ListAtr()
	{
		foreach (var dir in new[] { "res://data", "user://disks" })
		{
			if (!DirAccess.DirExistsAbsolute(dir)) continue;
			var names = DirAccess.GetFilesAt(dir);
			if (names == null) continue;
			foreach (var n in names)
				if (n.EndsWith(".atr", StringComparison.OrdinalIgnoreCase)) yield return dir + "/" + n;
		}
	}

	void DiscoverVersions()
	{
		_versions.Clear();
		var all = new List<string>(ListAtr());
		var used = new HashSet<string>();
		var rx = new Regex(@"^(?<dir>.*/)(?<pre>[^/]*?)_A(?<suf>[^/]*)\.atr$", RegexOptions.IgnoreCase);
		foreach (var p in all)
		{
			var m = rx.Match(p);
			if (!m.Success) continue;
			string partner = m.Groups["dir"].Value + m.Groups["pre"].Value + "_B" + m.Groups["suf"].Value + ".atr";
			string match = all.Find(x => string.Equals(x, partner, StringComparison.OrdinalIgnoreCase));
			var paths = new List<string> { p };
			if (match != null) { paths.Add(match); used.Add(match); }
			used.Add(p);
			_versions.Add(new GameVersion(m.Groups["pre"].Value + m.Groups["suf"].Value, paths));
		}
		foreach (var p in all) if (!used.Contains(p)) _versions.Add(new GameVersion(Path.GetFileNameWithoutExtension(p), new List<string> { p }));
	}

	DiskSet LoadDisks(GameVersion v)
	{
		var ds = new DiskSet();
		foreach (var p in v.Paths)
		{
			var bytes = ReadAll(p);
			if (bytes == null) { GD.PushError("cannot read " + p); continue; }
			try { ds.Images.Add(AtrImage.Load(Path.GetFileName(p), bytes)); }
			catch (Exception e) { GD.PushError(p + ": " + e.Message); }
		}
		// overlay: files in user://overlay/ shadow disk files (e.g. an edited text listing named MAIN3.SYS)
		var ov = DirAccess.DirExistsAbsolute("user://overlay") ? DirAccess.GetFilesAt("user://overlay") : null;
		if (ov != null)
			foreach (var n in ov) { var b = ReadAll("user://overlay/" + n); if (b != null) ds.Overlay[AtrImage.NormalizeName(n)] = b; }
		return ds;
	}

	// ------------------------------------------------------------------ menu / boot
	void ShowMenu()
	{
		_inMenu = true;
		_it.PowerOn();
		_menu.Clear();
		var s = _it.Scr;
		s.Put(0x7D);
		s.Put("  PIEC GOWIEN EEPCHA  (Macio Systems 1991)\x9b");
		s.Put("  Atari Turbo BASIC XL game, run by a Godot interpreter\x9b\x9b");
		for (int i = 0; i < _versions.Count && i < 9; i++)
		{
			var v = _versions[i];
			_menu.Add(new MenuItem(v.Name, () => StartVersion(v)));
			s.Put($"  {_menu.Count}) {v.Name}  [{string.Join(" + ", v.Paths.ConvertAll(Path.GetFileName))}]\x9b");
		}
		if (_versions.Count == 0) s.Put("  No .atr images found in res://data or user://disks\x9b");
		s.Put("\x9b  Press a number key to start.\x9b");
		s.Put("  (F2/F3/F4 = START/SELECT/OPTION in the game)\x9b");
	}

	void StartVersion(GameVersion v)
	{
		_current = v;
		_inMenu = false;
		_it.Disks = LoadDisks(v);
		_it.Speed = _turbo ? 8.0 : 1.0;
		_it.M.ConsoleKeys = 0;
		_it.ColdBoot();
		GD.Print($"[loader] version '{v.Name}', disks: {string.Join(", ", v.Paths)}");
	}

	void Reboot()
	{
		if (_current == null) return;
		_it.ColdBoot();
		_rebootAt = -1;
	}

	void DumpListings()
	{
		if (_it.Disks == null) return;
		DirAccess.MakeDirRecursiveAbsolute("user://listings");
		var utf8 = new UTF8Encoding(false);
		int n = 0;
		foreach (var name in _it.Disks.AllNames())
		{
			if (!_it.Disks.TryGet(name, out var data)) continue;
			if (!TbDetokenizer.LooksTokenized(data)) continue;
			try
			{
				string text = TbDetokenizer.ToText(TbDetokenizer.Detokenize(data));
				string path = "user://listings/" + name + ".lst";
				using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
				f.StoreBuffer(utf8.GetBytes(text));
				n++;
			}
			catch (Exception e) { GD.PushWarning(name + ": " + e.Message); }
		}
		string where = ProjectSettings.GlobalizePath("user://listings");
		GD.Print($"[loader] wrote {n} listing(s) to {where}");
		ShowHud($"Listings written to {where}");
	}

	void ShowHud(string text)
	{
		_hud.Text = text;
		_hudHideAt = _now + 5;
		_hud.Visible = true;
	}

	// ------------------------------------------------------------------ stdin console input
	void StartStdinThread()
	{
		var t = new System.Threading.Thread(() =>
		{
			try
			{
				string line;
				while ((line = Console.In.ReadLine()) != null) _stdinLines.Enqueue(line);
			}
			catch { }
		}) { IsBackground = true };
		t.Start();
	}

	// ------------------------------------------------------------------ frame loop
	public override void _Process(double delta)
	{
		_now += Math.Min(delta, 0.1);

		while (_stdinLines.TryDequeue(out var line))
		{
			foreach (char c in line) if (AtasciiText.TryFromChar(c, out byte b)) HandleKeyByte(b);
			HandleKeyByte(0x9B);
		}

		if (!_inMenu)
		{
			_it.Pump(_now);
			if (_it.M.ColdStartRequested && _rebootAt < 0) _rebootAt = _now + 0.7;   // USR($E477): "reset the computer"
			if (_rebootAt >= 0 && _now >= _rebootAt) Reboot();
		}
		PumpAudio();

		_renderer.Render();
		_img.SetData(Renderer.W, Renderer.H, false, Image.Format.Rgba8, _renderer.Rgba);
		_tex.Update(_img);

		if (_hud.Visible && _now > _hudHideAt) _hud.Visible = false;
	}

	// ------------------------------------------------------------------ keyboard
	void HandleKeyByte(byte b)
	{
		if (_inMenu)
		{
			int idx = b - '1';
			if (idx >= 0 && idx < _menu.Count) _menu[idx].Run();
			else if (b == 0x9B && _menu.Count > 0) _menu[0].Run();
			return;
		}
		_it.KeyIn(b);
	}

	public override void _Input(InputEvent e)
	{
		if (e is not InputEventKey k) return;

		// console keys follow press/release
		int bit = k.Keycode switch { Key.F2 => 1, Key.F3 => 2, Key.F4 => 4, _ => 0 };
		if (bit != 0)
		{
			if (k.Pressed) _it.M.ConsoleKeys |= bit; else _it.M.ConsoleKeys &= ~bit;
			GetViewport().SetInputAsHandled();
			return;
		}
		if (!k.Pressed || k.Echo && k.Keycode != Key.Backspace) return;

		switch (k.Keycode)
		{
			case Key.F1: ShowHud(HelpText()); GetViewport().SetInputAsHandled(); return;
			case Key.F5: if (!_inMenu) Reboot(); GetViewport().SetInputAsHandled(); return;
			case Key.F9: DumpListings(); GetViewport().SetInputAsHandled(); return;
			case Key.F10: _muted = !_muted; ShowHud(_muted ? "Sound off" : "Sound on"); GetViewport().SetInputAsHandled(); return;
			case Key.F11:
				_turbo = !_turbo; _it.Speed = _turbo ? 8.0 : 1.0;
				ShowHud(_turbo ? "Turbo x8 (music speeds up too)" : "Normal speed");
				GetViewport().SetInputAsHandled(); return;
			case Key.F12: ShowMenu(); GetViewport().SetInputAsHandled(); return;
			case Key.Enter or Key.KpEnter:
				if (k.AltPressed)
				{
					bool fs = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen;
					DisplayServer.WindowSetMode(fs ? DisplayServer.WindowMode.Windowed : DisplayServer.WindowMode.Fullscreen);
				}
				else HandleKeyByte(0x9B);
				GetViewport().SetInputAsHandled(); return;
			case Key.Backspace: HandleKeyByte(0x7E); GetViewport().SetInputAsHandled(); return;
			case Key.Tab: HandleKeyByte(0x7F); GetViewport().SetInputAsHandled(); return;
			case Key.Escape: HandleKeyByte(0x1B); GetViewport().SetInputAsHandled(); return;
		}

		// Ctrl+A .. Ctrl+Z -> ATASCII 1..26 (the game's charset shows Polish letters on these codes)
		if (k.CtrlPressed && k.Keycode >= Key.A && k.Keycode <= Key.Z)
		{
			HandleKeyByte((byte)(k.Keycode - Key.A + 1));
			GetViewport().SetInputAsHandled(); return;
		}

		if (k.Unicode > 0 && !k.CtrlPressed && !k.AltPressed || k.Unicode > 0x7F)
		{
			if (AtasciiText.TryFromChar((char)k.Unicode, out byte b)) { HandleKeyByte(b); GetViewport().SetInputAsHandled(); }
		}
	}
}
