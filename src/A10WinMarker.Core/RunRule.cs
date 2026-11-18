namespace A10WinMarker.Core;

/// <summary>
/// Plain facts about a finished run, copied by the adapter from the game's run record and the
/// post-run result (or typed in by the dev console's <c>sim</c>).
/// </summary>
/// <param name="Success">The game's <c>PostRunProgressUpdateResult.success</c>.</param>
/// <param name="Victory">The run was won.</param>
/// <param name="IsCustom">The run was a custom run.</param>
/// <param name="Mode">The run's game mode.</param>
/// <param name="Ascension">The run's ascension level.</param>
/// <param name="PlayerCount">Number of players in the run.</param>
/// <param name="CharacterId">
/// The single player's character id (only used for singleplayer Standard). For a Random run this is
/// already the character it rolled, because the run record holds that.
/// </param>
public sealed record RunFacts(
    bool Success,
    bool Victory,
    bool IsCustom,
    RunMode Mode,
    int Ascension,
    int PlayerCount,
    string? CharacterId);

/// <summary>Outcome of <see cref="RunRule.Evaluate"/>: the scope to mark, or why nothing is marked.</summary>
public sealed record RunVerdict(Scope? Scope, string Reason)
{
    public bool Qualifies => Scope is not null;
}

/// <summary>
/// Decides whether a finished run earns a mark, and for which scope (spec 4). Recording is
/// add-only, so a run that does not qualify never removes anything; the caller just skips it.
/// </summary>
public static class RunRule
{
    public static RunVerdict Evaluate(RunFacts run, Caps caps)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(caps);

        if (!run.Success)
        {
            return No("the game did not update progress for this run");
        }
        if (!run.Victory)
        {
            return No("not a win");
        }
        if (run.IsCustom)
        {
            return No("custom run");
        }
        if (caps.For(run.Mode) is not { } top)
        {
            return No($"{run.Mode} runs are ignored");
        }
        if (run.Ascension != top)
        {
            return No($"ascension {run.Ascension} is not the {run.Mode} top ({top})");
        }
        if (run.PlayerCount < 1)
        {
            return No("run has no players");
        }

        if (run.Mode == RunMode.Blitz)
        {
            return Yes(Scope.Blitz, top);
        }
        if (run.PlayerCount > 1)
        {
            return Yes(Scope.Multiplayer, top);
        }
        if (string.IsNullOrWhiteSpace(run.CharacterId))
        {
            return No("singleplayer run has no character id");
        }
        return Yes(Scope.Character(run.CharacterId), top);
    }

    private static RunVerdict No(string reason) => new(null, reason);

    private static RunVerdict Yes(Scope scope, int top) => new(scope, $"won at A{top}; marks {scope.Key}");
}
