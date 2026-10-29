using System.Text;
using Bunit;
using CodeSail.Components;
using CodeSail.Models;
using CodeSail.Services;
using CodeSail.Services.GitHub;
using CodeSail.Services.Preview;
using CodeSail.Services.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace CodeSail.Tests;

public class PreviewBuilderTests
{
    [Theory]
    [InlineData("README.md", PreviewKind.Markdown)]
    [InlineData("docs/Guide.MARKDOWN", PreviewKind.Markdown)]
    [InlineData("site/index.html", PreviewKind.Html)]
    [InlineData("page.htm", PreviewKind.Html)]
    [InlineData("app.cs", PreviewKind.None)]
    public void KindOf_DetectsPreviewableFiles(string path, PreviewKind kind) =>
        Assert.Equal(kind, PreviewBuilder.KindOf(path));

    [Fact]
    public void RenderMarkdown_SupportsTablesAndTaskLists()
    {
        var html = PreviewBuilder.RenderMarkdown("| a | b |\n|---|---|\n| 1 | 2 |\n\n- [x] erledigt");

        Assert.Contains("<table>", html);
        Assert.Contains("type=\"checkbox\"", html);
    }

    [Fact]
    public void FindLocalReferences_IgnoresUrlsAnchorsAndDataUris()
    {
        const string html = """
            <img src="img/logo.png"><img src='./a b.svg'>
            <img src="https://example.com/x.png"><img src="//cdn.example.com/y.png">
            <img src="data:image/png;base64,AA=="><link rel="stylesheet" href="#top">
            <script src="app.js"></script><a href="other.md">Link</a>
            """;

        Assert.Equal(["img/logo.png", "./a b.svg", "app.js"], PreviewBuilder.FindLocalReferences(html));
    }

    [Theory]
    [InlineData("docs/guide.md", "img/a.png", "docs/img/a.png")]
    [InlineData("docs/guide.md", "./a.png", "docs/a.png")]
    [InlineData("docs/guide.md", "../assets/a.png", "assets/a.png")]
    [InlineData("docs/guide.md", "/logo.svg", "logo.svg")]
    [InlineData("README.md", "my%20image.png?raw=true", "my image.png")]
    [InlineData("README.md", "../outside.png", null)]
    public void ResolvePath_IsRelativeToTheFile(string from, string reference, string? expected) =>
        Assert.Equal(expected, PreviewBuilder.ResolvePath(from, reference));

    [Fact]
    public void EmbedAssets_InlinesStylesAndScriptsAndUsesDataUrlsForImages()
    {
        const string html = """
            <link rel="stylesheet" href="style.css">
            <script type="module" src="app.js"></script>
            <img alt="Logo" src='logo.png'>
            <img src="missing.png">
            """;
        var assets = new Dictionary<string, PreviewAsset>
        {
            ["style.css"] = new("style.css", "body{color:red}"u8.ToArray()),
            ["app.js"] = new("app.js", "document.write('</script>')"u8.ToArray()),
            ["logo.png"] = new("logo.png", [1, 2, 3])
        };

        var result = PreviewBuilder.EmbedAssets(html, assets);

        Assert.Contains("<style data-source=\"style.css\">\nbody{color:red}\n</style>", result);
        Assert.Contains("<script type=\"module\" >document.write('<\\/script>')</script>", result);
        Assert.Contains("src=\"data:image/png;base64,AQID\"", result);
        Assert.Contains("<img src=\"missing.png\">", result);
    }

    [Fact]
    public void BuildDocument_ForHtml_KeepsPageAndAddsScrollScriptBeforeBodyEnd()
    {
        var document = PreviewBuilder.BuildDocument("<html><body><p>Hi</p></body></html>", PreviewKind.Html, "x");

        Assert.StartsWith("<html><body><p>Hi</p>", document);
        Assert.Contains("codesailReady", document);
        Assert.EndsWith("</body></html>", document);
    }

    [Fact]
    public void BuildDocument_ForMarkdown_OpensLinksInNewTab() =>
        Assert.Contains("<base target=\"_blank\">", PreviewBuilder.BuildDocument("<h1>T</h1>", PreviewKind.Markdown, "T"));
}

public class PreviewServiceTests
{
    private readonly FakeGitHubService _gitHub = new();
    private readonly FakeFileCache _cache = new();
    private readonly PreviewService _previews;
    private readonly RepoTree _tree;

    public PreviewServiceTests()
    {
        _gitHub.Blobs["sha-png"] = [137, 80, 78, 71];
        _gitHub.Blobs["sha-css"] = "h1{color:blue}"u8.ToArray();
        _previews = new PreviewService(_gitHub, _cache);
        _tree = RepoTree.FromEntries("t",
        [
            new("docs", true), new("docs/logo.png", false, "sha-png"), new("docs/site.css", false, "sha-css")
        ], false);
    }

    private static CachedFile File(string path, string content) =>
        new(CacheKeys.File("o", "r", "main", path), "o", "r", "main", path, content, content, "sha", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Build_EmbedsImagesFromGitHubOnce()
    {
        var file = File("docs/guide.md", "# Titel\n\n![Logo](logo.png)");

        var first = await _previews.BuildAsync(file, _tree);
        await _previews.BuildAsync(file with { Content = file.Content + "\nmehr" }, _tree);

        Assert.Contains("<h1 id=\"titel\">Titel</h1>", first);
        Assert.Contains("data:image/png;base64,iVBORw==", first);
        Assert.Equal(1, _gitHub.BlobRequests);
    }

    [Fact]
    public async Task Build_PrefersLocallyEditedStylesheet()
    {
        await _cache.SaveFileAsync(File("docs/site.css", "h1{color:green}") with { OriginalContent = "h1{color:blue}" });

        var html = await _previews.BuildAsync(File("docs/index.html", "<link rel=\"stylesheet\" href=\"site.css\"><h1>Hi</h1>"), _tree);

        Assert.Contains("h1{color:green}", html);
        Assert.Equal(0, _gitHub.BlobRequests);
    }

    [Fact]
    public async Task Build_WhenOffline_LeavesReferenceUntouched()
    {
        _gitHub.NetworkError = new HttpRequestException();

        var html = await _previews.BuildAsync(File("docs/guide.md", "![Logo](logo.png)"), _tree);

        Assert.Contains("src=\"logo.png\"", html);
    }

    [Fact]
    public async Task Build_ForUnsupportedFile_Throws() =>
        await Assert.ThrowsAsync<NotSupportedException>(() => _previews.BuildAsync(File("app.cs", ""), _tree));
}

public class PreviewPaneTests : MudTestContext
{
    [Fact]
    public void RendersMarkdownIntoSandboxedFrameWithoutSameOrigin()
    {
        Services.AddSingleton<IGitHubService>(new FakeGitHubService());
        Services.AddSingleton<IFileCache>(new FakeFileCache());
        Services.AddSingleton<PreviewService>();
        var file = new CachedFile("k", "o", "r", "main", "README.md", "# Hallo", "# Hallo", "sha", DateTimeOffset.UtcNow);

        var cut = Render<PreviewPane>(p => p
            .Add(x => x.File, file)
            .Add(x => x.Tree, RepoTree.FromEntries("t", [], false)));

        var frame = cut.Find("iframe");
        cut.WaitForAssertion(() => Assert.Contains("Hallo</h1>", cut.Find("iframe").GetAttribute("srcdoc")));
        Assert.DoesNotContain("allow-same-origin", frame.GetAttribute("sandbox"));
    }
}
