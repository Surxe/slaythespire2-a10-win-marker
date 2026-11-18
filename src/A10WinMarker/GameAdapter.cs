using System.Runtime.CompilerServices;
using A10WinMarker.Core;
using Godot;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace A10WinMarker;

/// <summary>
/// The only layer that touches the game's saves, runs and lobbies (spec 8). Maps them to the
/// core's plain values and owns the one <see cref="MarkStore"/>. Never reads or writes
/// <c>progress.save</c>, history or epochs.
/// </summary>
internal static class GameAdapter
{
    /// <summary>The mark store for whatever profile is active at the moment of each call.</summary>
    public static readonly MarkStore Store = new(MarkFilePath, Log.Warn);

    public static Caps Caps => Compat.Caps;

    /// <summary>
    /// Absolute path of the active profile's mark file, resolved now (spec 7). The game's own
    /// function picks the profile and the vanilla/modded tree; it returns a <c>user://</c> path,
    /// globalized here for plain file IO.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string MarkFilePath() =>
        ProjectSettings.GlobalizePath(SaveManager.Instance.GetProfileScopedPath(MarkStore.FileName));

    /// <summary>Copies what the run rule needs from a finished run (spec 4).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static RunFacts FromRun(SerializableRun run, bool victory, bool success)
    {
        var players = run.Players?.Count ?? 0;
        var character = players == 1 ? run.Players![0].CharacterId?.ToString() : null;
        return new RunFacts(success, victory, run.IsCustomGame, MapMode(run.GameMode), run.Ascension, players, character);
    }

    /// <summary>Copies what the screen-scope rule needs from a screen's lobby (spec 5).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ScreenFacts FromLobby(StartRunLobby lobby)
    {
        var character = lobby.LocalPlayer.character; // default struct (null character) if not found
        return new ScreenFacts(
            MapMode(lobby.GameMode),
            MapRole(lobby.NetService.Type),
            character?.Id.ToString(),
            character is RandomCharacter);
    }

    // Mapped by name so the mod does not bake in enum values that a build may lack.
    private static RunMode MapMode(GameMode mode) => mode.ToString() switch
    {
        "Standard" => RunMode.Standard,
        "Daily" => RunMode.Daily,
        "Dread" => RunMode.Dread,
        "Blitz" => RunMode.Blitz,
        _ => RunMode.Unknown,
    };

    private static LobbyRole MapRole(NetGameType type) => type switch
    {
        NetGameType.Singleplayer => LobbyRole.Singleplayer,
        NetGameType.Host => LobbyRole.Host,
        NetGameType.Client => LobbyRole.Client,
        _ => LobbyRole.Other,
    };
}
