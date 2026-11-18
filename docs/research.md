# Research: A10 win marker for Slay the Spire 2

Investigation done before any mod code was written. Goal of the mod: show a marker on the
ascension panel when you have **won at the top ascension** for the selected scope, because
the game shows `10` both before and after an A10 win and records nothing else about it.

All findings come from decompiling the game's managed assembly (`sts2.dll`) with
`ilspycmd`, from the game's resource pack, and from real save data on the Linux workstation.
Background on the save layout, mod loading and SavesMerger is in the sibling repo
`slaythespire2-relock-ascensions` (`docs/research.md`) and is not repeated here.

| Item | Value |
|---|---|
| Game build studied | `v0.113.1` (commit `d09d48e5`), Steam branch `public-beta` |
| Resource pack studied | `SlayTheSpire2.pck` from public `v0.107.1` (Godot 4.5.1, PCK format v3) |

## Contents

- [Decisions](#decisions)
- [The game keeps no record of an A10 win](#the-game-keeps-no-record-of-an-a10-win)
- [Background: how Dread unlocks](#background-how-dread-unlocks)
- [Hook: recording new wins](#hook-recording-new-wins)
- [Hook: showing the marker](#hook-showing-the-marker)
- [The marker control](#the-marker-control)
- [Icon](#icon)
- [Storage](#storage)
- [Reading the resource pack](#reading-the-resource-pack)
- [Open items](#open-items)

## Decisions

| Topic | Decision |
|---|---|
| Scopes | Singleplayer Standard per character; Multiplayer (shared); Blitz (shared) |
| Top ascension | A10 for Standard and Multiplayer; **A5** for Blitz (its cap) |
| Multiplayer | One shared mark, shown whichever character is selected, matching how multiplayer ascension is shared |
| Dread, Daily, custom runs | Ignored completely; no marker on the custom-run screen |
| Random character | Marker hidden while Random is selected; Random wins are credited to the rolled character |
| Joining another player's lobby | No marker (the ascension panel is hidden for joiners) |
| Modded characters | Included, keyed by character id |
| Recording | Automatic for new wins, via a post-run hook |
| Retroactive marks | The marker is always clickable and toggles the mark; no run-history scan |
| Look | Active = full colour; inactive = greyed out (colour change only); hover tooltip |
| Icon | `stats_trophy` (see [Icon](#icon)) |
| Storage | Local, per profile; not synced across devices |
| Relock mod | No interaction code; the marker never reads ascension values from the save |

## The game keeps no record of an A10 win

- Ascension advances only on a win at your current max (`ProgressSaveManager.Increment*Ascension`),
  and stops at the cap. A win at A10 (or Blitz A5) changes nothing in `progress.save`.
- No badge records it. The badge pool (`MegaCrit.Sts2.Core.Models.Badges`) holds per-run feats
  (Speedy, Perfect, BigDeck, ...).
- No achievement records it. `enum Achievement` has 22 entries (per-character first wins,
  enemy sets, character skills, misc). None mention ascension.
- The only trace is run history (`profileN/saves/history/*.run`: `win`, `ascension`,
  `game_mode`, `players[].character`). By decision, the mod does not scan it; past wins are
  marked by clicking.

## Background: how Dread unlocks

Researched first because Dread is the game's only "end of ascension" signal. It turned out not
to involve A10 wins.

`ProgressSaveManager.UpdateEpochsPostRun` (end of every run) obtains `DREAD_EPOCH` when:

1. every character in `ModelDb.AllCharacters` has a stats entry with `MaxAscension >= 10`.
   That list is fixed at six vanilla characters: Ironclad, Silent, Regent, Necrobinder, Defect,
   **Fool**. Modded characters and Random are not in it;
2. the run is not Daily and not custom.

Win/loss and singleplayer/multiplayer do not matter. The method's comment says
"singleplayer only", but it runs for both. The epoch must then be revealed in the Timeline;
the Dread button shows only when it is revealed (`NSingleplayerSubmenu`).

So Dread unlocks on **reaching** A10 with all six characters (beating A9 each), never on
beating A10. On the studied profile there is no `DREAD_EPOCH` entry yet, because Fool's max
is 0 (all Fool runs were multiplayer).

## Hook: recording new wins

Postfix on `ProgressSaveManager.UpdateWithRunData(SerializableRun serializableRun, bool victory)`,
which returns `PostRunProgressUpdateResult`.

- Runs once after every run (singleplayer and multiplayer, win or loss). It is the same method
  that advances ascension and epochs.
- Record a mark when all hold:
  - `__result.success`
  - `victory`
  - `serializableRun.GameMode` is `Standard` or `Blitz`
  - `!serializableRun.IsCustomGame`
  - `serializableRun.Ascension` equals the mode's cap (`AscensionManager.maxAscensionAllowed` /
    `maxAscensionAllowedBlitz`)
- Scope:
  - `Blitz` -> Blitz (any player count; matches the game's own Blitz branch);
  - `Standard`, `Players.Count == 1` -> that player's `CharacterId`;
  - `Standard`, more players -> Multiplayer.
- Read-only on the game side: the hook writes only the mod's own file.

## Hook: showing the marker

`NAscensionPanel` (`scenes/screens/ascension_panel.tscn`) draws the ascension number, arrows
and the fire icon. It is shared by `NCharacterSelectScreen` (singleplayer and multiplayer
host), `NBlitzScreen`, and the custom-run screens. The panel knows the game mode and the max,
but **not the selected character**.

The selection screens do know the character, through their lobby:

- `NCharacterSelectScreen.MaxAscensionChanged()` and `NBlitzScreen.MaxAscensionChanged()` are
  lobby-listener callbacks. They run whenever the lobby's max changes, which in singleplayer
  includes **every character switch**: `SelectCharacter` -> lobby
  (`SetSingleplayerAscensionAfterCharacterChanged`) -> `MaxAscensionChanged`.
- The screen's private `_lobby` (`StartRunLobby`) exposes `GameMode`, `NetService.Type`
  (singleplayer / host / client) and `LocalPlayer.character`. The screen's private
  `_ascensionPanel` is the panel.

Plan: postfix `MaxAscensionChanged` (and the screens' panel initialisation) on both screens.
Resolve the scope from `_lobby`, then create or update one marker control under
`_ascensionPanel`, next to `HBoxContainer/AscensionContainer/AscensionLevel`.

- Random selected (`RandomCharacter`) -> hide the marker.
- Client (joining) -> the panel is hidden anyway (`MultiplayerUiMode.Client`, max 0); no marker.
- Custom-run screens are not patched, so no marker there.
- The panel hides itself when the max is 0 (`Visible = _maxAscension > 0`), and the marker, as
  its child, hides with it.

## The marker control

- Unlock the Spire defines its button as a Godot script class, which needs Godot source
  generation and a `.pck`. Not needed here: a plain Godot `TextureButton` created in code,
  added as a child of the panel, with `Pressed` connected through `Callable.From(...)`.
  This keeps the mod DLL-only.
- Active vs inactive is `Modulate` (greyed out when unmarked).
- Tooltip on hover: e.g. "A10 won" / "A10 not yet won (click to mark)", with "A5" for Blitz.
- Clicking toggles the mark for the current scope and saves immediately.

## Icon

Use an existing game texture by its `res://` path, so the mod ships no art.

- **Chosen:** `res://images/packed/statistics_screen/stats_trophy.png` (128x128, gold trophy,
  from the statistics screen).
- Runners-up considered: `main_menu/submenu_trophy_icon.png` (trophy with sparkles, 280x151),
  `game_over_screen/score_ascension.png` (small fire, 48px), `game_over_screen/badge_gold.png`,
  `sprite_fonts/star_icon.png` (24px).
- The panel's own fire is `res://images/atlases/ui_atlas.sprites/top_bar/top_bar_ascension.tres`,
  an `AtlasTexture` region `(1227, 220, 88, 128)` of `ui_atlas_1.png`.
- The path was found in the public v0.107.1 pack and confirmed in the beta v0.113.1 pack
  (standalone texture, not an atlas region); see [Open items](#open-items).

## Storage

Local file, one per profile, not synced.

- Location: under the **active profile directory**, using the game's own path function:
  `SaveManager.Instance.GetProfileScopedPath("a10_win_marker.json")`. This follows profile
  switches and the vanilla/modded tree choice (including SavesMerger) automatically. It is
  written with plain local file IO, not through the game's cloud save store.
- Steam Cloud: the game downloads only a fixed list of files on startup (profile, progress,
  current runs, prefs, history), so this file stays local even if it is ever uploaded.
- Deleting a profile in-game deletes the whole profile directory recursively, so the marks go
  with it, which is the desired behaviour.
- Content: a small JSON object of marked scopes, for example
  `{ "version": 1, "characters": ["CHARACTER.IRONCLAD"], "multiplayer": true, "blitz": false }`.

## Reading the resource pack

`SlayTheSpire2.pck` is an unencrypted Godot 4 pack (format v3, Godot 4.5.1, relative file
base, directory at the end).

- Each `res://X.png` has an `X.png.import` remap pointing at `.godot/imported/*.ctex`.
- `.ctex` (`GST2`) holds either embedded PNG/WebP (`vram_texture: false`) or GPU-compressed
  data (`*.bptc.ctex`, BC7).
- Scratch tooling used for this research: `pck.py` (list/extract) and `ctex2png.py` (BC7 via
  `texture2ddecoder`, PNG/WebP via Pillow). At runtime none of this is needed: the game loads
  `res://` paths itself.

## Open items

- **Icon path: confirmed in beta v0.113.1.** The beta pack contains
  `images/packed/statistics_screen/stats_trophy.png.import`, remapped to a standalone,
  non-VRAM `.godot/imported/stats_trophy.png-2425c80a4bcc40efa35eabeacb484c4c.ctex`
  (7412 bytes), not an atlas region. Still to verify in-game:
  `ResourceLoader.Load<Texture2D>("res://images/packed/statistics_screen/stats_trophy.png")`.
- **Private members.** The hooks read `_lobby` and `_ascensionPanel` (private fields) via
  Harmony's `AccessTools`/`Traverse`. Fail safe (no marker, no crash) if they are renamed.
- **Marker placement and size** next to the level number, in both the red (singleplayer) and
  blue (multiplayer) panel styles.
- **Controller input.** The panel supports controller hotkeys; decide whether the marker needs
  to be reachable without a mouse.
