using CodeSail.Services;

namespace CodeSail.Tests;

public class ThreeWayMergeTests
{
    private static string Lines(params string[] lines) => string.Join('\n', lines);

    private static readonly string Base = Lines("eins", "zwei", "drei", "vier", "fünf", "sechs", "sieben");

    [Fact]
    public void ChangesInDifferentPlaces_AreCombined()
    {
        var mine = Lines("EINS", "zwei", "drei", "vier", "fünf", "sechs", "sieben");
        var theirs = Lines("eins", "zwei", "drei", "vier", "fünf", "sechs", "SIEBEN");

        var result = ThreeWayMerge.Merge(Base, mine, theirs);

        Assert.Equal(0, result.Conflicts);
        Assert.Equal(Lines("EINS", "zwei", "drei", "vier", "fünf", "sechs", "SIEBEN"), result.Text);
    }

    [Fact]
    public void InsertionsAndDeletions_AreCombined()
    {
        var mine = Lines("eins", "neu", "zwei", "drei", "vier", "fünf", "sechs", "sieben");
        var theirs = Lines("eins", "zwei", "drei", "vier", "sechs", "sieben");

        var result = ThreeWayMerge.Merge(Base, mine, theirs);

        Assert.Equal(0, result.Conflicts);
        Assert.Equal(Lines("eins", "neu", "zwei", "drei", "vier", "sechs", "sieben"), result.Text);
    }

    [Fact]
    public void SameChangeOnBothSides_IsNoConflict()
    {
        var both = Lines("eins", "zwei", "DREI", "vier", "fünf", "sechs", "sieben");

        var result = ThreeWayMerge.Merge(Base, both, both.Replace("sieben", "sieben"));

        Assert.Equal(0, result.Conflicts);
        Assert.Equal(both, result.Text);
    }

    [Fact]
    public void DifferentChangesToSameLine_ProduceConflictMarkers()
    {
        var mine = Lines("eins", "zwei", "drei (meins)", "vier", "fünf", "sechs", "sieben");
        var theirs = Lines("eins", "zwei", "drei (GitHub)", "vier", "fünf", "sechs", "SIEBEN");

        var result = ThreeWayMerge.Merge(Base, mine, theirs, "Ich", "GitHub");

        Assert.Equal(1, result.Conflicts);
        Assert.Equal(Lines(
            "eins", "zwei",
            "<<<<<<< Ich", "drei (meins)", "=======", "drei (GitHub)", ">>>>>>> GitHub",
            "vier", "fünf", "sechs", "SIEBEN"), result.Text);
        Assert.True(ThreeWayMerge.HasConflictMarkers(result.Text));
        Assert.Equal(3, ThreeWayMerge.FirstConflictLine(result.Text));
    }

    [Fact]
    public void AdjacentChanges_AreTreatedAsConflict()
    {
        var mine = Lines("eins", "ZWEI", "drei", "vier", "fünf", "sechs", "sieben");
        var theirs = Lines("eins", "zwei", "DREI", "vier", "fünf", "sechs", "sieben");

        Assert.Equal(1, ThreeWayMerge.Merge(Base, mine, theirs).Conflicts);
    }

    [Fact]
    public void OneSideUnchanged_TakesOtherSide()
    {
        var theirs = Lines("ganz", "anders");

        Assert.Equal(theirs, ThreeWayMerge.Merge(Base, Base, theirs).Text);
        Assert.Equal(theirs, ThreeWayMerge.Merge(Base, theirs, Base).Text);
    }

    [Fact]
    public void KeepsTrailingNewlineAndWindowsLineEndings()
    {
        var b = "a\r\nb\r\nc\r\n";
        var mine = "A\r\nb\r\nc\r\n";
        var theirs = "a\r\nb\r\nC\r\n";

        Assert.Equal("A\r\nb\r\nC\r\n", ThreeWayMerge.Merge(b, mine, theirs).Text);
    }

    [Fact]
    public void BothAppendDifferentLinesAtEnd_IsConflict()
    {
        var result = ThreeWayMerge.Merge(Lines("a"), Lines("a", "mine"), Lines("a", "theirs"));

        Assert.Equal(1, result.Conflicts);
    }

    [Fact]
    public void LargeFilesWithFewChanges_MergeQuickly()
    {
        var b = Enumerable.Range(0, 5000).Select(i => $"Zeile {i}").ToArray();
        var mine = (string[])b.Clone();
        var theirs = (string[])b.Clone();
        mine[10] = "meins";
        theirs[4900] = "theirs";

        var result = ThreeWayMerge.Merge(string.Join('\n', b), string.Join('\n', mine), string.Join('\n', theirs));

        Assert.Equal(0, result.Conflicts);
        Assert.Contains("meins", result.Text);
        Assert.Contains("theirs", result.Text);
    }

    [Theory]
    [InlineData("<<<<<<< x\na\n=======\nb\n>>>>>>> y", true)]
    [InlineData("<<<<<<< nur ein Marker", false)]
    [InlineData("const a = b <<<<<<< c;", false)]
    public void HasConflictMarkers_RequiresAllThreeMarkers(string text, bool expected) =>
        Assert.Equal(expected, ThreeWayMerge.HasConflictMarkers(text));
}
