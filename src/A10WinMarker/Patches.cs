using System.Reflection;
using A10WinMarker.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Save;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace A10WinMarker;

/// <summary>
/// Harmony patches (spec 9). Every target is named with explicit parameter types, and each one
/// is applied on its own: a failure logs and disables only the feature it belongs to (recording
/// or the marker), never the mod or the game.
/// </summary>
internal static class Patches
{
    private const string HarmonyId = "Surxe.A10WinMarker";

    /// <summary>The post-run hook is in place.</summary>
    public static bool RecordingEnabled { get; private set; }

    /// <summary>Every screen hook is in place and the screens' private members exist.</summary>
    public static bool MarkerEnabled { get; private set; }

    /// <summary>
    /// Screen methods after which the marker is refreshed. <c>MaxAscensionChanged</c> is the
    /// spec's hook; in singleplayer Standard it also runs on every character switch. In Blitz and
    /// multiplayer it runs only when the shared max changes, so <c>SelectCharacter</c> is patched
    /// too (Random must hide the marker there as well). The three <c>Initialize*</c> methods are
    /// the panel initialisation path the screens use when they open.
    /// </summary>
    private static readonly (string Name, Type[] Args)[] ScreenTargets =
    {
        ("MaxAscensionChanged", Type.EmptyTypes),
        ("SelectCharacter", new[] { typeof(NCharacterSelectButton) }),
        ("InitializeSingleplayer", Type.EmptyTypes),
        ("InitializeMultiplayerAsHost", new[] { typeof(INetGameService), typeof(int) }),
        ("InitializeMultiplayerAsClient", new[] { typeof(INetGameService), typeof(ClientLobbyJoinResponseMessage) }),
    };

    public static void Apply()
    {
        var harmony = new Harmony(HarmonyId);

        RecordingEnabled = TryPatch(
            harmony,
            typeof(ProgressSaveManager),
            "UpdateWithRunData",
            new[] { typeof(SerializableRun), typeof(bool) },
            nameof(AfterUpdateWithRunData));

        var markerOk = true;
        foreach (var screen in Compat.Screens)
        {
            if (Compat.LobbyField(screen) is null || Compat.PanelField(screen) is null)
            {
                Log.Error($"{screen.Name}: _lobby or _ascensionPanel not found; marker disabled.");
                markerOk = false;
                continue;
            }
            foreach (var (name, args) in ScreenTargets)
            {
                markerOk &= TryPatch(harmony, screen, name, args, nameof(AfterScreenChanged));
            }
        }
        // The arrows change the level without any lobby event: refresh on every level change.
        markerOk &= TryPatch(harmony, typeof(NAscensionPanel), "SetAscensionLevel", new[] { typeof(int) }, nameof(AfterPanelLevelChanged));
        MarkerEnabled = markerOk;
        if (!markerOk)
        {
            Log.Error("Marker disabled: at least one screen hook could not be applied.");
        }
    }

    private static bool TryPatch(Harmony harmony, Type type, string name, Type[] args, string postfix)
    {
        var label = $"{type.Name}.{name}({string.Join(", ", args.Select(a => a.Name))})";
        try
        {
            var target = AccessTools.Method(type, name, args);
            if (target is null || target.DeclaringType != type)
            {
                Log.Error($"Patch target not found: {label}");
                return false;
            }
            var method = typeof(Patches).GetMethod(postfix, BindingFlags.NonPublic | BindingFlags.Static)!;
            harmony.Patch(target, postfix: new HarmonyMethod(method));
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Could not patch {label}: {e}");
            return false;
        }
    }

    // ---- postfixes -----------------------------------------------------------------------
    // Positional parameters (__0, __1) so only the types, already pinned above, matter.

    /// <summary>
    /// Post-run hook (spec 4). Uses only the run and the result, writes only the mod's file, and
    /// never throws into the game: a failure here is a missed mark, not a broken save.
    /// </summary>
    private static void AfterUpdateWithRunData(SerializableRun __0, bool __1, PostRunProgressUpdateResult __result)
    {
        try
        {
            var facts = GameAdapter.FromRun(__0, __1, __result.success);
            var verdict = RunRule.Evaluate(facts, GameAdapter.Caps);
            if (verdict.Scope is not { } scope)
            {
                return;
            }
            var outcome = GameAdapter.Store.Record(scope);
            Log.Info(outcome == RecordOutcome.Added
                ? $"Recorded {scope.Key}: {verdict.Reason}"
                : $"{scope.Key} was already marked");
        }
        catch (Exception e)
        {
            Log.Error($"Recording the run failed (no mark written, game save untouched): {e}");
        }
    }

    private static void AfterPanelLevelChanged(object __instance)
    {
        try
        {
            MarkerUi.RefreshPanel(__instance);
        }
        catch (Exception e)
        {
            Log.Error($"Marker refresh failed: {e}");
        }
    }

    private static void AfterScreenChanged(object __instance)
    {
        try
        {
            MarkerUi.Refresh(__instance);
        }
        catch (Exception e)
        {
            Log.Error($"Marker refresh failed: {e}");
        }
    }
}
