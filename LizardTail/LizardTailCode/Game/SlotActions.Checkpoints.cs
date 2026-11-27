using System.Text;
using MegaCrit.Sts2.Core.Saves;
using LizardTail.LizardTailCode.Core;

namespace LizardTail.LizardTailCode.Game;

public static partial class SlotActions
{
    /// <summary>
    /// Goes back to a checkpoint: makes its save the game's save of its kind, so Continue (or Load) picks
    /// the run up from that moment. Checked like a swap before anything changes: you are in the run
    /// (multiplayer), the game can load it with the installed version and mods, and the active save can
    /// be copied. Nothing is lost on the way:
    /// <list type="bullet">
    /// <item>The active save is kept: as a checkpoint when it is the same run (the other branch), or
    /// parked when it is another run.</item>
    /// <item>Parked copies of this run become checkpoints. The parked list clears copies of the active
    /// run once the game saves past them, which would sweep away the other branch.</item>
    /// <item>A run that was won, lost or abandoned since has its history record moved aside (a copy is
    /// kept), or the game would delete the save again at the next launch. This is the one step that can
    /// still refuse, so it goes first, and it is put back if anything after it fails.</item>
    /// </list>
    /// The checkpoint itself stays, so it can be gone back to again.
    /// </summary>
    public static string Restore(Checkpoint checkpoint)
    {
        var kind = checkpoint.Kind;
        if (!CheckpointRules.CanRestore(checkpoint.State) || checkpoint.Summary == null)
        {
            throw new InvalidOperationException(CheckpointText.StateNote(checkpoint.State) ?? "This checkpoint cannot be restored.");
        }

        var text = SlotStore.Local.ReadFile(checkpoint.Path) ?? throw new InvalidOperationException("The checkpoint could not be read.");
        var run = RunSummary.TryParse(text);
        if (run == null || run.StartTime != checkpoint.Summary.StartTime || run.SaveTime != checkpoint.Summary.SaveTime)
        {
            throw new InvalidOperationException("The checkpoint changed since the list was drawn. Nothing was changed.");
        }

        var host = SaveKinds.NeedsYouInIt(kind) ? LocalPlayerId : 0;
        if (SaveKinds.NeedsYouInIt(kind))
        {
            if (host == 0)
            {
                throw new InvalidOperationException("Steam is not ready yet, so the game could not host this run. Nothing was changed.");
            }
            if (!run.HasPlayer(host))
            {
                throw new InvalidOperationException(SlotText.StateNote(SlotState.NotYourRun));
            }
        }
        SaveCheck.Verify(kind, text, host);

        var ending = SlotStore.EndingAnywhere(run.StartTime);
        (string Text, RunSummary Run)? active = SlotStore.HasActive(kind) ? ReadParkable(kind) : null;
        if (active is { } same && ending == RunEnding.None && CheckpointRules.SameSave(same.Text, text))
        {
            return $"The run is already at “{checkpoint.Info.Label}”. {SlotText.ContinuesIt(kind)}";
        }

        // Every check has passed. The history record goes first: it is the one step that can still refuse.
        var label = checkpoint.Info.Label;
        var record = ending != RunEnding.None ? ReviveHistory(kind, run.StartTime, allowFinished: true) : null;
        var path = SlotStore.ActivePath(kind);
        try
        {
            if (active is { } current)
            {
                KeepActive(kind, current.Text, current.Run, run, label);
            }
            foreach (var parked in SlotStore.AllSlots(kind, 0).Where(s => s.Summary?.StartTime == run.StartTime))
            {
                if (SlotStore.Local.ReadFile(parked.Path) is { } parkedText && RunSummary.TryParse(parkedText) is { } parkedRun)
                {
                    CheckpointStore.KeepAutomatically(kind, parkedText, parkedRun, CheckpointText.FromParked(parkedRun));
                    SlotStore.MoveToTrash(parked);
                }
            }

            if (active != null)
            {
                ClearActive(kind);
            }
            SlotStore.Local.CreateDirectory(path[..path.LastIndexOf('/')]);
            using (SlotStore.Manager.BeginSaveBatch())
            {
                SlotStore.Store.WriteFile(path, Encoding.UTF8.GetBytes(text));
            }
            if (!SameCopy(RunSummary.TryParse(SlotStore.ReadActive(kind)), run))
            {
                throw new InvalidOperationException($"The checkpoint did not read back from the game's {SaveKinds.Name(kind)} slot. Nothing was lost: the checkpoint is still there.");
            }
        }
        catch
        {
            if (record != null)
            {
                RestoreHistory(kind, run.StartTime, record);
            }
            throw;
        }

        if (SlotStore.Cloud is { } cloud)
        {
            RunSummary? inCloud;
            try
            {
                inCloud = RunSummary.TryParse(cloud.FileExists(path) ? cloud.ReadFile(path) : null);
            }
            catch (Exception ex)
            {
                ModLog.Warn($"Restored checkpoint of run {run.Key}, but could not read it back from Steam Cloud: {ex.Message}");
                return $"Back at “{label}”, but Steam Cloud could not be checked just now. {SlotText.ContinuesIt(kind)}";
            }
            if (!SameCopy(inCloud, run))
            {
                // The next launch would undo the restore anyway. Undo it now, completely, so nothing is
                // played on a save the game is going to remove: clear the slot and put the history back.
                ModLog.Warn($"Restored checkpoint of run {run.Key} on this PC, but Steam Cloud did not take the write; undoing the restore");
                try
                {
                    ClearActive(kind);
                }
                catch (Exception ex)
                {
                    ModLog.Warn($"Could not clear the refused restore: {ex.Message}");
                }
                if (record != null)
                {
                    RestoreHistory(kind, run.StartTime, record);
                }
                var kept = active == null ? "" : active.Value.Run.StartTime == run.StartTime
                    ? " Where you were is kept as a checkpoint."
                    : " The run that was active is parked.";
                throw new InvalidOperationException($"Steam Cloud refused the save (it may be full), so the restore was undone.{kept} Free some Steam Cloud space or turn off Steam Cloud for the game, then restore it again.");
            }
        }

        ModLog.Info($"Restored checkpoint “{label}” of {SaveKinds.Name(kind)} run {run.Key}");
        return $"Back at “{label}”. {SlotText.ContinuesIt(kind)}";
    }

    /// <summary>
    /// Keeps the active save before a restore replaces it. The same run's save is the other branch, so it
    /// becomes a checkpoint, and so does the game's previous save of it (its .backup), which the game
    /// deletes along with the save. Another run is parked, with its .backup, as a swap would.
    /// </summary>
    private static void KeepActive(SaveKind kind, string text, RunSummary current, RunSummary restoring, string label)
    {
        if (current.StartTime != restoring.StartTime)
        {
            SlotStore.Park(kind, text, current);
            SlotStore.ParkBackup(kind, text);
            Remember(kind, current);
            return;
        }

        CheckpointStore.KeepAutomatically(kind, text, current, CheckpointText.BeforeGoingBack(label));
        var backupPath = SlotStore.ActivePath(kind) + ".backup";
        var backup = SlotStore.Store.FileExists(backupPath) ? SlotStore.Store.ReadFile(backupPath) : null;
        if (backup != null && backup != text && RunSummary.TryParse(backup) is { } earlier)
        {
            if (earlier.StartTime == current.StartTime)
            {
                CheckpointStore.KeepAutomatically(kind, backup, earlier, CheckpointText.BeforeGoingBackEarlier(label));
            }
            else
            {
                SlotStore.ParkBackup(kind, text);
            }
        }
    }
}
