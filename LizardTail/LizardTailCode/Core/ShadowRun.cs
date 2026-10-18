using System.Globalization;
using System.Text.Json;

namespace LizardTail.LizardTailCode.Core;

/// <summary>
/// What one player did on one floor of a finished run, as the game recorded it.
///
/// The floor's position in its act is the row: the game itself indexes history that way, so
/// <see cref="Row"/> is the list index rather than anything stored in the file. There is no column,
/// which is why the map can be shadowed by row but not yet by node.
/// </summary>
public sealed record ShadowFloor(
    int ActIndex,
    int Row,
    string PointType,
    IReadOnlyList<string> PickedCards,
    IReadOnlyList<string> OfferedCards,
    IReadOnlyList<string> PickedRelics,
    IReadOnlyList<string> PickedPotions,
    IReadOnlyList<string> RestSiteChoices,
    IReadOnlyList<string> UpgradedCards)
{
    public bool IsEmpty =>
        PickedCards.Count == 0 && PickedRelics.Count == 0 && PickedPotions.Count == 0
        && RestSiteChoices.Count == 0 && UpgradedCards.Count == 0;
}

/// <summary>
/// A finished run, read from one of the game's own history/&lt;start_time&gt;.run files.
///
/// Read with JsonDocument rather than the game's serializer, for the same reasons
/// <see cref="RunSummary"/> does: it survives a schema change and has no side effects on the file.
/// </summary>
public sealed record ShadowRun(
    string Seed,
    long StartTime,
    int RunTimeSeconds,
    int Ascension,
    string GameMode,
    string Character,
    IReadOnlyList<string> AllCharacters,
    bool Win,
    bool Abandoned,
    string? KilledBy,
    IReadOnlyList<ShadowFloor> Floors)
{
    /// <summary>How far the run got: the number of floors it has any record of.</summary>
    public int FloorCount => Floors.Count;

    /// <summary>Also the name of the file this came from.</summary>
    public string Key => StartTime.ToString(CultureInfo.InvariantCulture);

    public bool IsMultiplayer => AllCharacters.Count > 1;

    /// <summary>Null when the run never reached that floor, which is normal near the end of a shadow.</summary>
    public ShadowFloor? FloorAt(int actIndex, int row) =>
        Floors.FirstOrDefault(f => f.ActIndex == actIndex && f.Row == row);

    /// <summary>How the run ended, for the picker: "Won", "Abandoned", or what killed you.</summary>
    public string Outcome =>
        Win ? "Won"
        : Abandoned ? "Abandoned"
        : string.IsNullOrEmpty(KilledBy) ? "Lost"
        : "Killed by " + Pretty(KilledBy);

    /// <summary>Turns ENCOUNTER.KAISER_CRAB_BOSS into "Kaiser Crab Boss".</summary>
    internal static string Pretty(string modelId)
    {
        var tail = modelId.Contains('.') ? modelId[(modelId.LastIndexOf('.') + 1)..] : modelId;
        var words = tail.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant());
        return string.Join(' ', words);
    }

    /// <summary>
    /// Parses one history record, keeping the choices of the player with the given character.
    ///
    /// A co-op record holds one set of choices per player, and shadowing someone else's picks would
    /// be worse than showing nothing: when no player matches, this returns null rather than guess.
    /// </summary>
    public static ShadowRun? TryParse(string? json, string? forCharacter = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var seed = Str(root, "seed");
            if (string.IsNullOrWhiteSpace(seed) || Long(root, "start_time") is not { } startTime)
            {
                return null;
            }

            var characters = new List<string>();
            var playerIds = new List<int>();
            if (root.TryGetProperty("players", out var players) && players.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in players.EnumerateArray())
                {
                    characters.Add(Str(p, "character") ?? "");
                    playerIds.Add((int)(Long(p, "id") ?? 0));
                }
            }
            if (characters.Count == 0)
            {
                return null;
            }

            var index = forCharacter == null
                ? 0
                : characters.FindIndex(c => string.Equals(c, forCharacter, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return null;
            }

            var playerId = playerIds[index];
            var floors = ReadFloors(root, playerId);

            return new ShadowRun(
                Seed: seed!,
                StartTime: startTime,
                RunTimeSeconds: (int)(Long(root, "run_time") ?? 0),
                Ascension: (int)(Long(root, "ascension") ?? 0),
                GameMode: Str(root, "game_mode") ?? "standard",
                Character: characters[index],
                AllCharacters: characters,
                Win: Bool(root, "win"),
                Abandoned: Bool(root, "was_abandoned"),
                KilledBy: Nothing(Str(root, "killed_by_encounter")) ?? Nothing(Str(root, "killed_by_event")),
                Floors: floors);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<ShadowFloor> ReadFloors(JsonElement root, int playerId)
    {
        var floors = new List<ShadowFloor>();
        if (!root.TryGetProperty("map_point_history", out var acts) || acts.ValueKind != JsonValueKind.Array)
        {
            return floors;
        }

        var actIndex = 0;
        foreach (var act in acts.EnumerateArray())
        {
            if (act.ValueKind != JsonValueKind.Array)
            {
                actIndex++;
                continue;
            }

            var row = 0;
            foreach (var point in act.EnumerateArray())
            {
                var stats = StatsFor(point, playerId);
                floors.Add(new ShadowFloor(
                    ActIndex: actIndex,
                    Row: row,
                    PointType: Str(point, "map_point_type") ?? "",
                    PickedCards: Chosen(stats, "card_choices", "card", "id", "was_picked"),
                    OfferedCards: Offered(stats, "card_choices", "card", "id"),
                    PickedRelics: Chosen(stats, "relic_choices", null, "choice", "was_picked"),
                    PickedPotions: Chosen(stats, "potion_choices", null, "choice", "was_picked"),
                    RestSiteChoices: Strings(stats, "rest_site_choices"),
                    UpgradedCards: Strings(stats, "upgraded_cards")));
                row++;
            }
            actIndex++;
        }

        return floors;
    }

    /// <summary>The stats block belonging to one player, or nothing when this floor has none.</summary>
    private static JsonElement? StatsFor(JsonElement point, int playerId)
    {
        if (!point.TryGetProperty("player_stats", out var all) || all.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var s in all.EnumerateArray())
        {
            if (Long(s, "player_id") == playerId)
            {
                return s;
            }
        }

        // Single-player records always match on id; this only catches a record written before the
        // field existed, where there is exactly one block and it can only be yours.
        var first = all.EnumerateArray().FirstOrDefault();
        return all.GetArrayLength() == 1 ? first : null;
    }

    /// <summary>Ids from a choice array where the flag says it was taken.</summary>
    private static List<string> Chosen(JsonElement? stats, string array, string? wrapper, string idField, string flag)
    {
        var result = new List<string>();
        if (stats is not { } s || !s.TryGetProperty(array, out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (!Bool(item, flag))
            {
                continue;
            }

            var holder = wrapper == null ? item
                : item.TryGetProperty(wrapper, out var w) ? w : default;
            if (Str(holder, idField) is { } id && id.Length > 0)
            {
                result.Add(id);
            }
        }

        return result;
    }

    /// <summary>Every id a choice array offered, taken or not.</summary>
    private static List<string> Offered(JsonElement? stats, string array, string? wrapper, string idField)
    {
        var result = new List<string>();
        if (stats is not { } s || !s.TryGetProperty(array, out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in items.EnumerateArray())
        {
            var holder = wrapper == null ? item
                : item.TryGetProperty(wrapper, out var w) ? w : default;
            if (Str(holder, idField) is { } id && id.Length > 0)
            {
                result.Add(id);
            }
        }

        return result;
    }

    private static List<string> Strings(JsonElement? stats, string array)
    {
        var result = new List<string>();
        if (stats is not { } s || !s.TryGetProperty(array, out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } v)
            {
                result.Add(v);
            }
        }

        return result;
    }

    private static string? Nothing(string? id) =>
        string.IsNullOrEmpty(id) || id.EndsWith(".NONE", StringComparison.Ordinal) || id == "NONE" ? null : id;

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static long? Long(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt64(out var l)
            ? l
            : null;

    private static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
