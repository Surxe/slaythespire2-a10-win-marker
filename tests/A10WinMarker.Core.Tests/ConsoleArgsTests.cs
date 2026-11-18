namespace A10WinMarker.Core.Tests;

public class ConsoleArgsTests
{
    private static ConsoleCommand? Parse(params string[] args) => ConsoleArgs.Parse(args, out _);

    private static string Error(params string[] args)
    {
        Assert.Null(ConsoleArgs.Parse(args, out var error));
        return Assert.IsType<string>(error);
    }

    // ---- scope names ---------------------------------------------------------------------

    [Theory]
    [InlineData("CHARACTER.IRONCLAD")]
    [InlineData("character.ironclad")]
    [InlineData("ironclad")]
    [InlineData("IronClad")]
    [InlineData("  ironclad ")]
    public void Character_prefix_is_optional_and_case_insensitive(string input) =>
        Assert.Equal(Scope.Character("CHARACTER.IRONCLAD"), ConsoleArgs.ParseScope(input));

    [Fact]
    public void Modded_character_ids_resolve() =>
        Assert.Equal(Scope.Character("CHARACTER.HELLOSPIRE-PALADIN"), ConsoleArgs.ParseScope("hellospire-paladin"));

    [Theory]
    [InlineData("multiplayer")]
    [InlineData("MULTIPLAYER")]
    public void Multiplayer_scope(string input) => Assert.Equal(Scope.Multiplayer, ConsoleArgs.ParseScope(input));

    [Theory]
    [InlineData("blitz")]
    [InlineData("Blitz")]
    public void Blitz_scope(string input) => Assert.Equal(Scope.Blitz, ConsoleArgs.ParseScope(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CARD.STRIKE")]
    [InlineData("character.")]
    [InlineData("a b")]
    [InlineData("CHARACTER.A.B")]
    public void Bad_scope_names(string input) => Assert.Null(ConsoleArgs.ParseScope(input));

    // ---- list / set ----------------------------------------------------------------------

    [Theory]
    [InlineData("list")]
    [InlineData("LIST")]
    public void List(string verb) => Assert.IsType<ListCommand>(Parse(verb));

    [Fact]
    public void Set_defaults_to_on() =>
        Assert.Equal(new SetCommand(Scope.Blitz, true), Parse("set", "blitz"));

    [Theory]
    [InlineData("on", true)]
    [InlineData("ON", true)]
    [InlineData("off", false)]
    [InlineData("Off", false)]
    public void Set_on_off(string value, bool marked) =>
        Assert.Equal(new SetCommand(Scope.Character("CHARACTER.SILENT"), marked), Parse("set", "silent", value));

    [Fact]
    public void Set_rejects_bad_on_off() => Assert.Contains("on or off", Error("set", "blitz", "maybe"));

    [Fact]
    public void Set_rejects_bad_scope() => Assert.Contains("Unknown scope", Error("set", "CARD.STRIKE"));

    [Theory]
    [InlineData]
    [InlineData("nope")]
    [InlineData("list", "extra")]
    [InlineData("set")]
    [InlineData("set", "blitz", "on", "extra")]
    [InlineData("sim", "standard", "1", "ironclad", "10")]
    public void Bad_shapes_show_usage(params string[] args) => Assert.Contains("Usage", Error(args));

    // ---- sim -----------------------------------------------------------------------------

    [Fact]
    public void Sim_builds_a_non_custom_successful_run()
    {
        var sim = Assert.IsType<SimCommand>(Parse("sim", "standard", "1", "ironclad", "10", "win"));
        Assert.Equal(
            new RunFacts(Success: true, Victory: true, IsCustom: false, RunMode.Standard, 10, 1, "CHARACTER.IRONCLAD"),
            sim.Run);
    }

    [Theory]
    [InlineData("standard", RunMode.Standard)]
    [InlineData("BLITZ", RunMode.Blitz)]
    [InlineData("daily", RunMode.Daily)]
    [InlineData("Dread", RunMode.Dread)]
    public void Sim_modes(string mode, RunMode expected) =>
        Assert.Equal(expected, Assert.IsType<SimCommand>(Parse("sim", mode, "2", "silent", "5", "win")).Run.Mode);

    [Theory]
    [InlineData("win", true)]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("loss", false)]
    [InlineData("lose", false)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    public void Sim_win_values(string value, bool victory) =>
        Assert.Equal(victory, Assert.IsType<SimCommand>(Parse("sim", "standard", "1", "ironclad", "10", value)).Run.Victory);

    [Theory]
    [InlineData("custom", "1", "ironclad", "10", "win", "Unknown mode")]
    [InlineData("standard", "0", "ironclad", "10", "win", "Players")]
    [InlineData("standard", "two", "ironclad", "10", "win", "Players")]
    [InlineData("standard", "1", "CARD.X", "10", "win", "character")]
    [InlineData("standard", "1", "ironclad", "-1", "win", "Ascension")]
    [InlineData("standard", "1", "ironclad", "ten", "win", "Ascension")]
    [InlineData("standard", "1", "ironclad", "10", "maybe", "win or loss")]
    public void Sim_rejects_bad_values(string mode, string players, string character, string asc, string win, string fragment) =>
        Assert.Contains(fragment, Error("sim", mode, players, character, asc, win));

    [Fact]
    public void Sim_goes_through_the_same_rule()
    {
        var caps = new Caps(10, 5);
        var mp = Assert.IsType<SimCommand>(Parse("sim", "standard", "3", "ironclad", "10", "win"));
        Assert.Equal(Scope.Multiplayer, RunRule.Evaluate(mp.Run, caps).Scope);
        var dread = Assert.IsType<SimCommand>(Parse("sim", "dread", "1", "ironclad", "10", "win"));
        Assert.Null(RunRule.Evaluate(dread.Run, caps).Scope);
    }
}
