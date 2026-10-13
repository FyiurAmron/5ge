*Dedicated to my children, today's and future; let The Sun always shine in your eyes.*

# Pięć gówien Eepcha – Turbo BASIC loader + interpreter for Godot 4.8 / C#

Runs the 1991 Macio Systems Atari 8-bit text adventure straight from the original `.atr`
disk images. It is **not an emulator**: no 6502 runs the game and there is no OS ROM. The
game's Turbo-BASIC XL program is loaded, converted to a text listing, parsed and interpreted.

Both supplied versions work (`eepch_A/B` and `eepch_A_alt/B_alt`; the alt Side A also contains `DEMO.BSX`).

## Run

1. Open the folder in Godot 4.8 (.NET build), let it build, press F5. 
   The csproj targets `net11.0` / `Godot.NET.Sdk/4.8.0` – adjust these two strings if your install differs.
2. A menu (drawn by the interpreter itself) lets you pick the version. `--version=N` after `--` on the command line skips it.
3. Exporting: the `.atr` files are not Godot resources – add `data/*` to the export include filter (see `export_presets.cfg.example`).

Keys: `F2/F3/F4` = START/SELECT/OPTION (title screen wants START) · `F5` reboot · `F9` dump text listings to `user://listings` ·
`F10` mute · `F11` turbo ×8 · `F12` version menu · `Alt+Enter` fullscreen.
Polish letters: type them directly, or use the game's own convention `Ctrl+A=ą  Ctrl+C=ć  Ctrl+E=ę  Ctrl+L=ł  Ctrl+N=ń  Ctrl+O=ó  Ctrl+S=ś  Ctrl+Z=ż`
(`ź` is ATASCII 0; type it directly).
Console: everything the game prints is mirrored to Godot's stdout; launching with `-- --stdin` also accepts typed lines from stdin.

## What is on the disks (reverse-engineered)

* ATR, single/enhanced density, 128-byte sectors. The DOS 2 directory has been moved to sectors 384–391 (auto-detected).
* Side A: `MAIN1.SYS` = the Turbo-BASIC XL runtime (binary load file; its token name tables were used to build `TbListing.cs`),
  `MAIN2.SYS` = title program, `MAIN3.SYS` = the game, `HEAD.PIC` = title bitmap, `POLITEM.CHR` = Polish charset.
  Side B: 3245-byte location pictures (3240 bytes of 160×81 4-colour data + 4 colour registers) and the `A`/`B` marker files.
* `MAIN2/MAIN3` are tokenized Atari/Turbo BASIC SAVE files. Readable versions are in `docs/listings/`.
* Data is stored raw; nothing was converted. `data/system.chr` is the game's own charset (raw copy of `POLITEM.CHR`) used as the fallback "ROM" font.

## Architecture (`Core/` has no Godot dependency)

| File | Role |
|---|---|
| `AtrImage.cs` | ATR + DOS 2 reader, `DiskSet` (all sides searched by `D:`; `user://overlay/` files shadow disk files, so you can run edited text listings) |
| `TbListing.cs` | Turbo-BASIC XL tokens → **text listing** (loader). Strings use `ź ą ć ę ł ń ó ś ż`, `\xNN`, `\I..\i` for inverse video |
| `Parser.cs`, `Expr.cs` | Text → statements/expressions. DATA/REM keep raw text |
| `Interp.cs` | Step-based interpreter: GOSUB/EXEC/FOR/DO/WHILE/REPEAT frames, IF-ENDIF blocks, TRAP, DATA, files `D:` `E:` `K:` `S:` |
| `Machine.cs` | 64 KB RAM + the OS variables/hardware registers that PEEK/POKE touch (colours, CHBAS, SDMCTL, CONSOL, RTCLOK …) |
| `Screen.cs` | `E:` screen editor (cursor in OS variables), `GRAPHICS`, display-list building, PLOT/DRAWTO/TEXT |
| `Renderer.cs` | Walks the display list in RAM → 384×240 RGBA (PAL overscan size, 320-px playfield). ANTIC text/graphics modes, per-line DLI colour changes |
| `Cpu6502.cs` | Tiny 6502, only for DLI routines and `USR` snippets the game pokes into memory |
| `Pokey.cs` | 4-voice tone/noise synth for `SOUND`, timestamped on the simulated clock |
| `Scripts/Main.cs` | Godot host: texture upload, `AudioStreamGenerator`, keyboard, menu |

Timing: every statement advances a simulated clock (`SOUND` ≈ 2.2 ms, others ≈ 0.15 ms), so envelope loops, `PAUSE` and the title
music run at a believable tempo (turbo with F11). `USR(DEC("E477"))` (game over) triggers a cold boot.

## Headless tests

`Tests/Headless` (plain .NET, no Godot): `dotnet run -- eepch_A.atr,eepch_B.atr "" "" "" "" "północ" "i"` boots, plays the typed lines and
prints the text screen; it also writes PPM screenshots to `/tmp`.
Env vars: `LISTDIR=dir` dump listings, `OVERLAY=dir` text-listing overlay, `WAV=1` render the title music, `DEMO=DEMO.BSX` run another program.

## Known simplifications

GTIA modes 9–11, player/missile graphics, paddles, `FILLTO`/`PAINT`/`CIRCLE`, real disk timing and disk swapping (both sides are always
mounted; the "insert side B" prompts still appear and accept RETURN). `USR` works only for plain 6502 routines.
