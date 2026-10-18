using LizardTail.LizardTailCode.Core;
using Xunit;

namespace LizardTail.Tests;

public class ShadowLibraryTests
{
    private const string Ironclad = "CHARACTER.IRONCLAD";

    private static ShadowRun Run(string seed, long startTime, int floors, bool win = false,
        bool abandoned = false, string character = Ironclad)
    {
        var list = new List<ShadowFloor>();
        for (var i = 0; i < floors; i++)
        {
            list.Add(new ShadowFloor(0, i, "monster",
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<string>(), Array.Empty<string>()));
        }

        return new ShadowRun(seed, startTime, 0, 10, "custom", character,
            new[] { character }, win, abandoned, null, list);
    }

    [Theory]
    [InlineData("artENG68z5su", "ARTENG68Z5SU")]
    [InlineData("  artengo8i5su  ", "ARTENG0815SU")]
    public void SeedsAreComparedTheWayTheGameCanonicalizesThem(string typed, string expected)
    {
        // The game upper-cases and reads O as zero and I as one before hashing, so two spellings
        // of the same seed are the same run and have to match here too.
        Assert.Equal(expected, ShadowSeed.Canonical(typed));
    }

    [Fact]
    public void AnEmptySeedNeverMatchesAnything()
    {
        Assert.False(ShadowSeed.Same("", ""));
        Assert.False(ShadowSeed.Same(null, null));
    }

    [Fact]
    public void OnlyRunsOfTheSameSeedAndCharacterAreOffered()
    {
        var all = new[]
        {
            Run("AAA", 1, 10),
            Run("BBB", 2, 20),
            Run("AAA", 3, 30, character: "CHARACTER.SILENT"),
        };

        var library = ShadowLibrary.For(all, "aaa", Ironclad);

        Assert.Single(library.Runs);
        Assert.Equal(1, library.Current!.StartTime);
    }

    [Fact]
    public void ARunThatRecordedNoFloorsIsLeftOutOfThePicker()
    {
        var library = ShadowLibrary.For(new[] { Run("AAA", 1, 0), Run("AAA", 2, 5) }, "AAA", Ironclad);

        Assert.Single(library.Runs);
        Assert.Equal(2, library.Current!.StartTime);
    }

    [Fact]
    public void ThePickerListsRunsOldestFirst()
    {
        var library = ShadowLibrary.For(new[] { Run("AAA", 30, 3), Run("AAA", 10, 3), Run("AAA", 20, 3) }, "AAA", Ironclad);

        Assert.Equal(new long[] { 10, 20, 30 }, library.Runs.Select(r => r.StartTime));
    }

    [Fact]
    public void ItOpensOnTheRunThatGotFurthestRatherThanTheFirstOne()
    {
        // A replayed seed is mostly false starts. Opening on the first run would usually shadow a
        // run that was abandoned on floor one.
        var library = ShadowLibrary.For(
            new[] { Run("AAA", 1, 7, abandoned: true), Run("AAA", 2, 1, abandoned: true), Run("AAA", 3, 43) },
            "AAA", Ironclad);

        Assert.Equal(43, library.Current!.FloorCount);
    }

    [Fact]
    public void AWinBreaksATieOnDepth()
    {
        var library = ShadowLibrary.For(new[] { Run("AAA", 1, 49), Run("AAA", 2, 49, win: true) }, "AAA", Ironclad);

        Assert.True(library.Current!.Win);
    }

    [Fact]
    public void TheEarliestRunBreaksATieOnDepthAndOutcome()
    {
        var library = ShadowLibrary.For(new[] { Run("AAA", 20, 45, win: true), Run("AAA", 10, 45, win: true) }, "AAA", Ironclad);

        Assert.Equal(10, library.Current!.StartTime);
    }

    [Fact]
    public void YouCanPickAnyRunInTheListIncludingTheEarliestAndTheLatest()
    {
        var library = ShadowLibrary.For(
            new[] { Run("AAA", 10, 5), Run("AAA", 20, 50), Run("AAA", 30, 9) }, "AAA", Ironclad);

        Assert.Equal(20, library.Current!.StartTime);          // the default, deepest
        Assert.Equal(10, library.Select(0).Current!.StartTime); // earliest
        Assert.Equal(30, library.Select(2).Current!.StartTime); // latest
    }

    [Fact]
    public void SelectingOutsideTheListLeavesTheChoiceAlone()
    {
        var library = ShadowLibrary.For(new[] { Run("AAA", 10, 5) }, "AAA", Ironclad);

        Assert.Same(library, library.Select(-1));
        Assert.Same(library, library.Select(9));
    }

    [Fact]
    public void SteppingCyclesThroughTheRunsInBothDirections()
    {
        var library = ShadowLibrary.For(
            new[] { Run("AAA", 10, 5), Run("AAA", 20, 50), Run("AAA", 30, 9) }, "AAA", Ironclad);

        Assert.Equal(30, library.Step(1).Current!.StartTime);
        Assert.Equal(10, library.Step(1).Step(1).Current!.StartTime);
        Assert.Equal(10, library.Step(-1).Current!.StartTime);
    }

    [Fact]
    public void SteppingASingleRunLibraryDoesNothing()
    {
        var library = ShadowLibrary.For(new[] { Run("AAA", 10, 5) }, "AAA", Ironclad);

        Assert.Same(library, library.Step(1));
    }

    [Fact]
    public void ASeedYouHaveNeverPlayedGivesAnEmptyLibrary()
    {
        var library = ShadowLibrary.For(new[] { Run("AAA", 1, 10) }, "ZZZ", Ironclad);

        Assert.False(library.HasAny);
        Assert.Null(library.Current);
        Assert.Contains("Nothing to shadow", ShadowText.Loaded(library));
    }

    [Fact]
    public void APickerRowSaysWhenTheRunWasPlayedHowFarItGotAndHowItEnded()
    {
        var row = ShadowText.Row(Run("AAA", 1792285479, 43, win: true));

        Assert.Contains("floor 43", row);
        Assert.Contains("Won", row);
    }

    [Fact]
    public void ThePositionLineIsHiddenWhenThereIsOnlyOneRun()
    {
        var one = ShadowLibrary.For(new[] { Run("AAA", 1, 5) }, "AAA", Ironclad);
        var two = ShadowLibrary.For(new[] { Run("AAA", 1, 5), Run("AAA", 2, 6) }, "AAA", Ironclad);

        Assert.Equal("", ShadowText.Position(one));
        Assert.Equal("Run 2 of 2", ShadowText.Position(two));
    }
}
