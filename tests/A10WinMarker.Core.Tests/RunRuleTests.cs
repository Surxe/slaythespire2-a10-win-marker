namespace A10WinMarker.Core.Tests;

public class RunRuleTests
{
    private const string Ironclad = "CHARACTER.IRONCLAD";
    private const string Modded = "CHARACTER.HELLOSPIRE-PALADIN";
    private static readonly Caps Caps = new(Standard: 10, Blitz: 5);

    private static RunFacts Win(RunMode mode = RunMode.Standard, int ascension = 10, int players = 1, string? character = Ironclad) =>
        new(Success: true, Victory: true, IsCustom: false, mode, ascension, players, character);

    private static Scope? ScopeOf(RunFacts run) => RunRule.Evaluate(run, Caps).Scope;

    [Fact]
    public void Singleplayer_standard_top_win_marks_that_character() =>
        Assert.Equal(Scope.Character(Ironclad), ScopeOf(Win()));

    [Fact]
    public void Modded_character_is_keyed_by_its_id() =>
        Assert.Equal("character:" + Modded, ScopeOf(Win(character: Modded))?.Key);

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void Multiplayer_standard_top_win_marks_shared_multiplayer(int players) =>
        Assert.Equal(Scope.Multiplayer, ScopeOf(Win(players: players)));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void Blitz_top_win_marks_blitz_at_any_player_count(int players) =>
        Assert.Equal(Scope.Blitz, ScopeOf(Win(RunMode.Blitz, ascension: 5, players: players)));

    [Fact]
    public void Blitz_top_is_its_own_cap_not_ten()
    {
        Assert.Null(ScopeOf(Win(RunMode.Blitz, ascension: 10)));
        Assert.Null(ScopeOf(Win(RunMode.Blitz, ascension: 4)));
    }

    [Fact]
    public void Requires_success() =>
        Assert.False(RunRule.Evaluate(Win() with { Success = false }, Caps).Qualifies);

    [Fact]
    public void Requires_victory() =>
        Assert.False(RunRule.Evaluate(Win() with { Victory = false }, Caps).Qualifies);

    [Fact]
    public void Loss_at_top_marks_nothing_in_any_mode()
    {
        Assert.Null(ScopeOf(Win() with { Victory = false }));
        Assert.Null(ScopeOf(Win(players: 3) with { Victory = false }));
        Assert.Null(ScopeOf(Win(RunMode.Blitz, ascension: 5) with { Victory = false }));
    }

    [Theory]
    [InlineData(RunMode.Standard, 10)]
    [InlineData(RunMode.Blitz, 5)]
    public void Custom_runs_are_excluded(RunMode mode, int ascension) =>
        Assert.Null(ScopeOf(Win(mode, ascension) with { IsCustom = true }));

    [Theory]
    [InlineData(RunMode.Daily)]
    [InlineData(RunMode.Dread)]
    [InlineData(RunMode.Unknown)]
    public void Daily_dread_and_unknown_modes_are_excluded(RunMode mode)
    {
        Assert.Null(ScopeOf(Win(mode)));
        Assert.Null(ScopeOf(Win(mode, players: 2)));
        Assert.Contains("ignored", RunRule.Evaluate(Win(mode), Caps).Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void Wins_below_the_cap_mark_nothing(int ascension)
    {
        var verdict = RunRule.Evaluate(Win(ascension: ascension), Caps);
        Assert.Null(verdict.Scope);
        Assert.Contains($"ascension {ascension}", verdict.Reason);
    }

    [Fact]
    public void Above_the_cap_marks_nothing() => Assert.Null(ScopeOf(Win(ascension: 11)));

    [Fact]
    public void Cap_comes_from_caps_not_a_literal()
    {
        var caps = new Caps(Standard: 12, Blitz: 6);
        Assert.Null(RunRule.Evaluate(Win(ascension: 10), caps).Scope);
        Assert.Equal(Scope.Character(Ironclad), RunRule.Evaluate(Win(ascension: 12), caps).Scope);
        Assert.Equal(Scope.Blitz, RunRule.Evaluate(Win(RunMode.Blitz, ascension: 6), caps).Scope);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Singleplayer_without_character_marks_nothing(string? character) =>
        Assert.Null(ScopeOf(Win(character: character)));

    [Fact]
    public void Multiplayer_ignores_the_character() =>
        Assert.Equal(Scope.Multiplayer, ScopeOf(Win(players: 2, character: null)));

    [Fact]
    public void No_players_marks_nothing() => Assert.Null(ScopeOf(Win(players: 0)));

    [Fact]
    public void Each_failed_condition_has_a_reason()
    {
        Assert.Contains("did not update", RunRule.Evaluate(Win() with { Success = false }, Caps).Reason);
        Assert.Contains("not a win", RunRule.Evaluate(Win() with { Victory = false }, Caps).Reason);
        Assert.Contains("custom", RunRule.Evaluate(Win() with { IsCustom = true }, Caps).Reason);
    }

    [Fact]
    public void Qualifying_reason_names_the_scope() =>
        Assert.Contains("character:CHARACTER.IRONCLAD", RunRule.Evaluate(Win(), Caps).Reason);
}
