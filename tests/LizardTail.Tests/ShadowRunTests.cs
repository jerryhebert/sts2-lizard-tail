using LizardTail.LizardTailCode.Core;
using Xunit;

namespace LizardTail.Tests;

public class ShadowRunTests
{
    /// <summary>
    /// A trimmed history record in the game's own shape: two floors, one card reward taken out of
    /// three offered, a relic, and a campfire. Field names and nesting match a real .run file.
    /// </summary>
    private const string Record = """
    {
      "seed": "ARTENG68Z5SU",
      "start_time": 1792285479,
      "run_time": 4399,
      "ascension": 10,
      "game_mode": "custom",
      "win": false,
      "was_abandoned": false,
      "killed_by_encounter": "ENCOUNTER.KAISER_CRAB_BOSS",
      "killed_by_event": "NONE.NONE",
      "players": [ { "id": 1, "character": "CHARACTER.IRONCLAD" } ],
      "map_point_history": [
        [
          {
            "map_point_type": "monster",
            "player_stats": [ {
              "player_id": 1,
              "card_choices": [
                { "card": { "id": "CARD.POMMEL_STRIKE" }, "was_picked": true },
                { "card": { "id": "CARD.HEADBUTT" }, "was_picked": false },
                { "card": { "id": "CARD.TREMBLE" }, "was_picked": false }
              ],
              "potion_choices": [ { "choice": "POTION.SPEED_POTION", "was_picked": true } ]
            } ]
          },
          {
            "map_point_type": "rest_site",
            "player_stats": [ {
              "player_id": 1,
              "rest_site_choices": [ "SMITH" ],
              "upgraded_cards": [ "CARD.POMMEL_STRIKE" ],
              "relic_choices": [ { "choice": "RELIC.WAR_PAINT", "was_picked": false } ]
            } ]
          }
        ],
        [
          {
            "map_point_type": "treasure",
            "player_stats": [ {
              "player_id": 1,
              "relic_choices": [ { "choice": "RELIC.BAG_OF_PREPARATION", "was_picked": true } ]
            } ]
          }
        ]
      ]
    }
    """;

    [Fact]
    public void ReadsTheSeedAndHowTheRunEnded()
    {
        var run = ShadowRun.TryParse(Record);

        Assert.NotNull(run);
        Assert.Equal("ARTENG68Z5SU", run!.Seed);
        Assert.Equal(1792285479, run.StartTime);
        Assert.Equal("CHARACTER.IRONCLAD", run.Character);
        Assert.Equal("Killed by Kaiser Crab Boss", run.Outcome);
    }

    [Fact]
    public void CountsEveryFloorAcrossEveryAct()
    {
        var run = ShadowRun.TryParse(Record)!;

        Assert.Equal(3, run.FloorCount);
    }

    [Fact]
    public void AFloorsRowIsItsPositionInItsAct()
    {
        // The row is not stored anywhere: it is the index, which is how the game itself looks a
        // floor up. Getting this wrong would shadow the right choices onto the wrong floor.
        var run = ShadowRun.TryParse(Record)!;

        Assert.Equal("monster", run.FloorAt(0, 0)!.PointType);
        Assert.Equal("rest_site", run.FloorAt(0, 1)!.PointType);
        Assert.Equal("treasure", run.FloorAt(1, 0)!.PointType);
    }

    [Fact]
    public void KeepsOnlyTheCardThatWasActuallyTaken()
    {
        var floor = ShadowRun.TryParse(Record)!.FloorAt(0, 0)!;

        Assert.Equal(new[] { "CARD.POMMEL_STRIKE" }, floor.PickedCards);
        Assert.Equal(3, floor.OfferedCards.Count);
        Assert.Contains("CARD.TREMBLE", floor.OfferedCards);
    }

    [Fact]
    public void ReadsRelicsPotionsCampfiresAndUpgrades()
    {
        var run = ShadowRun.TryParse(Record)!;

        Assert.Equal(new[] { "POTION.SPEED_POTION" }, run.FloorAt(0, 0)!.PickedPotions);
        Assert.Equal(new[] { "SMITH" }, run.FloorAt(0, 1)!.RestSiteChoices);
        Assert.Equal(new[] { "CARD.POMMEL_STRIKE" }, run.FloorAt(0, 1)!.UpgradedCards);
        Assert.Equal(new[] { "RELIC.BAG_OF_PREPARATION" }, run.FloorAt(1, 0)!.PickedRelics);
    }

    [Fact]
    public void ARelicOfferedButDeclinedIsNotTreatedAsTaken()
    {
        Assert.Empty(ShadowRun.TryParse(Record)!.FloorAt(0, 1)!.PickedRelics);
    }

    [Fact]
    public void AFloorTheRunNeverReachedIsNullRatherThanAnError()
    {
        Assert.Null(ShadowRun.TryParse(Record)!.FloorAt(0, 40));
        Assert.Null(ShadowRun.TryParse(Record)!.FloorAt(2, 0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "start_time": 1 }""")]
    public void AnythingThatIsNotAHistoryRecordParsesToNull(string? json)
    {
        Assert.Null(ShadowRun.TryParse(json));
    }

    [Fact]
    public void AMultiplayerRecordKeepsOnlyYourOwnChoices()
    {
        // Each player has their own stats block on every floor. Shadowing a teammate's picks would
        // be worse than showing nothing, so the block is chosen by player id, not by position.
        const string coop = """
        {
          "seed": "GLJ1UX4H28E9", "start_time": 10, "win": false, "was_abandoned": false,
          "players": [
            { "id": 1, "character": "CHARACTER.IRONCLAD" },
            { "id": 2, "character": "CHARACTER.SILENT" }
          ],
          "map_point_history": [ [ {
            "map_point_type": "monster",
            "player_stats": [
              { "player_id": 1, "card_choices": [ { "card": { "id": "CARD.CLEAVE" }, "was_picked": true } ] },
              { "player_id": 2, "card_choices": [ { "card": { "id": "CARD.NEUTRALIZE" }, "was_picked": true } ] }
            ]
          } ] ]
        }
        """;

        var asSilent = ShadowRun.TryParse(coop, "CHARACTER.SILENT");

        Assert.NotNull(asSilent);
        Assert.True(asSilent!.IsMultiplayer);
        Assert.Equal(new[] { "CARD.NEUTRALIZE" }, asSilent.FloorAt(0, 0)!.PickedCards);
    }

    [Fact]
    public void ARunWithoutTheCharacterYouArePlayingIsNotOffered()
    {
        Assert.Null(ShadowRun.TryParse(Record, "CHARACTER.DEFECT"));
    }

    [Fact]
    public void AnAbandonedRunSaysSoRatherThanNamingAKiller()
    {
        const string abandoned = """
        {
          "seed": "AAA", "start_time": 1, "win": false, "was_abandoned": true,
          "killed_by_encounter": "NONE.NONE",
          "players": [ { "id": 1, "character": "CHARACTER.REGENT" } ],
          "map_point_history": []
        }
        """;

        Assert.Equal("Abandoned", ShadowRun.TryParse(abandoned)!.Outcome);
    }
}
