using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Runs;
using LizardTail.LizardTailCode.Core;

namespace LizardTail.LizardTailCode.Game;

/// <summary>
/// Which earlier run is being shadowed, and what it did on the floor you are standing on.
///
/// One static seam rather than a node, because the patches that decorate each screen run wherever
/// the game happens to build that screen and need the answer without reaching through the tree.
/// </summary>
public static class Shadowing
{
    /// <summary>RunManager keeps the live run in a private property, so it is read by reflection.</summary>
    private static readonly PropertyInfo? StateProperty = AccessTools.Property(typeof(RunManager), "State");

    private static bool _warnedMissingState;
    private static ShadowLibrary? _library;
    private static bool _suppressed;

    /// <summary>Raised when the shadowed run changes, so an open picker can redraw.</summary>
    public static event Action? Changed;

    /// <summary>The runs available for the seed in play, or null before a run has started.</summary>
    public static ShadowLibrary? Library => _library;

    /// <summary>True while a run is being shadowed and the glow should be drawn.</summary>
    public static bool IsOn => !_suppressed && _library?.Current != null;

    /// <summary>
    /// Set from the picker to hide the glow without forgetting which run was chosen. Shadowing every
    /// choice makes a run trivial, and that is not always what you want on the floor in front of you.
    /// </summary>
    public static bool Suppressed
    {
        get => _suppressed;
        set
        {
            if (_suppressed == value)
            {
                return;
            }

            _suppressed = value;
            Changed?.Invoke();
        }
    }

    /// <summary>The run being shadowed, or null when there is none or the glow is suppressed.</summary>
    public static ShadowRun? Run => IsOn ? _library?.Current : null;

    /// <summary>Shows a different run of the same seed.</summary>
    public static void Select(int index)
    {
        if (_library is not { } library)
        {
            return;
        }

        var next = library.Select(index);
        if (ReferenceEquals(next, library))
        {
            return;
        }

        _library = next;
        ModLog.Info(ShadowText.Loaded(next));
        Changed?.Invoke();
    }

    /// <summary>Loads the runs for a seed. Called as a run becomes live, new or loaded.</summary>
    public static void Begin(RunState? state)
    {
        try
        {
            var seed = state?.Rng?.StringSeed;
            var character = LocalCharacter(state);

            if (string.IsNullOrWhiteSpace(seed) || character == null)
            {
                Forget();
                return;
            }

            _library = ShadowStore.Load(seed!, character);
            _suppressed = false;
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            // Nothing here is worth interrupting the start of a run for.
            ModLog.Warn($"Could not start shadowing: {ex.Message}");
            Forget();
        }
    }

    public static void Forget()
    {
        if (_library == null && !_suppressed)
        {
            return;
        }

        _library = null;
        _suppressed = false;
        Changed?.Invoke();
    }

    /// <summary>
    /// What the shadowed run did on the floor you are on now.
    ///
    /// The floor's row is the length of the live run's own history for this act, because the game
    /// appends an entry as you enter a room: the entry being filled in right now is the last one.
    /// Null once you go deeper than the shadowed run ever got, which is how a shadow normally ends.
    /// </summary>
    public static ShadowFloor? CurrentFloor()
    {
        if (Run is not { } run || CurrentState() is not { } state)
        {
            return null;
        }

        var act = state.CurrentActIndex;
        var history = state.MapPointHistory;
        if (act < 0 || act >= history.Count)
        {
            return null;
        }

        var row = history[act].Count - 1;
        return row < 0 ? null : run.FloorAt(act, row);
    }

    /// <summary>Where you are, for the log: the floor a mark was or was not drawn on.</summary>
    public static string Where()
    {
        if (CurrentState() is not { } state)
        {
            return "(no run)";
        }

        var act = state.CurrentActIndex;
        var history = state.MapPointHistory;
        var row = act >= 0 && act < history.Count ? history[act].Count - 1 : -1;
        var known = CurrentFloor() is { } floor ? floor.PointType : "nothing recorded";
        return $"at act {act} row {row} ({known})";
    }

    /// <summary>The live run, read through the private property the game keeps it in.</summary>
    public static RunState? CurrentState()
    {
        var manager = RunManager.Instance;
        if (manager == null)
        {
            return null;
        }

        if (StateProperty == null)
        {
            if (!_warnedMissingState)
            {
                _warnedMissingState = true;
                ModLog.Error("RunManager.State was not found, so nothing can be shadowed; the game version may have changed.");
            }
            return null;
        }

        try
        {
            return StateProperty.GetValue(manager) as RunState;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The character you are playing, which decides whose choices to shadow. In co-op a teammate's
    /// picks are not yours, and a run of this seed played by another character is no help at all.
    /// </summary>
    private static string? LocalCharacter(RunState? state)
    {
        if (state == null)
        {
            return null;
        }

        try
        {
            // Returns null in ordinary single-player, where there is no network identity to match.
            var me = LocalContext.GetMe(state) ?? (state.Players.Count > 0 ? state.Players[0] : null);
            return me?.Character?.Id?.ToString();
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Loads the shadow as a run becomes live. Launch runs for a new run and for a loaded one.</summary>
[HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
public static class ShadowBeginPatch
{
    public static void Postfix(RunState __result) => Shadowing.Begin(__result);
}

/// <summary>Drops the shadow when the run ends, so the next run does not inherit it.</summary>
[HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
public static class ShadowEndPatch
{
    public static void Postfix() => Shadowing.Forget();
}
