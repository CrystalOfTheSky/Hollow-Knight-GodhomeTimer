# Godhome Boss Timer

A standalone Hollow Knight mod that times individual Hall of Gods boss
challenges. It is independent of the DPS Counter mod and can be installed or
unloaded by itself.

## Features

- Prepares an attempt when a Hall of Gods boss scene loads and starts the
  stopwatch at the game's post-transition gameplay boundary.
- Stops when the game completes the boss scene's exit sequence.
- Does not time Pantheon sequences as individual boss runs.
- Invalidates a run only when the Knight actually dies or uses a Dreamgate to
  leave. Non-lethal void, spike, thorn, and other hazard respawns keep timing.
  Dreamgate detection checks both the transition methods and the game's
  `dreamGate` entry-gate marker during scene changes.
- GodSeeker+ Fast Dream Warp is supported: it drives the Dream Nail FSM
  directly and reports `door_dreamReturn`, so leaving the active boss scene
  through that path also invalidates the run.
- Saves personal bests separately for each boss scene and difficulty:
  Attuned, Ascended, and Radiant.
- Shows the previous completed run, PB, and personal-worst time for each boss
  and difficulty.
- Colors the completed timer: gold for a new PB, green when faster than the
  previous run, pale red when slower than the previous run, yellow while
  paused, and deep red when invalid.
- Uses a LiveSplit-style load-removed start boundary: the attempt is prepared
  during scene loading, but the stopwatch starts only after the Boss transition,
  gameplay state, UI state, control, and hero entry are all ready.
- Pauses automatically while the game pause menu is open.
- Includes pause, restart, PB reset, position adjustment, and font sizing.
- Selectable built-in font types: Trajan Bold, Trajan Regular, Perpetua, and
  Noto Serif CJK.
- Optional text outline. It is off by default to avoid shadow/ghost artifacts
  on bright backgrounds.
- Optional P5 boss health for Ascended Hall of Gods runs. Boss HP uses the
  Pantheon/Attuned value while the normal Ascended damage multiplier remains.
- Optional Pantheon Vengefly form for Hall of Gods: uses the game's own
  Pantheon 1/5 first-boss arrival animation and movement lock.

## Install

1. Make sure Hollow Knight is launched with the Modding API installed.
2. Copy `GodhomeBossTimer.dll` into the game's `Mods` folder:

   ```text
   Hollow Knight/hollow_knight_Data/Managed/Mods/          (Windows/Linux)
   Hollow Knight/hollow_knight.app/Contents/Resources/Data/Managed/Mods/   (macOS)
   ```

3. Enter Hall of Gods and select a boss. The timer appears in Godhome.

The DPS Counter is not required.

## Controls

| Key (default) | Action |
| --- | --- |
| `F8` | Pause/resume the active run |
| `F9` | Restart the active run from zero, or clear a stopped result |
| `F10` | Enter/leave timer layout mode |

In layout mode:

- Arrow keys move the timer by 10 pixels.
- Hold Shift while using arrow keys to move by 1 pixel.
- PageUp/PageDown change the font size.
- Press the layout key again to save and return control to the Knight.

All keys can be changed in **Pause -> Mods -> Godhome Boss Timer**.

## Pause menu

- Mod on/off toggle.
- Show Timer.
- P5 Health (Ascended).
- Pantheon Vengefly Form.
- Pause, reset, and layout keys.
- Font type.
- Font size.
- Text outline.
- HUD corner.
- Reset the current boss/difficulty PB or all timer records.

## Settings

The mod creates `GodhomeBossTimer.GlobalSettings.json` next to the game saves.
Available settings:

- `TimerEnabled`
- `PantheonHealth` - use Pantheon/Attuned boss HP in Ascended runs.
- `PantheonVengeflyIntro` - use the native Pantheon first-boss arrival
  animation and movement lock in Hall of Gods Vengefly King.
- `TimerPauseKey` / `TimerResetKey` / `TimerLayoutKey`
- `TimerFontFamily` - `TrajanPro-Bold` (default), `TrajanPro-Regular`,
  `Perpetua`, or `NotoSerifCJKsc-Regular`.
- `TimerFontSize`
- `TimerOutline` - draw a dark outline around all timer rows. Default `false`.
- `TimerAnchorX` / `TimerAnchorY` / `TimerOffsetX` / `TimerOffsetY`
- `TimerPersonalBests` - milliseconds keyed by `GG_Boss_Scene|level`, where
  level 0/1/2 is Attuned/Ascended/Radiant.
- `TimerPreviousRuns` - most recent completed time per boss/difficulty.
- `TimerPersonalWorsts` - slowest completed time per boss/difficulty.

### GodSeeker+ compatibility

`PantheonHealth` only activates when the timer is enabled and the challenge is
Ascended. If GodSeeker+ is installed and its `P5Health` module is currently
enabled, this mod's P5 Health option is disabled automatically to prevent two
hooks from applying the same change. Disable GodSeeker+ P5 Health before using
this mod's option.

## Build

```bash
dotnet build GodhomeBossTimer/GodhomeBossTimer.csproj -c Release \
  -p:HollowKnightManaged="/absolute/path/to/hollow_knight_Data/Managed"
```

Output:

```text
GodhomeBossTimer/bin/Release/net472/GodhomeBossTimer.dll
```
