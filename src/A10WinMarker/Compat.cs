using System.Reflection;
using A10WinMarker.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Nodes.Screens.Blitz;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Saves;

namespace A10WinMarker;

/// <summary>
/// Build compatibility (spec 9 and 12). The mod runs only on the target build, and every member
/// it touches is resolved by reflection first, so a missing member is reported instead of
/// throwing at JIT time.
/// </summary>
internal static class Compat
{
    public const string TargetVersion = "v0.113.1";
    public const string TargetCommit = "d09d48e5";

    /// <summary>The screens that get a marker. Custom-run screens are deliberately absent.</summary>
    public static readonly Type[] Screens = { typeof(NCharacterSelectScreen), typeof(NBlitzScreen) };

    private static readonly (Type Type, string Member)[] Required =
    {
        (typeof(SaveManager), nameof(SaveManager.Instance)),
        (typeof(SaveManager), nameof(SaveManager.GetProfileScopedPath)),
        (typeof(AscensionManager), "maxAscensionAllowed"),
        (typeof(AscensionManager), "maxAscensionAllowedBlitz"),
    };

    private static readonly Lazy<IReadOnlyList<string>> _missing = new(FindMissing);

    /// <summary>Members needed by everything (storage and caps). Empty when compatible.</summary>
    public static IReadOnlyList<string> Missing => _missing.Value;

    public static ReleaseInfo? Release
    {
        get
        {
            try
            {
                return ReleaseInfoManager.Instance.ReleaseInfo;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public static bool IsTargetBuild =>
        Release is { } r
        && r.Version == TargetVersion
        && r.Commit?.StartsWith(TargetCommit, StringComparison.OrdinalIgnoreCase) == true;

    public static string Describe() =>
        Release is { } r ? $"{r.Version} ({r.Commit})" : "unknown build";

    /// <summary>Caps from the running game's constants (not compile-time copies, which are inlined).</summary>
    public static Caps Caps { get; } = new(
        ReadConst("maxAscensionAllowed") ?? 10,
        ReadConst("maxAscensionAllowedBlitz") ?? 5);

    /// <summary>A screen's private <c>_lobby</c> field, or null if the build renamed it.</summary>
    public static FieldInfo? LobbyField(Type screen) => AccessTools.Field(screen, "_lobby");

    /// <summary>A screen's private <c>_ascensionPanel</c> field, or null if the build renamed it.</summary>
    public static FieldInfo? PanelField(Type screen) => AccessTools.Field(screen, "_ascensionPanel");

    private static int? ReadConst(string name)
    {
        var field = typeof(AscensionManager).GetField(name, BindingFlags.Public | BindingFlags.Static);
        return field?.GetRawConstantValue() is int value ? value : null;
    }

    private static IReadOnlyList<string> FindMissing()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        return Required
            .Where(r => r.Type.GetMember(r.Member, all).Length == 0)
            .Select(r => $"{r.Type.Name}.{r.Member}")
            .ToList();
    }
}
