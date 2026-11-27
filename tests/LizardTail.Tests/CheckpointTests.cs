using LizardTail.LizardTailCode.Core;
using Xunit;

namespace LizardTail.Tests;

public class CheckpointTests
{
    private static RunSummary Run(long start = 1, long saved = 10, int floor = 17, string? room = "boss", string? finished = null) =>
        new(start, saved, 600, 0, "standard", false, 0, ["ACT.UNDERDOCKS"], floor, [], 20) { RoomType = room, FinishedRoomType = finished };

    [Fact]
    public void ASaveMadeAfterAFightIsFoundAndNamed()
    {
        var run = RunSummary.TryParse("""
            {"start_time":1,"save_time":2,"map_point_history":[[{"map_point_type":"monster"},{"map_point_type":"boss"}]],
             "pre_finished_room":{"room_type":"boss","is_pre_finished":true}}
            """)!;

        Assert.Equal("boss", run.RoomType);
        Assert.Equal("boss", run.FinishedRoomType);
        Assert.Equal("Floor 2, after the boss fight, rewards waiting", CheckpointText.RestorePoint(run));
    }

    // Shaped like the game's save on entering a room: it is written before the room joins the history,
    // so visited_map_coords (kept per act) is one ahead, and the map says what the new room is.
    private const string EnteringTheBoss = """
        {"start_time":1,"current_act_index":0,
         "acts":[{"id":"ACT.UNDERDOCKS","saved_map":{
           "points":[{"coord":{"col":3,"row":0},"type":"monster"},{"coord":{"col":3,"row":1},"type":"rest_site"}],
           "boss":{"coord":{"col":3,"row":2},"type":"boss"},
           "start":{"coord":{"col":3,"row":-1},"type":"ancient"}}}],
         "visited_map_coords":[{"col":3,"row":0},{"col":3,"row":1},{"col":3,"row":2}],
         "map_point_history":[[{"map_point_type":"monster"},{"map_point_type":"rest_site"}]],
         "pre_finished_room":null}
        """;

    [Fact]
    public void ASaveMadeOnEnteringARoomIsNamedByTheRoomBeingEntered()
    {
        var run = RunSummary.TryParse(EnteringTheBoss)!;

        Assert.Equal("boss", run.RoomType);
        Assert.Equal(3, run.Floor);
        Assert.Equal("Floor 3, entering the boss fight", CheckpointText.RestorePoint(run));
    }

    [Fact]
    public void TheFirstRoomOfANewActIsFoundBeforeTheActHasAHistory()
    {
        // The new act's history list does not exist yet; visited coordinates restart with each act.
        var run = RunSummary.TryParse("""
            {"start_time":1,"current_act_index":1,
             "acts":[{"id":"ACT.A"},{"id":"ACT.B","saved_map":{"points":[],"start":{"coord":{"col":3,"row":0},"type":"ancient"}}}],
             "visited_map_coords":[{"col":3,"row":0}],
             "map_point_history":[[{"map_point_type":"monster"},{"map_point_type":"boss"}]],
             "pre_finished_room":null}
            """)!;

        Assert.Equal("Floor 3, meeting the Ancient", CheckpointText.RestorePoint(run));
    }

    [Fact]
    public void AnUnknownCoordinateStillCountsTheFloor()
    {
        var run = RunSummary.TryParse(EnteringTheBoss.Replace("\"row\":2},\"type\":\"boss\"", "\"row\":9},\"type\":\"boss\""))!;

        Assert.Equal("Floor 3, entering a room", CheckpointText.RestorePoint(run));
    }

    [Fact]
    public void ASaveAfterTheFightIsNotMovedAhead()
    {
        // Once the fight is won, the room is in the history and visited and history agree.
        var run = RunSummary.TryParse("""
            {"start_time":1,"current_act_index":0,
             "visited_map_coords":[{"col":3,"row":0},{"col":3,"row":1}],
             "map_point_history":[[{"map_point_type":"monster"},{"map_point_type":"elite"}]],
             "pre_finished_room":{"room_type":"elite","is_pre_finished":true}}
            """)!;

        Assert.Equal("Floor 2, after the elite fight, rewards waiting", CheckpointText.RestorePoint(run));
    }

    [Fact]
    public void TheVeryStartOfARunSaysSo()
    {
        Assert.Equal("The start of the run", CheckpointText.RestorePoint(RunSummary.TryParse("""{"start_time":1}""")!));
    }

    [Fact]
    public void AReloadIsTheSameSaveButAnotherBranchIsNot()
    {
        const string original = """{"start_time":1,"run_time":1020,"save_time":50,"num_reloads":0,"players":[{"relics":["RELIC.X"]}]}""";
        const string reloaded = """{"num_reloads":1,"save_time":77,"players":[{"relics":["RELIC.X"]}],"run_time":1020,"start_time":1}""";
        const string otherBranch = """{"start_time":1,"run_time":1020,"save_time":50,"num_reloads":0,"players":[{"relics":["RELIC.Y"]}]}""";

        Assert.True(CheckpointRules.SameSave(original, reloaded));
        Assert.False(CheckpointRules.SameSave(original, otherBranch));
    }

