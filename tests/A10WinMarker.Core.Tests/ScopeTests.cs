namespace A10WinMarker.Core.Tests;

public class ScopeTests
{
    private static readonly Caps Caps = new(Standard: 10, Blitz: 5);

    [Theory]
    [InlineData("CHARACTER.IRONCLAD", "character:CHARACTER.IRONCLAD")]
    [InlineData("CHARACTER.HELLOSPIRE-PALADIN", "character:CHARACTER.HELLOSPIRE-PALADIN")]
    public void Character_keys_use_the_full_id(string id, string key) =>
        Assert.Equal(key, Scope.Character(id).Key);

    [Fact]
    public void Shared_scope_keys()
    {
        Assert.Equal("multiplayer", Scope.Multiplayer.Key);
        Assert.Equal("blitz", Scope.Blitz.Key);
    }

    [Theory]
    [InlineData("character:CHARACTER.IRONCLAD")]
    [InlineData("multiplayer")]
    [InlineData("blitz")]
    public void Keys_round_trip(string key) => Assert.Equal(key, Scope.FromKey(key)?.Key);

    [Theory]
    [InlineData("character:")]
    [InlineData("dread")]
    [InlineData("Multiplayer")]
    [InlineData("")]
    public void Unknown_keys_are_not_scopes(string key) => Assert.Null(Scope.FromKey(key));

    [Fact]
    public void Scopes_compare_by_value() =>
        Assert.Equal(Scope.Character("CHARACTER.SILENT"), Scope.Character("CHARACTER.SILENT"));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Character_needs_an_id(string id) => Assert.Throws<ArgumentException>(() => Scope.Character(id));

    [Fact]
    public void Caps_per_scope_and_mode()
    {
        Assert.Equal(10, Caps.For(Scope.Character("CHARACTER.IRONCLAD")));
        Assert.Equal(10, Caps.For(Scope.Multiplayer));
        Assert.Equal(5, Caps.For(Scope.Blitz));
        Assert.Equal(10, Caps.For(RunMode.Standard));
        Assert.Equal(5, Caps.For(RunMode.Blitz));
        Assert.Null(Caps.For(RunMode.Daily));
        Assert.Null(Caps.For(RunMode.Dread));
        Assert.Null(Caps.For(RunMode.Unknown));
    }

    // ---- screen scope (spec 3 / 5) -------------------------------------------------------

    private static Scope? Screen(RunMode mode, LobbyRole role, string? character = "CHARACTER.IRONCLAD", bool random = false) =>
        ScreenScope.Resolve(new ScreenFacts(mode, role, character, random));

    [Fact]
    public void Singleplayer_standard_shows_the_selected_character() =>
        Assert.Equal(Scope.Character("CHARACTER.IRONCLAD"), Screen(RunMode.Standard, LobbyRole.Singleplayer));

    [Fact]
    public void Host_standard_shows_multiplayer_for_any_character()
    {
        Assert.Equal(Scope.Multiplayer, Screen(RunMode.Standard, LobbyRole.Host));
        Assert.Equal(Scope.Multiplayer, Screen(RunMode.Standard, LobbyRole.Host, "CHARACTER.SILENT"));
    }

    [Theory]
    [InlineData(LobbyRole.Singleplayer)]
    [InlineData(LobbyRole.Host)]
    public void Blitz_shows_blitz_for_singleplayer_and_host(LobbyRole role) =>
        Assert.Equal(Scope.Blitz, Screen(RunMode.Blitz, role));

    [Theory]
    [InlineData(RunMode.Standard)]
    [InlineData(RunMode.Blitz)]
    public void Clients_get_no_marker(RunMode mode) => Assert.Null(Screen(mode, LobbyRole.Client));

    [Theory]
    [InlineData(RunMode.Standard, LobbyRole.Singleplayer)]
    [InlineData(RunMode.Standard, LobbyRole.Host)]
    [InlineData(RunMode.Blitz, LobbyRole.Singleplayer)]
    [InlineData(RunMode.Blitz, LobbyRole.Host)]
    public void Random_hides_the_marker(RunMode mode, LobbyRole role) =>
        Assert.Null(Screen(mode, role, "CHARACTER.RANDOM_CHARACTER", random: true));

    [Theory]
    [InlineData(RunMode.Daily)]
    [InlineData(RunMode.Dread)]
    [InlineData(RunMode.Unknown)]
    public void Other_modes_have_no_marker(RunMode mode) => Assert.Null(Screen(mode, LobbyRole.Singleplayer));

    [Fact]
    public void Other_roles_have_no_marker() => Assert.Null(Screen(RunMode.Standard, LobbyRole.Other));

    [Fact]
    public void Singleplayer_without_a_selection_has_no_marker() =>
        Assert.Null(Screen(RunMode.Standard, LobbyRole.Singleplayer, character: null));

    // ---- tooltip (spec 6) ----------------------------------------------------------------

    [Theory]
    [InlineData(true, "A10 won")]
    [InlineData(false, "A10 not yet won. Click to mark")]
    public void Character_tooltip(bool marked, string text) =>
        Assert.Equal(text, MarkerText.Tooltip(Scope.Character("CHARACTER.IRONCLAD"), marked, Caps).ToString());

    [Theory]
    [InlineData(true, "A5 won")]
    [InlineData(false, "A5 not yet won. Click to mark")]
    public void Blitz_tooltip(bool marked, string text) =>
        Assert.Equal(text, MarkerText.Tooltip(Scope.Blitz, marked, Caps).ToString());

    [Theory]
    [InlineData(true, "Multiplayer A10 won")]
    [InlineData(false, "Multiplayer A10 not yet won. Click to mark")]
    public void Multiplayer_tooltip_is_prefixed(bool marked, string text) =>
        Assert.Equal(text, MarkerText.Tooltip(Scope.Multiplayer, marked, Caps).ToString());

    [Fact]
    public void Tooltip_splits_into_title_and_description()
    {
        Assert.Equal(new TooltipText("A10 not yet won", "Click to mark"), MarkerText.Tooltip(Scope.Character("CHARACTER.IRONCLAD"), false, Caps));
        Assert.Equal(new TooltipText("Multiplayer A10 won", ""), MarkerText.Tooltip(Scope.Multiplayer, true, Caps));
        Assert.Equal(new TooltipText("A5 not yet won", "Click to mark"), MarkerText.Tooltip(Scope.Blitz, false, Caps));
    }
}
