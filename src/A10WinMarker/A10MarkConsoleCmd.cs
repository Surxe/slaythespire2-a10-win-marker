#if DEBUG
using A10WinMarker.Core;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace A10WinMarker;

/// <summary>
/// Dev-console command (spec 11), compiled only in Debug builds. The game's console discovers
/// it in loaded mods; debug commands are allowed whenever the game runs modded. It touches only
/// the local mark file, through the same core rule and store as the post-run hook.
/// </summary>
public sealed class A10MarkConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "a10mark";

    public override string Args => "list | set <scope> [on|off] | sim <mode> <players> <character> <ascension> <win>";

    public override string Description =>
        "A10 win marks for the active profile. scope: multiplayer, blitz or a character id (CHARACTER. optional).";

    public override bool IsNetworked => false;

    // DebugOnly keeps its default (true): the command is a dev tool.

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        try
        {
            if (!Compat.IsTargetBuild)
            {
                return Fail($"Mod inactive: built for {Compat.TargetVersion}, running on {Compat.Describe()}.");
            }
            if (Compat.Missing.Count > 0)
            {
                return Fail($"Mod inactive; missing: {string.Join(", ", Compat.Missing)}");
            }
            var command = ConsoleArgs.Parse(args, out var error);
            return command switch
            {
                ListCommand => List(),
                SetCommand set => Set(set),
                SimCommand sim => Sim(sim),
                _ => Fail(error ?? $"Usage: {ConsoleArgs.Usage}"),
            };
        }
        catch (Exception e)
        {
            Log.Error($"a10mark failed: {e}");
            return Fail($"Error: {e.Message}");
        }
    }

    public override CompletionResult GetArgumentCompletions(Player? player, string[] args)
    {
        var verb = args.Length > 1 ? args[0].ToLowerInvariant() : "";
        return (args.Length, verb) switch
        {
            (<= 1, _) => CompleteArgument(ConsoleArgs.Subcommands, Array.Empty<string>(), args.Length == 0 ? "" : args[0]),
            (2, "set") => CompleteArgument(ScopeNames(), args[..1], args[1]),
            (3, "set") => CompleteArgument(new[] { "on", "off" }, args[..2], args[2]),
            (2, "sim") => CompleteArgument(ConsoleArgs.Modes, args[..1], args[1]),
            (4, "sim") => CompleteArgument(CharacterNames(), args[..3], args[3]),
            (6, "sim") => CompleteArgument(new[] { "win", "loss" }, args[..5], args[5]),
            _ => base.GetArgumentCompletions(player, args),
        };
    }

    private static CmdResult List()
    {
        var snap = GameAdapter.Store.Read();
        var lines = new List<string> { $"Mark file: {snap.Path}" };
        switch (snap.Status)
        {
            case MarkFileStatus.Absent:
                lines.Add("No file yet: nothing marked.");
                break;
            case MarkFileStatus.Corrupt:
                lines.Add($"CORRUPT ({snap.Error}). Shown as nothing marked; the next change moves it aside.");
                break;
            default:
                lines.Add(snap.Marks.Count == 0 ? "No marks stored." : $"version {snap.Version?.ToString() ?? "?"}:");
                lines.AddRange(snap.Marks.Select(m => $"  {m.Key}: {(m.Value ? "on" : "off")}"));
                break;
        }
        lines.Add($"Recording hook: {(Patches.RecordingEnabled ? "on" : "off")}, marker: {(Patches.MarkerEnabled ? "on" : "off")}");
        return Ok(string.Join("\n", lines));
    }

    private static CmdResult Set(SetCommand set)
    {
        GameAdapter.Store.Set(set.Scope, set.Marked);
        MarkerUi.RefreshOpenScreens();
        return Ok($"{set.Scope.Key}: {(set.Marked ? "on" : "off")}");
    }

    private static CmdResult Sim(SimCommand sim)
    {
        var verdict = RunRule.Evaluate(sim.Run, GameAdapter.Caps);
        if (verdict.Scope is not { } scope)
        {
            return Ok($"Does not qualify: {verdict.Reason}. Nothing written.");
        }
        var outcome = GameAdapter.Store.Record(scope);
        MarkerUi.RefreshOpenScreens();
        return Ok(outcome == RecordOutcome.Added
            ? $"Qualifies: {verdict.Reason}. Recorded."
            : $"Qualifies: {verdict.Reason}. Already marked; nothing written.");
    }

    private static IEnumerable<string> ScopeNames() =>
        new[] { Scope.MultiplayerKey, Scope.BlitzKey }.Concat(CharacterNames());

    private static IEnumerable<string> CharacterNames()
    {
        try
        {
            return ModelDb.AllCharacters.Select(c => c.Id.Entry.ToLowerInvariant()).ToList();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    private static CmdResult Ok(string msg) => new(true, msg);

    private static CmdResult Fail(string msg) => new(false, msg);
}
#endif
