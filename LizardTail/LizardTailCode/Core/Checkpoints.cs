using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LizardTail.LizardTailCode.Core;

/// <summary>
/// What the mod keeps about a checkpoint besides the save itself: the label you gave it and when it was
/// taken. Stored as a small JSON file beside the checkpoint's save.
/// </summary>
public sealed record CheckpointInfo(string Label, long CreatedUnix)
{
    /// <summary>Long enough for a note like "Before taking Ectoplasm over Runic Dome".</summary>
    public const int MaxLabelLength = 80;

    public string ToJson() =>
        JsonSerializer.Serialize(new Dictionary<string, object> { ["label"] = Label, ["created"] = CreatedUnix });

    /// <summary>Null when the text is not a checkpoint label file.</summary>
    public static CheckpointInfo? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("label", out var label) || label.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            var created = root.TryGetProperty("created", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt64(out var n) ? n : 0;
            return new CheckpointInfo(label.GetString() ?? "", created);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A label as typed, made safe to show on one line: whitespace and line breaks collapsed to single
    /// spaces, trimmed, and cut to <see cref="MaxLabelLength"/>. Empty becomes <paramref name="fallback"/>.
    /// </summary>
    public static string Clean(string? label, string fallback)
    {
        var text = new StringBuilder();
        foreach (var ch in label ?? "")
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
            {
                if (text.Length > 0 && text[^1] != ' ')
                {
                    text.Append(' ');
                }
            }
            else
            {
                text.Append(ch);
            }
        }
        var clean = text.ToString().Trim();
        if (clean.Length == 0)
        {
            return fallback;
        }
        return clean.Length <= MaxLabelLength ? clean : clean[..MaxLabelLength].TrimEnd();
    }
}

/// <summary>What can be restored, and in which order checkpoints are listed.</summary>
public static class CheckpointRules
{
    /// <summary>
    /// A checkpoint can be restored when the game can load it. Unlike a parked run, a run that was won,
    /// lost or abandoned since can be restored too: going back to try the other choice is the point.
    /// </summary>
    public static bool CanRestore(SlotState state) => state is SlotState.Ready or SlotState.Abandoned or SlotState.Finished;

    /// <summary>
    /// A cheap first test for <see cref="SameSave"/>: the same run, clock, floor and room state. Not
    /// enough on its own: two branches can reach the same kind of room at the same second.
    /// </summary>
    public static bool SameMoment(RunSummary a, RunSummary b) =>
        a.StartTime == b.StartTime && a.RunTimeSeconds == b.RunTimeSeconds && a.Floor == b.Floor
        && a.RoomType == b.RoomType && a.FinishedRoomType == b.FinishedRoomType;

    /// <summary>The fields a reload rewrites without changing the run: Continue raises num_reloads, and
    /// multiplayer's Load sets a new save_time.</summary>
    private static readonly string[] ReloadFields = ["save_time", "num_reloads"];

    /// <summary>
    /// Whether two save texts hold the same run state: equal apart from <see cref="ReloadFields"/>.
    /// Anything else differing (another branch's deck, relics, map) makes them different. When in doubt
    /// (unreadable text, or a reload that reformats other fields) the answer is "different", which at
    /// worst keeps a duplicate.
    /// </summary>
    public static bool SameSave(string? a, string? b)
    {
        if (a == null || b == null)
        {
            return false;
        }
        if (a == b)
        {
            return true;
        }
        try
        {
            if (JsonNode.Parse(a) is not JsonObject left || JsonNode.Parse(b) is not JsonObject right)
            {
                return false;
            }
            foreach (var field in ReloadFields)
            {
                left.Remove(field);
                right.Remove(field);
            }
            return JsonNode.DeepEquals(left, right);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Checkpoints in the order the panel lists them: the active run's first, then the other runs by
    /// their latest checkpoint, and within a run the latest point in the run first. Unreadable files last.
    /// </summary>
    public static IReadOnlyList<T> Ordered<T>(IEnumerable<T> items, Func<T, RunSummary?> summary, Func<T, long> created, long? activeStart)
    {
        var list = items.ToList();
        var readable = list.Where(i => summary(i) != null)
            .GroupBy(i => summary(i)!.StartTime)
            .OrderByDescending(g => g.Key == activeStart)
            .ThenByDescending(g => g.Max(i => summary(i)!.SaveTime))
            .SelectMany(g => g.OrderByDescending(i => summary(i)!.SaveTime).ThenByDescending(created));
        return readable.Concat(list.Where(i => summary(i) == null).OrderByDescending(created)).ToList();
    }
}

/// <summary>The words for a checkpoint: where in the run it goes back to.</summary>
public static class CheckpointText
{
    /// <summary>
    /// "Floor 17, after the boss fight, rewards waiting" or "Floor 18, entering an elite fight": the
    /// game saves on entering a room and again once a fight is won or an Ancient chosen, so a checkpoint
    /// goes back to one of those moments.
    /// </summary>
    public static string RestorePoint(RunSummary run)
    {
        string phrase;
        if (run.FinishedRoomType is { } finished)
        {
            phrase = finished switch
            {
                "monster" => "after the fight, rewards waiting",
                "elite" => "after the elite fight, rewards waiting",
                "boss" => "after the boss fight, rewards waiting",
                "event" when run.RoomType == "ancient" => "after the Ancient",
                "event" => "after the event",
                _ => "after the room",
            };
        }
        else
        {
            phrase = run.RoomType switch
            {
                "monster" => "entering a fight",
                "elite" => "entering an elite fight",
                "boss" => "entering the boss fight",
                "treasure" => "entering a treasure room",
                "shop" => "entering a shop",
                "rest_site" => "entering a rest site",
                "unknown" => "entering an unknown room",
                "ancient" => "meeting the Ancient",
                _ => "entering a room",
            };
        }
        return run.Floor == 0 && run.FinishedRoomType == null && run.RoomType == null ? "The start of the run" : $"Floor {run.Floor}, {phrase}";
    }

    /// <summary>The label a new checkpoint gets unless you type one.</summary>
    public static string DefaultLabel(RunSummary run) => RestorePoint(run);

    /// <summary>The label of the checkpoint kept automatically when you go back to another one.</summary>
    public static string BeforeGoingBack(string label) =>
        CheckpointInfo.Clean($"Before going back to “{label}”", "Before going back");

    /// <summary>The label of the game's previous save of the branch being left, kept beside it.</summary>
    public static string BeforeGoingBackEarlier(string label) =>
        CheckpointInfo.Clean($"Before going back to \u201c{label}\u201d, one save earlier", "Before going back, one save earlier");

    /// <summary>The label of a parked copy turned into a checkpoint, so going back cannot clear it.</summary>
    public static string FromParked(RunSummary run) =>
        CheckpointInfo.Clean($"Parked copy: {RestorePoint(run)}", "Parked copy");

    /// <summary>Why a checkpoint row cannot be restored, or what restoring it will also do.</summary>
    public static string? StateNote(SlotState state) => state switch
    {
        SlotState.Finished => "This run has ended since. Restoring moves its result aside so the save is kept; the game's statistics keep counting the first result.",
        SlotState.Abandoned => "This run was abandoned since. Restoring picks it up again.",
        _ => SlotText.StateNote(state),
    };
}
