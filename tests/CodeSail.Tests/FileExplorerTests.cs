using Bunit;
using CodeSail.Components;
using CodeSail.Models;
using CodeSail.Services.GitHub;

namespace CodeSail.Tests;

public class FileExplorerTests : MudTestContext
{
    private static readonly string[] Files = ["src/App.cs", "src/Services/Api.cs", "README.md"];

    private static readonly RepoTree Tree = RepoTree.FromEntries(
        "sha",
        [new("src", true), new("src/Services", true), .. Files.Select(f => new TreeEntry(f, false, "blob"))],
        truncated: false);

    [Fact]
    public void ShowsTopLevelEntries()
    {
        var cut = Render<FileExplorer>(p => p.Add(x => x.Tree, Tree));

        Assert.Contains("src", cut.Markup);
        Assert.Contains("README.md", cut.Markup);
    }

    [Fact]
    public void Filter_ShowsMatchingFilesAndSelectsThem()
    {
        string? selected = null;
        var cut = Render<FileExplorer>(p => p
            .Add(x => x.Tree, Tree)
            .Add(x => x.FileSelected, path => selected = path));

        cut.Find("input").Input("api");

        cut.WaitForAssertion(() => Assert.Contains("src/Services/Api.cs", cut.Markup));
        Assert.DoesNotContain("README.md", cut.Markup);

        // MudListItem ruft OnClick asynchron auf, daher auf das Ergebnis warten
        cut.Find(".mud-list-item").Click();
        cut.WaitForAssertion(() => Assert.Equal("src/Services/Api.cs", selected));
    }
}
