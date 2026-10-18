using System.Diagnostics;
using MegaCrit.Sts2.Core.Saves.Managers;
using LizardTail.LizardTailCode.Core;

namespace LizardTail.LizardTailCode.Game;

/// <summary>
/// Finds earlier runs of a seed among the game's own history records.
///
/// The game writes one history/&lt;start_time&gt;.run per finished run and never deletes the local
/// copy, so this reads them rather than recording anything itself: a seed played long before the
/// mod was installed can still be shadowed.
/// </summary>
public static class ShadowStore
{
    /// <summary>Where the game keeps its finished runs, for the profile in use.</summary>
    public static string HistoryDir => RunHistorySaveManager.GetHistoryPath(SlotStore.ProfileId);

    /// <summary>
    /// Every earlier run of this seed played by this character, as a library opened on the one
    /// worth shadowing. Never throws: a history that cannot be read is an empty library, which
    /// turns the feature off rather than interrupting a run.
    /// </summary>
    public static ShadowLibrary Load(string seed, string character)
    {
        try
        {
            var watch = Stopwatch.StartNew();
            var runs = Scan(seed, character, out var examined);
            var library = ShadowLibrary.For(runs, seed, character);

            ModLog.Info($"Read {examined} run record(s) in {watch.ElapsedMilliseconds}ms. {ShadowText.Loaded(library)}");
            return library;
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not read the run history, so nothing is shadowed: {ex.Message}");
            return ShadowLibrary.Empty(seed, character);
        }
    }

    private static List<ShadowRun> Scan(string seed, string character, out int examined)
    {
        examined = 0;
        var found = new List<ShadowRun>();
        var dir = HistoryDir;
        var store = SlotStore.Local;

        // A full history is hundreds of files and tens of megabytes. Building a JSON document for
        // every one of them is the slow part, so the seed is matched against the raw text first and
        // only a file that could be a hit is actually parsed.
        var needle = $"\"{ShadowSeed.Canonical(seed)}\"";

        foreach (var file in store.GetFilesInDirectory(dir))
        {
            if (!file.EndsWith(".run", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            examined++;
            try
            {
                var text = store.ReadFile($"{dir}/{file}");
                if (text == null || !text.Contains(needle, StringComparison.Ordinal))
                {
                    continue;
                }

                if (ShadowRun.TryParse(text, character) is { } run && ShadowSeed.Same(run.Seed, seed))
                {
                    found.Add(run);
                }
            }
            catch (Exception ex)
            {
                // One unreadable record must not hide the rest, exactly as when listing parked runs.
                ModLog.Warn($"Could not read run record {file}: {ex.Message}");
            }
        }

        return found;
    }
}
