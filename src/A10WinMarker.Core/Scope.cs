namespace A10WinMarker.Core;

/// <summary>Kind of scope a mark belongs to (spec 3).</summary>
public enum ScopeKind
{
    Character,
    Multiplayer,
    Blitz,
}

/// <summary>
/// One markable scope. <see cref="CharacterId"/> is set only for <see cref="ScopeKind.Character"/>
/// and holds the game's full model id, e.g. <c>CHARACTER.IRONCLAD</c>, so modded characters need
/// no list.
/// </summary>
public sealed record Scope
{
    public const string CharacterPrefix = "character:";
    public const string MultiplayerKey = "multiplayer";
    public const string BlitzKey = "blitz";

    private Scope(ScopeKind kind, string? characterId)
    {
        Kind = kind;
        CharacterId = characterId;
    }

    public ScopeKind Kind { get; }

    public string? CharacterId { get; }

    public static readonly Scope Multiplayer = new(ScopeKind.Multiplayer, null);

    public static readonly Scope Blitz = new(ScopeKind.Blitz, null);

    public static Scope Character(string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            throw new ArgumentException("Character id is required", nameof(characterId));
        }
        return new Scope(ScopeKind.Character, characterId);
    }

    /// <summary>Storage key: <c>character:&lt;id&gt;</c>, <c>multiplayer</c> or <c>blitz</c>.</summary>
    public string Key => Kind switch
    {
        ScopeKind.Character => CharacterPrefix + CharacterId,
        ScopeKind.Multiplayer => MultiplayerKey,
        _ => BlitzKey,
    };

    /// <summary>Parses a storage key. Returns null for anything else (such keys are kept, not used).</summary>
    public static Scope? FromKey(string key)
    {
        if (key == MultiplayerKey)
        {
            return Multiplayer;
        }
        if (key == BlitzKey)
        {
            return Blitz;
        }
        if (key.StartsWith(CharacterPrefix, StringComparison.Ordinal) && key.Length > CharacterPrefix.Length)
        {
            return Character(key[CharacterPrefix.Length..]);
        }
        return null;
    }

    public override string ToString() => Key;
}

/// <summary>Game mode, mapped by the adapter from the game's enum by name.</summary>
public enum RunMode
{
    Standard,
    Daily,
    Dread,
    Blitz,
    Unknown,
}

/// <summary>
/// Top ascension per mode. The adapter fills it from the game's constants
/// (<c>AscensionManager.maxAscensionAllowed</c> / <c>maxAscensionAllowedBlitz</c>).
/// </summary>
public sealed record Caps(int Standard, int Blitz)
{
    /// <summary>Top ascension for a recordable mode, or null for modes the mod ignores.</summary>
    public int? For(RunMode mode) => mode switch
    {
        RunMode.Standard => Standard,
        RunMode.Blitz => Blitz,
        _ => null,
    };

    /// <summary>Top ascension a scope is marked for: Blitz uses the Blitz cap, the rest Standard.</summary>
    public int For(Scope scope) => scope.Kind == ScopeKind.Blitz ? Blitz : Standard;
}
