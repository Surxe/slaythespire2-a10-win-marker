using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace A10WinMarker;

/// <summary>
/// Loader entry point. Checks the build first (spec 12) and applies the Harmony patches only on
/// the target build (spec 9). Declaring an initializer also stops the loader from calling
/// <c>Harmony.PatchAll</c> on this assembly, so nothing is patched implicitly.
/// Loading never reads or writes any file.
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string ModId = "A10WinMarker";

    public static void Initialize()
    {
        try
        {
            if (!Compat.IsTargetBuild)
            {
                Log.Warn($"Built for {Compat.TargetVersion} ({Compat.TargetCommit}), running on {Compat.Describe()}; doing nothing.");
                return;
            }
            if (Compat.Missing.Count > 0)
            {
                Log.Error($"Missing game members, doing nothing: {string.Join(", ", Compat.Missing)}");
                return;
            }
            Patches.Apply();
            Log.Info($"Loaded on {Compat.Describe()}. Recording: {OnOff(Patches.RecordingEnabled)}, marker: {OnOff(Patches.MarkerEnabled)}.");
        }
        catch (Exception e)
        {
            Log.Error($"Initialize failed (mod stays inert): {e}");
        }
    }

    private static string OnOff(bool on) => on ? "on" : "off";
}

internal static class Log
{
    private static readonly Logger Logger = new(ModEntry.ModId, LogType.Generic);

    public static void Info(string text) => Logger.Info(text, 2);

    public static void Warn(string text) => Logger.Warn(text, 2);

    public static void Error(string text) => Logger.Error(text, 2);
}
