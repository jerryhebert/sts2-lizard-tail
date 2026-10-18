using MegaCrit.Sts2.Core.Saves;
using LizardTail.LizardTailCode.Core;

namespace LizardTail.LizardTailCode.Game;

/// <summary>A checkpoint: a copy of a run's save at one moment, with the label you gave it.</summary>
public sealed record Checkpoint(SaveKind Kind, string FileName, string Path, RunSummary? Summary, SlotState State, CheckpointInfo Info);

/// <summary>
/// Checkpoints live in their own folder beside the parked runs (checkpoints/), each a copy of a run's
/// save plus a small label file. They are kept apart from parked runs on purpose: the parked list hides
/// and clears older copies of the active run once the game saves past them, and a checkpoint is exactly
/// such an older copy. Nothing here is ever overwritten or deleted; Trash moves files to the trash folder.
/// </summary>
public static class CheckpointStore
{
    public static string Dir(SaveKind kind) => $"{SlotStore.Folder(kind)}/checkpoints";

    private static string OtherDir(SaveKind kind) => $"{SlotStore.OtherFolder(kind)}/checkpoints";

    private static string InfoPath(string savePath) => savePath[..^".save".Length] + ".json";

    /// <summary>
    /// Every checkpoint of this kind, readable or not, in no particular order: this profile's folder and
    /// the other kind of profile's, so switching between shared and separate saves never hides one.
    /// </summary>
    public static IReadOnlyList<Checkpoint> All(SaveKind kind, ulong localPlayerId)
    {
        var mustBeIn = SaveKinds.NeedsYouInIt(kind) ? localPlayerId : 0;
        var latest = SlotStore.Manager.GetLatestSchemaVersion<SerializableRun>();
        var endings = new Dictionary<long, RunEnding>();
        var list = new List<Checkpoint>();
        foreach (var dir in new[] { Dir(kind), OtherDir(kind) }.Distinct())
        {
            if (!SlotStore.Local.DirectoryExists(dir))
            {
                continue;
            }
            foreach (var file in SlotStore.Local.GetFilesInDirectory(dir))
            {
                if (!file.EndsWith(".save", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // label files, and the .backup and .tmp siblings the store writes
                }
                var path = $"{dir}/{file}";
                try
                {
                    var summary = RunSummary.TryParse(SlotStore.Local.ReadFile(path));
                    var ending = RunEnding.None;
                    if (summary != null && !endings.TryGetValue(summary.StartTime, out ending))
                    {
                        ending = endings[summary.StartTime] = SlotStore.EndingOf(summary.StartTime);
                    }
                    var info = ReadInfo(path) ?? new CheckpointInfo(summary == null ? file : CheckpointText.DefaultLabel(summary), 0);
                    list.Add(new Checkpoint(kind, file, path, summary, SlotRules.StateOf(summary, mustBeIn, ending, latest), info));
                }
                catch (Exception ex)
                {
                    // One bad file must not hide the others; it is listed as unreadable so it can be trashed.
                    ModLog.Warn($"Could not read checkpoint {file}: {ex.Message}");
                    list.Add(new Checkpoint(kind, file, path, null, SlotState.Unreadable, new CheckpointInfo(file, 0)));
                }
            }
        }
        return list;
    }

    /// <summary>The checkpoints of one run, latest point first.</summary>
    public static IReadOnlyList<Checkpoint> OfRun(SaveKind kind, long startTime) =>
        All(kind, 0).Where(c => c.Summary?.StartTime == startTime)
            .OrderByDescending(c => c.Summary!.SaveTime).ThenByDescending(c => c.Info.CreatedUnix)
            .ToList();

    private static CheckpointInfo? ReadInfo(string savePath)
    {
        var infoPath = InfoPath(savePath);
        return SlotStore.Local.FileExists(infoPath) ? CheckpointInfo.TryParse(SlotStore.Local.ReadFile(infoPath)) : null;
    }

    /// <summary>
    /// Takes a checkpoint of the game's save of this kind as it is on disk now: the moment the game last
    /// saved (entering the current room, or finishing it). Refused while the game is writing that save.
    /// </summary>
    public static Checkpoint Take(SaveKind kind, string? label)
    {
        if (SlotStore.Manager.CurrentRunSaveTask is { IsCompleted: false })
        {
            throw new InvalidOperationException("The game is saving. Try again in a moment.");
        }
        if (SlotStore.ActiveDamaged(kind))
        {
            throw new InvalidOperationException("The game's save file is damaged, so there is nothing safe to copy.");
        }
        var text = SlotStore.ReadActive(kind)
            ?? throw new InvalidOperationException("The game has not saved this run yet. It saves on entering each room.");
        var run = RunSummary.TryParse(text)!;
        return Save(kind, text, run, CheckpointInfo.Clean(label, CheckpointText.DefaultLabel(run)));
    }

    /// <summary>
    /// Saves a run's text as a checkpoint with this label. A checkpoint of exactly the same save already
    /// there keeps its file; it takes the new label only when <paramref name="relabel"/> (you took it
    /// again yourself), never for a checkpoint the mod keeps on its own.
    /// </summary>
    public static Checkpoint Save(SaveKind kind, string text, RunSummary run, string label, bool relabel = true)
    {
        var path = SlotStore.WriteCopy(Dir(kind), text, run);
        var existing = ReadInfo(path);
        if (existing != null && !relabel)
        {
            return new Checkpoint(kind, System.IO.Path.GetFileName(path), path, run, SlotState.Ready, existing);
        }
        var created = existing is { CreatedUnix: > 0 } ? existing.CreatedUnix : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var info = new CheckpointInfo(label, created);
        SlotStore.WriteLocal(InfoPath(path), info.ToJson());
        ModLog.Info($"Checkpoint “{label}” of {SaveKinds.Name(kind)} run {run.Key}: {CheckpointText.RestorePoint(run)}");
        return new Checkpoint(kind, System.IO.Path.GetFileName(path), path, run, SlotState.Ready, info);
    }

    /// <summary>
    /// Keeps a save as a checkpoint on the mod's own initiative (going back, or a parked copy taken over),
    /// unless a checkpoint already holds the same run state (equal but for a reload's fields); your own
    /// labels are never changed.
    /// </summary>
    public static void KeepAutomatically(SaveKind kind, string text, RunSummary run, string label)
    {
        if (Holds(kind, text, run))
        {
            return;
        }
        Save(kind, text, run, label, relabel: false);
    }

    /// <summary>True when a checkpoint of this kind holds the same run state as <paramref name="text"/>.</summary>
    public static bool Holds(SaveKind kind, string text, RunSummary run) =>
        All(kind, 0).Any(c => c.Summary is { } held && CheckpointRules.SameMoment(held, run)
            && CheckpointRules.SameSave(SlotStore.Local.ReadFile(c.Path), text));

    /// <summary>Gives a checkpoint a new label.</summary>
    public static string Rename(Checkpoint checkpoint, string? label)
    {
        var clean = CheckpointInfo.Clean(label, checkpoint.Info.Label);
        SlotStore.WriteLocal(InfoPath(checkpoint.Path), (checkpoint.Info with { Label = clean }).ToJson());
        return $"Renamed to “{clean}”.";
    }

    /// <summary>Moves a checkpoint (its save, label and any .backup) into the trash folder. Nothing is deleted.</summary>
    public static string Trash(Checkpoint checkpoint)
    {
        var trash = SlotStore.TrashDir(checkpoint.Kind);
        SlotStore.Local.CreateDirectory(trash);
        var target = SlotStore.FreePath(trash, "checkpoint-" + System.IO.Path.GetFileNameWithoutExtension(checkpoint.FileName), ".save");
        SlotStore.Local.RenameFile(checkpoint.Path, target);
        var info = InfoPath(checkpoint.Path);
        foreach (var (from, to) in new[]
        {
            (info, InfoPath(target)),
            (info + ".backup", InfoPath(target) + ".backup"), // the store's copy of the label before a rename
            (checkpoint.Path + ".backup", target + ".backup"),
        })
        {
            if (SlotStore.Local.FileExists(from))
            {
                SlotStore.Local.RenameFile(from, to);
            }
        }
        return $"Moved “{checkpoint.Info.Label}” to the trash folder.";
    }
}
