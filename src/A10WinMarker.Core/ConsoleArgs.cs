namespace A10WinMarker.Core;

/// <summary>A parsed <c>a10mark</c> console command (spec 11).</summary>
public abstract record ConsoleCommand
{
    private protected ConsoleCommand()
    {
    }
}

/// <summary><c>a10mark list</c></summary>
public sealed record ListCommand : ConsoleCommand;

/// <summary><c>a10mark set &lt;scope&gt; [on|off]</c></summary>
public sealed record SetCommand(Scope Scope, bool Marked) : ConsoleCommand;

/// <summary><c>a10mark sim &lt;mode&gt; &lt;players&gt; &lt;character&gt; &lt;ascension&gt; &lt;win&gt;</c></summary>
public sealed record SimCommand(RunFacts Run) : ConsoleCommand;

/// <summary>
/// Pure argument parsing for the dev console command, so it is unit-tested without the game.
/// Scope names: <c>multiplayer</c>, <c>blitz</c>, or a character id with the <c>CHARACTER.</c>
/// prefix optional (<c>ironclad</c> = <c>CHARACTER.IRONCLAD</c>). Case-insensitive.
/// </summary>
public static class ConsoleArgs
{
    public const string CharacterCategory = "CHARACTER";

    public const string Usage =
        "a10mark list | a10mark set <scope> [on|off] | a10mark sim <mode> <players> <character> <ascension> <win>";

    public static readonly IReadOnlyList<string> Subcommands = new[] { "list", "set", "sim" };

    public static readonly IReadOnlyList<string> Modes = new[] { "standard", "blitz", "daily", "dread" };

    /// <summary>Parses the arguments after <c>a10mark</c>. Returns null and sets <paramref name="error"/> on bad input.</summary>
    public static ConsoleCommand? Parse(IReadOnlyList<string> args, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        error = null;
        var verb = args.Count == 0 ? "" : args[0].Trim().ToLowerInvariant();
        switch (verb)
        {
            case "list" when args.Count == 1:
                return new ListCommand();

            case "set" when args.Count is 2 or 3:
                if (ParseScope(args[1]) is not { } scope)
                {
                    return Fail(out error, $"Unknown scope '{args[1]}'. Use multiplayer, blitz or a character id (e.g. ironclad).");
                }
                var on = true;
                if (args.Count == 3)
                {
                    if (ParseOnOff(args[2]) is not { } parsed)
                    {
                        return Fail(out error, $"Expected on or off, got '{args[2]}'.");
                    }
                    on = parsed;
                }
                return new SetCommand(scope, on);

            case "sim" when args.Count == 6:
                return ParseSim(args, out error);


            default:
                return Fail(out error, $"Usage: {Usage}");
        }
    }

    /// <summary><c>multiplayer</c>, <c>blitz</c>, or a character id (prefix optional).</summary>
    public static Scope? ParseScope(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var name = input.Trim();
        if (name.Equals(Scope.MultiplayerKey, StringComparison.OrdinalIgnoreCase))
        {
            return Scope.Multiplayer;
        }
        if (name.Equals(Scope.BlitzKey, StringComparison.OrdinalIgnoreCase))
        {
            return Scope.Blitz;
        }
        return NormalizeCharacterId(name) is { } id ? Scope.Character(id) : null;
    }

    /// <summary>
    /// <c>ironclad</c>, <c>character.ironclad</c> and <c>CHARACTER.IRONCLAD</c> all give
    /// <c>CHARACTER.IRONCLAD</c>. Ids are upper case in the game, modded ones included.
    /// Returns null for empty input or another category (<c>CARD.X</c>).
    /// </summary>
    public static string? NormalizeCharacterId(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var name = input.Trim().ToUpperInvariant();
        if (name.Length == 0 || name.Any(char.IsWhiteSpace))
        {
            return null;
        }
        var dot = name.IndexOf('.');
        if (dot < 0)
        {
            return $"{CharacterCategory}.{name}";
        }
        var category = name[..dot];
        var entry = name[(dot + 1)..];
        return category == CharacterCategory && entry.Length > 0 && !entry.Contains('.') ? name : null;
    }

    public static bool? ParseOnOff(string input) => input.Trim().ToLowerInvariant() switch
    {
        "on" => true,
        "off" => false,
        _ => null,
    };

    public static RunMode? ParseMode(string input) => input.Trim().ToLowerInvariant() switch
    {
        "standard" => RunMode.Standard,
        "blitz" => RunMode.Blitz,
        "daily" => RunMode.Daily,
        "dread" => RunMode.Dread,
        _ => null,
    };

    /// <summary><c>win</c>/<c>true</c>/<c>1</c> or <c>loss</c>/<c>lose</c>/<c>false</c>/<c>0</c>.</summary>
    public static bool? ParseWin(string input) => input.Trim().ToLowerInvariant() switch
    {
        "win" or "true" or "1" => true,
        "loss" or "lose" or "false" or "0" => false,
        _ => null,
    };

    private static ConsoleCommand? ParseSim(IReadOnlyList<string> args, out string? error)
    {
        error = null;
        if (ParseMode(args[1]) is not { } mode)
        {
            return Fail(out error, $"Unknown mode '{args[1]}'. Use {string.Join(", ", Modes)}.");
        }
        if (!int.TryParse(args[2], out var players) || players < 1)
        {
            return Fail(out error, $"Players must be a whole number of at least 1, got '{args[2]}'.");
        }
        if (NormalizeCharacterId(args[3]) is not { } character)
        {
            return Fail(out error, $"Bad character id '{args[3]}'.");
        }
        if (!int.TryParse(args[4], out var ascension) || ascension < 0)
        {
            return Fail(out error, $"Ascension must be a whole number of at least 0, got '{args[4]}'.");
        }
        if (ParseWin(args[5]) is not { } win)
        {
            return Fail(out error, $"Expected win or loss, got '{args[5]}'.");
        }
        // A simulated run is a non-custom run the game accepted, exactly what the hook would see.
        return new SimCommand(new RunFacts(
            Success: true,
            Victory: win,
            IsCustom: false,
            Mode: mode,
            Ascension: ascension,
            PlayerCount: players,
            CharacterId: character));
    }

    private static ConsoleCommand? Fail(out string? error, string message)
    {
        error = message;
        return null;
    }
}
