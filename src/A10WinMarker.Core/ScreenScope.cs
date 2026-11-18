namespace A10WinMarker.Core;

/// <summary>The local player's role in the screen's lobby (the game's <c>NetGameType</c>).</summary>
public enum LobbyRole
{
    Singleplayer,
    Host,
    Client,
    Other,
}

/// <summary>Plain facts about a character-select or Blitz screen, read by the adapter from its lobby.</summary>
/// <param name="Mode">The lobby's game mode.</param>
/// <param name="Role">Singleplayer, host or client.</param>
/// <param name="CharacterId">The local player's selected character id, or null if none yet.</param>
/// <param name="IsRandom">The selected character is Random.</param>
public sealed record ScreenFacts(RunMode Mode, LobbyRole Role, string? CharacterId, bool IsRandom);

/// <summary>Which scope a screen's marker shows (spec 3 and 5), or none.</summary>
public static class ScreenScope
{
    public static Scope? Resolve(ScreenFacts screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        // Joiners get no marker (their panel is hidden), and nothing else has a scope.
        if (screen.Role is not (LobbyRole.Singleplayer or LobbyRole.Host))
        {
            return null;
        }
        // Random has no scope; the marker is hidden while it is selected, on every screen.
        if (screen.IsRandom)
        {
            return null;
        }

        return screen.Mode switch
        {
            RunMode.Blitz => Scope.Blitz,
            RunMode.Standard when screen.Role == LobbyRole.Host => Scope.Multiplayer,
            RunMode.Standard when !string.IsNullOrWhiteSpace(screen.CharacterId) => Scope.Character(screen.CharacterId),
            _ => null,
        };
    }
}

/// <summary>Hover-tip text in the game's title + description layout.</summary>
/// <param name="Title">Bold first line, e.g. <c>A10 won</c>.</param>
/// <param name="Description">Body text, or empty when there is none.</param>
public sealed record TooltipText(string Title, string Description)
{
    /// <summary>Single-line form, as written in the spec.</summary>
    public override string ToString() => Description.Length == 0 ? Title : $"{Title}. {Description}";
}

/// <summary>Hover text for the marker (spec 6).</summary>
public static class MarkerText
{
    public static TooltipText Tooltip(Scope scope, bool marked, Caps caps)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(caps);
        var level = $"A{caps.For(scope)}";
        var prefix = scope.Kind == ScopeKind.Multiplayer ? "Multiplayer " : "";
        return marked
            ? new TooltipText($"{prefix}{level} won", "")
            : new TooltipText($"{prefix}{level} not yet won", "Click to mark");
    }
}
