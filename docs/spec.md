# Spec: A10 Win Marker (STS2 mod)

Status: draft. Based on [research.md](research.md).

## 1. Goal

Show a clickable marker on the ascension panel for each scope where the player has won at the
top ascension. The game records no A10 win, so the mod records it.

- Recording is automatic for wins from now on (post-run hook).
- Past wins are marked by the player clicking the marker. There is **no run-history scan**.
- Marking is a toggle: click to mark, click again to unmark.

## 2. Non-goals

- Reading run history, or back-filling marks from it.
- Anything about Dread, Daily or custom runs. They are ignored completely.
- Changing any game save. `progress.save`, history and epochs are never written.
- Showing marks to other players, or syncing them across devices (storage is local).
- Relock interaction. The relock mod is independent; this mod never reads ascension values
  from the save (see research: "Decisions").

## 3. Scopes and top ascension

| Scope key | Meaning | Top ascension | Shown when |
|---|---|---|---|
| `character:<CHARACTER.ID>` | Singleplayer Standard, one character (vanilla or modded) | 10 | Singleplayer Standard, that character selected |
| `multiplayer` | Standard multiplayer, shared by every character | 10 | Host Standard, any character selected |
| `blitz` | Blitz, shared by every character | 5 | Singleplayer or host Blitz |

- Random (`CHARACTER.RANDOM_CHARACTER`) has no scope: the marker is hidden while it is selected.
- A win by Random is credited to the real character it rolled, because the run record holds
  that character.
- Top ascension is the mode's cap. It is taken from the game's constants in the adapter
  (`AscensionManager.maxAscensionAllowed` / `maxAscensionAllowedBlitz`), not typed in.
- Keys use the character's full id (`CHARACTER.IRONCLAD`), so modded characters work without
  any list.

## 4. Recording a win

Postfix on `ProgressSaveManager.UpdateWithRunData(SerializableRun, bool)`, which returns
`PostRunProgressUpdateResult`. The postfix uses only the run and the result; it does not read
the save.

A mark is recorded when **all** hold:

1. `__result.success` is true.
2. `victory` is true.
3. `run.IsCustomGame` is false.
4. `run.GameMode` is `Standard` or `Blitz`. (`Daily` and `Dread` are excluded.)
5. `run.Ascension` equals the top ascension for the mode (10, or 5 for Blitz).

Scope from the run:

- `Blitz` -> `blitz`, for any player count.
- `Standard` with one player -> `character:<that player's CharacterId>`.
- `Standard` with more than one player -> `multiplayer`.

Rules:

- Recording only ever **adds** a mark. A loss or a lower-ascension win never removes one.
- The hook must never throw into the game. Wrap all mod code in the postfix in try/catch and
  log on failure; a failed mark is a missed mark, not a broken save.
- The hook writes only the mod's own file (section 7).

## 5. Showing the marker

Postfix on `MaxAscensionChanged()` in `NCharacterSelectScreen` and `NBlitzScreen`, plus the
panel initialisation path those screens use (`Initialize`), so the marker is also correct when
a screen first opens.

Each time, the mod:

1. Resolves the current scope from the screen's lobby (`_lobby`): mode (`GameMode`),
   network type (`NetService.Type`: singleplayer, host or client), and
   `LocalPlayer.character`.
2. Picks the scope key (section 3), or none.
3. Shows the marker under `_ascensionPanel` (next to the level number) with the state read
   from the mark file, or hides it if there is no scope.

No marker is shown when:

