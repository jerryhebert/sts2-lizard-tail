using System.Globalization;

namespace LizardTail.LizardTailCode.Core;

/// <summary>Matching seeds the way the game does before it hashes them.</summary>
public static class ShadowSeed
{
    /// <summary>
    /// The game canonicalizes a typed seed before use: upper case, O read as zero and I as one,
    /// trimmed. Two seeds are the same run if and only if these agree, so every comparison goes
    /// through here rather than comparing the raw strings.
    /// </summary>
    public static string Canonical(string? seed) =>
        string.IsNullOrWhiteSpace(seed)
            ? ""
            : seed.Trim().ToUpperInvariant().Replace('O', '0').Replace('I', '1');

    public static bool Same(string? a, string? b) =>
        Canonical(a).Length > 0 && Canonical(a) == Canonical(b);
}

/// <summary>
/// The runs available to shadow for the seed being played, and which one is shown.
///
/// Immutable: choosing a different run makes a new library rather than mutating this one, so the
/// patches can read <see cref="Current"/> without worrying about when it changes under them.
/// </summary>
public sealed class ShadowLibrary
{
    private ShadowLibrary(string seed, string character, IReadOnlyList<ShadowRun> runs, int index)
    {
        Seed = seed;
        Character = character;
        Runs = runs;
        Index = index;
    }

    public string Seed { get; }
    public string Character { get; }

    /// <summary>Every run of this seed by this character, oldest first.</summary>
    public IReadOnlyList<ShadowRun> Runs { get; }

    /// <summary>Which of <see cref="Runs"/> is being shadowed.</summary>
    public int Index { get; }

    public ShadowRun? Current => Index >= 0 && Index < Runs.Count ? Runs[Index] : null;

    public bool HasAny => Runs.Count > 0;

    /// <summary>
    /// Gathers the runs worth shadowing for this seed and character, oldest first, and opens on the
    /// one <see cref="DefaultIndex"/> chooses.
    ///
    /// A run with no floors is dropped: it was abandoned before anything was recorded, so there is
    /// nothing in it to show and it would only clutter the picker.
    /// </summary>
    public static ShadowLibrary For(IEnumerable<ShadowRun> candidates, string seed, string character)
    {
        var runs = candidates
            .Where(r => ShadowSeed.Same(r.Seed, seed))
            .Where(r => string.Equals(r.Character, character, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.FloorCount > 0)
            .OrderBy(r => r.StartTime)
            .ToList();

        return new ShadowLibrary(ShadowSeed.Canonical(seed), character, runs, DefaultIndex(runs));
    }

    public static ShadowLibrary Empty(string seed, string character) =>
        new(ShadowSeed.Canonical(seed), character, Array.Empty<ShadowRun>(), -1);

    /// <summary>
    /// Which run to open on: the one that got furthest, a win breaking a tie on depth, and the
    /// earliest breaking that.
    ///
    /// Not simply the first run of the seed. A seed you have replayed is mostly false starts, runs
    /// abandoned on floor one or two while re-rolling, and opening on one of those would shadow
    /// almost nothing. Depth is what makes a run worth following.
    /// </summary>
    public static int DefaultIndex(IReadOnlyList<ShadowRun> runs)
    {
        if (runs.Count == 0)
        {
            return -1;
        }

        var best = 0;
        for (var i = 1; i < runs.Count; i++)
        {
            if (Better(runs[i], runs[best]))
            {
                best = i;
            }
        }

        return best;
    }

    private static bool Better(ShadowRun a, ShadowRun b) =>
        a.FloorCount != b.FloorCount ? a.FloorCount > b.FloorCount
        : a.Win != b.Win ? a.Win
        : a.StartTime < b.StartTime;

    /// <summary>Shows a different run. Out-of-range leaves the library alone rather than throwing.</summary>
    public ShadowLibrary Select(int index) =>
        index == Index || index < 0 || index >= Runs.Count
            ? this
            : new ShadowLibrary(Seed, Character, Runs, index);

    /// <summary>Steps through the runs, wrapping, so one key can cycle them.</summary>
    public ShadowLibrary Step(int delta)
    {
        if (Runs.Count <= 1 || delta == 0)
        {
            return this;
        }

        var next = ((Index + delta) % Runs.Count + Runs.Count) % Runs.Count;
        return Select(next);
    }
}

/// <summary>Everything the shadow feature puts on screen. Kept out of the UI classes, as SlotText is.</summary>
public static class ShadowText
{
    /// <summary>One row of the picker: when it was played, how far it got, and how it ended.</summary>
    public static string Row(ShadowRun run)
    {
        var when = DateTimeOffset.FromUnixTimeSeconds(run.StartTime).ToLocalTime()
            .ToString("d MMM yyyy", CultureInfo.CurrentCulture);
        return $"{when} — floor {run.FloorCount.ToString(CultureInfo.InvariantCulture)}, {run.Outcome}";
    }

    public static string Title(ShadowLibrary library) =>
        $"Shadowing seed {library.Seed}";

    public static string Position(ShadowLibrary library) =>
        library.Runs.Count <= 1
            ? ""
            : $"Run {(library.Index + 1).ToString(CultureInfo.InvariantCulture)} of {library.Runs.Count.ToString(CultureInfo.InvariantCulture)}";

    public static string Nothing(string seed) =>
        $"No earlier run of seed {ShadowSeed.Canonical(seed)} with this character. Nothing to shadow.";

    /// <summary>Said once per run in the log, so the feature's state is visible without the panel.</summary>
    public static string Loaded(ShadowLibrary library) =>
        library.Current is { } run
            ? $"Shadowing {ShadowText.Row(run)} ({library.Runs.Count} run(s) for seed {library.Seed})"
            : Nothing(library.Seed);
}
