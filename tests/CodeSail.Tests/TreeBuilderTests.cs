using CodeSail.Services.GitHub;

namespace CodeSail.Tests;

public class TreeBuilderTests
{
    [Fact]
    public void Build_NestsEntriesUnderTheirFolders()
    {
        var root = TreeBuilder.Build([
            new("src", true),
            new("src/App.cs", false),
            new("src/Services", true),
            new("src/Services/Api.cs", false),
            new("README.md", false)
        ]);

        var src = Assert.Single(root, n => n.Name == "src");
        Assert.True(src.IsFolder);
        var services = Assert.Single(src.Children, n => n.Name == "Services");
        Assert.Equal("src/Services/Api.cs", Assert.Single(services.Children).Path);
    }

    [Fact]
    public void Build_SortsFoldersFirstThenByNameIgnoringCase()
    {
        var root = TreeBuilder.Build([
            new("b.txt", false),
            new("Zeta", true),
            new("A.txt", false),
            new("alpha", true)
        ]);

        Assert.Equal(["alpha", "Zeta", "A.txt", "b.txt"], root.Select(n => n.Name));
    }

    [Fact]
    public void Build_CreatesMissingParentFolders()
    {
        // Bei abgeschnittenen Bäumen kann der Ordner-Eintrag selbst fehlen
        var root = TreeBuilder.Build([new("docs/guide/intro.md", false)]);

        var docs = Assert.Single(root);
        Assert.True(docs.IsFolder);
        var guide = Assert.Single(docs.Children);
        Assert.Equal("docs/guide", guide.Path);
        Assert.Equal("intro.md", Assert.Single(guide.Children).Name);
    }

    [Fact]
    public void Build_IgnoresDuplicateFolderEntries()
    {
        var root = TreeBuilder.Build([new("src/a.cs", false), new("src", true)]);

        var src = Assert.Single(root);
        Assert.Single(src.Children);
    }
}