- the selected character is Random;
- the local player is a **client** (joining someone else's lobby; the panel is hidden);
- the screen is a custom-run screen (those screens are not patched);
- the panel itself is hidden (max 0). The marker is its child, so it hides with it.

The marker's active and inactive state is read from storage **every time it is shown**, not
cached, so a profile switch is reflected immediately.

## 6. The marker control

- A `TextureButton` created in code, added as a child of `_ascensionPanel`. No Godot scene
  or `.pck` is needed; the mod is a DLL only.
- Texture: `res://images/packed/statistics_screen/stats_trophy.png`, loaded through
  `ResourceLoader`. The game ships this file; the mod ships no art.
- Active (marked): full colour (`Modulate` white).
- Inactive (not marked): greyed out (`Modulate` a dim grey). Colour change only.
- Always clickable, at **any** ascension level shown in the panel (the mark belongs to the
  scope, not the level), so past wins can be marked retroactively.
- Click: toggles the mark for the current scope, saves immediately, and updates the visual.
- Tooltip on hover:
  - marked: "A10 won" (or "A5 won" for Blitz);
  - not marked: "A10 not yet won. Click to mark" (or "A5 ...").
  - For the multiplayer scope, the text is prefixed so it is clear the mark is shared:
    "Multiplayer A10 won" / "Multiplayer A10 not yet won. Click to mark".
- Input: mouse click only. Controller focus navigation is out of scope for now.

## 7. Storage

- One JSON file per profile, through the game's own path function:
  `SaveManager.Instance.GetProfileScopedPath("a10_win_marker.json")`. It follows profile
  switches and the vanilla/modded save tree (including SavesMerger) automatically.
- Local only. The file is written with plain file IO, not through the cloud save store, and is
  not part of the set the game downloads at startup.
- Deleting a profile in-game removes the directory and with it the marks.

Format, version 1:

```json
{
  "version": 1,
  "marks": {
    "character:CHARACTER.IRONCLAD": true,
    "multiplayer": true,
    "blitz": false
  }
}
```

- An absent file means no marks.
- A missing or unknown `version` is read as best-effort; unknown keys are kept on write.
- A corrupt file is **not overwritten**. The mod logs it and treats all marks as unset for
  display; the next click writes a fresh file only after moving the corrupt one aside
  (`a10_win_marker.json.corrupt`).
- Writes go to a temporary file in the same folder, then replace the real file, so a crash
  cannot leave half a file.
- Each read and write resolves the path at that moment (no cached path).

## 8. Layering

```
Core (pure C#, tested)         Adapter (game)                       Front-end (the marker)
  ScopeKey, RunRule              Run -> scope + eligibility           TextureButton, tooltip,
  Caps per mode                  Lobby -> scope for the screen        click -> core toggle
  MarkStore (read, toggle,       Caps from game constants             Show/hide under panel
    add-only record, JSON I/O)   GetProfileScopedPath, file IO
                                 Harmony patches (explicit types)
```

- The core has no game or Godot types. Its run rule takes plain values (mode, custom flag,
  victory, ascension, player count, character id), so it is unit-tested with synthetic cases.
- The adapter is the only code that touches `SaveManager`, the lobby or the screens.
- The front-end only asks the core for state and asks it to toggle. It never writes the file.

## 9. Harmony patching

- Every patch names its target with **explicit parameter types** (for example
  `AccessTools.Method(typeof(NCharacterSelectScreen), "MaxAscensionChanged", Type.EmptyTypes)`).
  Ambiguous names are what broke SavesMerger on beta v0.113.1 (see the relock repo's research).
- A failed patch logs and disables the marker; it must not stop the mod or the game.
- Private members (`_lobby`, `_ascensionPanel`) are read through `AccessTools`/`Traverse`. If a
  member is missing, the marker is not shown; nothing else changes.
- The version check (section 12) runs first, so the patches are only applied on the target
  build.

## 10. Testing

- **Core unit tests:**
  - the run rule over synthetic cases: each eligibility condition, Blitz at any player count,
    Dread and Daily excluded, custom excluded, wins below the cap, losses, unknown modes;
  - scope keys for singleplayer characters (including a modded id), multiplayer and Blitz;
  - MarkStore: toggle, add-only record, round-trip JSON, corrupt-file handling, unknown keys
    kept;
  - console argument parsing (scope names, `CHARACTER.` prefix optional, on/off).
- **Adapter and UI:** verified in-game only.
- **Short term: dev console command** (section 11) to exercise storage, the run rule and the
  marker UI without winning a run.
- **End to end, later:** a real multiplayer A10 win, which exercises the post-run hook and the
  shared multiplayer scope in one match.
- **In-game acceptance:** section 13, on a fork profile.

Note: unlike the relock mod, this mod has no history replay, so it has no golden table to
check against. Fixtures are synthetic.

## 11. Dev console command (Debug builds only)

The game's dev console discovers every `AbstractConsoleCmd` subclass in loaded mod assemblies
(`DevConsole` constructor, via `ReflectionHelper.GetSubtypesInMods`). Commands default to
`DebugOnly`, and debug commands are allowed whenever the game is running modded
(`NDevConsole`: `ModManager.IsRunningModded()`). No patching is needed.

To keep it dev-only, the command class is compiled only in the Debug configuration
(`#if DEBUG`). Release builds do not contain it.

```
a10mark list                              show every stored mark for the active profile
a10mark set <scope> [on|off]              set or clear a mark (default on)
a10mark sim <mode> <players> <character> <ascension> <win>
                                          run the recording rule on a fake run, then store
                                          the result exactly as the post-run hook would
```

- `<scope>` is `multiplayer`, `blitz`, or a character id (`CHARACTER.IRONCLAD`; the
  `CHARACTER.` prefix may be omitted).
- `sim` goes through the same core rule and the same store as the post-run hook, so it tests
  everything except the Harmony hook itself. It prints whether the run qualified and why not.
- `IsNetworked` is false: the command only touches the local mark file.
- After a change, an open character-select or Blitz screen refreshes its marker.

## 11a. Decided

| Topic | Decision |
|---|---|
| Testing | Dev console command now; real multiplayer A10 win later for end to end |
| Controller input | Out of scope for now; mouse only |
| Relock mod | Fully independent, no shared code. The player can correct any mark by clicking it |
| Toggling | Allowed at any ascension level |
| Multiplayer tooltip | Prefixed "Multiplayer A10 ..." |

Still to tune in-game: the marker's exact position and size beside the level number, in both
the red (singleplayer) and blue (multiplayer) panel styles.

## 12. Compatibility

- Target: beta `v0.113.1` (commit `d09d48e5`). On other builds, the mod logs a warning and does
  nothing, rather than patching blind.
- Missing members (for example `_lobby` renamed) disable the marker and log, as in section 9.
- The icon path is confirmed in the beta pack; its in-game load is the first thing to verify
  (section 13).

## 13. Acceptance criteria

1. Loading the game, switching profiles and opening the menus change no save file.
2. The marker appears only on singleplayer Standard and Blitz character-select screens and on
   the host's multiplayer screen, and never for Random, for joiners, or on custom runs.
3. A recorded A10 win (singleplayer, multiplayer, Blitz at A5) marks the correct scope, and only
   that scope, after the run ends.
4. Dread, Daily and custom wins mark nothing.
5. Clicking toggles the mark; the greyed and full-colour states match the stored state after
   reopening the screen and after restarting the game.
6. Marks are per profile: switching profile shows that profile's marks.
7. A corrupt mark file is not overwritten and does not crash the mod or the game.
8. A failed hook never breaks a run's save.
9. The icon loads from `res://images/packed/statistics_screen/stats_trophy.png` in game.
10. With SavesMerger on or off, the marks are stored in the profile the game is using.
11. A Release build contains no `a10mark` console command.

## 14. Build and deploy (proposed)

- Solution with the core as a class library, its unit tests, and the mod project referencing
  the game DLLs (`sts2.dll`, `GodotSharp.dll`, `0Harmony.dll`) from a local copy. The DLLs are
  not checked in; the build reads them from a local props file.
- Output: `A10WinMarker.dll` and a manifest (`A10WinMarker.json`) in the game's `mods/` layout.
- Deploy by copying the built files into the game folder through a small install script, run
  by the player. `dev` never writes into the game install.
