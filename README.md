# A10 Win Marker (Slay the Spire 2 mod)

The game shows `10` both before and after you beat Ascension 10, and keeps no record of the win.
This mod turns the ascension panel's fire icon into a fire trophy (flames rising from a trophy
cup, still showing the number) whenever the top ascension is selected on the character-select or
Blitz screen: A10, or A5 in Blitz. It is full colour once that scope is won at the top ascension,
greyed out until then. Other levels keep the vanilla fire.

- **Scopes:** each character in singleplayer Standard (modded characters included), one shared
  mark for multiplayer, one for Blitz (top ascension 5).
- **Recording:** automatic for new top-ascension wins, through a post-run hook.
- **Past wins:** select the top ascension and click the fire trophy to mark (or unmark) the
  current scope. There is no run-history scan.
- **Never shown** for Random, for players joining someone else's lobby, or on custom-run screens.
  Dread, Daily and custom runs are ignored.
- **Storage:** `a10_win_marker.json` in the active profile folder, local only. No game save is
  ever written.

Design: [docs/spec.md](docs/spec.md). Background: [docs/research.md](docs/research.md).
Target build: beta `v0.113.1` (`d09d48e5`); on any other build the mod logs a warning and does
nothing.

## Layout

| Path | What |
|---|---|
| `src/A10WinMarker.Core/` | Pure C#, no game types: scopes and caps, the run rule, screen scope, tooltip text, `MarkStore` (JSON file), console argument parsing. |
| `tests/A10WinMarker.Core.Tests/` | xUnit tests for the core. Synthetic fixtures only. |
| `src/A10WinMarker/` | The mod DLL: core sources compiled in, plus `Compat` (build gate, caps, private members), `GameAdapter` (runs, lobbies, profile path), `Patches` (Harmony), `MarkerUi` (the fire-trophy overlay), and `A10MarkConsoleCmd` (Debug only). |
| `scripts/` | `test.sh`, `build.sh`, `install.sh`. |

## Build and test

```bash
cp local.props.example local.props   # set Sts2ManagedDir to the folder with sts2.dll
scripts/test.sh                      # core tests; no game files needed
scripts/build.sh                     # Release build -> dist/A10WinMarker/
scripts/build.sh Debug               # Debug build, adds the a10mark console command
```

Install (run by the account that owns the game install, not `dev`):

```bash
scripts/install.sh "/path/to/Slay the Spire 2"   # -> <game>/mods/A10WinMarker/
```

## Dev console (Debug builds only)

Open the console with the backtick key (debug commands are allowed whenever the game runs modded).

```
a10mark list                              every stored mark for the active profile, and the file path
a10mark set <scope> [on|off]              set or clear a mark (default on)
a10mark sim <mode> <players> <character> <ascension> <win>
                                          run the recording rule on a fake run and store the
                                          result exactly as the post-run hook would
```

`<scope>` is `multiplayer`, `blitz` or a character id (`ironclad` or `CHARACTER.IRONCLAD`).
`<mode>` is `standard`, `blitz`, `daily` or `dread`; `<win>` is `win` or `loss`. Examples:

```
a10mark sim standard 1 ironclad 10 win    # marks character:CHARACTER.IRONCLAD
a10mark sim standard 3 silent 10 win      # marks multiplayer
a10mark sim blitz 1 defect 5 win          # marks blitz
a10mark sim dread 1 ironclad 10 win       # does not qualify: Dread runs are ignored
```

An open character-select or Blitz screen refreshes its fire trophy after each change.

## Hook points

| Patch (explicit parameter types) | Purpose |
|---|---|
| `ProgressSaveManager.UpdateWithRunData(SerializableRun, bool)` postfix | Record a qualifying win (add-only). |
| `NCharacterSelectScreen` / `NBlitzScreen` `.MaxAscensionChanged()` postfix | Refresh the marker (spec 5). |
| ... `.InitializeSingleplayer()`, `.InitializeMultiplayerAsHost(INetGameService, int)`, `.InitializeMultiplayerAsClient(INetGameService, ClientLobbyJoinResponseMessage)` postfixes | Correct marker when a screen opens. |
| ... `.SelectCharacter(NCharacterSelectButton)` postfix | In Blitz and multiplayer, `MaxAscensionChanged` does not run on a character switch, so this is what hides the marker for Random there. |
| `NAscensionPanel.SetAscensionLevel(int)` postfix | The arrows change the level with no lobby event; shows the fire trophy only at the top level. |

A patch that cannot be applied logs and disables only its feature (recording, or the marker).

## The fire trophy

At the top level, `MarkerUi` adds an overlay straight after the panel's active fire icon
(`%AscensionIconStandard` or `%AscensionIconBlitz`), so the game's own level label still draws the
number on top. The overlay holds two layers built from game textures: a smaller flame (the icon's
own texture and HSV shader, so the red singleplayer and blue host tints follow the game) and
`stats_trophy.png` below it. The vanilla fire is hidden through its `SelfModulate` while the
overlay shows. Layout fractions: `FlameAnchors` and `TrophyAnchors` in `MarkerUi.cs`.

## Still to verify in game

Adapter and UI are verified in game only (spec 10). Use a fork profile and the Debug build,
then walk spec section 13. First checks:

1. The fire trophy appears only at A10 (A5 in Blitz), with the number on the cup, in both the red
   (singleplayer) and blue (host) styles; other levels show the vanilla fire.
2. Clicking it toggles full colour / greyed, and the game's hover tip shows.