    [Theory]
    [InlineData(null, "{}")]
    [InlineData("not json", "{}")]
    [InlineData("[]", "{}")]
    public void UnreadableSavesAreNeverTheSame(string? a, string b)
    {
        Assert.False(CheckpointRules.SameSave(a, b));
    }

    [Fact]
    public void AReloadedSaveIsTheSameMoment()
    {
        // Continue raises num_reloads and multiplayer Load sets a new save time; the run's clock does not move.
        var before = Run(saved: 10) with { };
        var reloaded = before with { SaveTime = 99 };
        var later = before with { RunTimeSeconds = 900 };

        Assert.True(CheckpointRules.SameMoment(before, reloaded));
        Assert.False(CheckpointRules.SameMoment(before, later));
        Assert.False(CheckpointRules.SameMoment(before, before with { FinishedRoomType = "boss" }));
    }

    [Theory]
    [InlineData("ancient", "event", "Floor 17, after the Ancient")]
    [InlineData("unknown", "event", "Floor 17, after the event")]
    [InlineData("elite", "elite", "Floor 17, after the elite fight, rewards waiting")]
    [InlineData("ancient", null, "Floor 17, meeting the Ancient")]
    [InlineData("rest_site", null, "Floor 17, entering a rest site")]
    [InlineData(null, null, "Floor 17, entering a room")]
    public void RestorePointsReadNaturally(string? room, string? finished, string expected)
    {
        Assert.Equal(expected, CheckpointText.RestorePoint(Run(room: room, finished: finished)));
    }

    [Fact]
    public void LabelsRoundTrip()
    {
        var info = new CheckpointInfo("Before the boss relic “pick”", 1700000000);

        Assert.Equal(info, CheckpointInfo.TryParse(info.ToJson()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("""{"created":5}""")]
    [InlineData("not json")]
    public void AnythingThatIsNotALabelFileIsNull(string? json)
    {
        Assert.Null(CheckpointInfo.TryParse(json));
    }

    [Fact]
    public void TypedLabelsAreKeptToOneTidyLine()
    {
        Assert.Equal("Took the relic", CheckpointInfo.Clean("  Took \n the\trelic  ", "x"));
        Assert.Equal("fallback", CheckpointInfo.Clean("   ", "fallback"));
        Assert.Equal(CheckpointInfo.MaxLabelLength, CheckpointInfo.Clean(new string('a', 200), "x").Length);
    }

    [Fact]
    public void WonLostAndAbandonedRunsCanStillBeRestored()
    {
        Assert.True(CheckpointRules.CanRestore(SlotState.Ready));
        Assert.True(CheckpointRules.CanRestore(SlotState.Abandoned));
        Assert.True(CheckpointRules.CanRestore(SlotState.Finished));
        Assert.False(CheckpointRules.CanRestore(SlotState.NotYourRun));
        Assert.False(CheckpointRules.CanRestore(SlotState.NewerVersion));
        Assert.False(CheckpointRules.CanRestore(SlotState.Unreadable));
    }

    [Fact]
    public void EveryBlockedOrUnusualStateExplainsItself()
    {
        foreach (var state in Enum.GetValues<SlotState>().Where(s => s != SlotState.Ready))
        {
            Assert.False(string.IsNullOrWhiteSpace(CheckpointText.StateNote(state)));
        }
    }

    [Fact]
    public void TheActiveRunsCheckpointsComeFirstLatestPointFirst()
    {
        var items = new (string Name, RunSummary? Run, long Created)[]
        {
            ("other-old", Run(start: 2, saved: 50), 1),
            ("active-early", Run(start: 1, saved: 10), 2),
            ("junk", null, 9),
            ("active-late", Run(start: 1, saved: 30), 3),
            ("other-new", Run(start: 3, saved: 90), 4),
        };

        var ordered = CheckpointRules.Ordered(items, i => i.Run, i => i.Created, activeStart: 1).Select(i => i.Name);

        Assert.Equal(["active-late", "active-early", "other-new", "other-old", "junk"], ordered);
    }

    [Fact]
    public void AutomaticLabelsSayWhereTheyCameFrom()
    {
        Assert.Equal("Before going back to “Boss relic”", CheckpointText.BeforeGoingBack("Boss relic"));
        Assert.Equal("Parked copy: Floor 17, entering the boss fight", CheckpointText.FromParked(Run()));
        Assert.True(CheckpointText.BeforeGoingBack(new string('x', 200)).Length <= CheckpointInfo.MaxLabelLength);
    }
}
