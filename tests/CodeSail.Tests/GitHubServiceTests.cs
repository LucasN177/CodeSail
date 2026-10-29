using System.Net;
using System.Text.Json;
using CodeSail.Models;
using CodeSail.Services.GitHub;

namespace CodeSail.Tests;

/// <summary>Prüft die direkt per HTTP gesendeten Git-Data-Aufrufe (Pfade, JSON-Body, Fehler).</summary>
public class GitHubServiceTests
{
    private readonly RecordingHandler _handler = new();
    private readonly GitHubService _service;

    public GitHubServiceTests()
    {
        var store = new FakeTokenStore { Token = "token" };
        _service = new GitHubService(store, new HttpClient(_handler) { BaseAddress = new Uri("https://api.github.com/") });
    }

    [Fact]
    public async Task CreateTree_SendsNullShaForDeletionsAndContentForChanges()
    {
        _handler.Respond("""{ "sha": "t2" }""");

        var sha = await _service.CreateTreeAsync("o", "r", "t1",
            [new TreeChange("a.txt", "100644", "neu"), new TreeChange("alt.txt", "100755", null)]);

        Assert.Equal("t2", sha);
        Assert.Equal("https://api.github.com/repos/o/r/git/trees", _handler.Uri);
        using var body = JsonDocument.Parse(_handler.Body!);
        Assert.Equal("t1", body.RootElement.GetProperty("base_tree").GetString());
        var items = body.RootElement.GetProperty("tree");
        Assert.Equal("neu", items[0].GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("sha").ValueKind);
        Assert.Equal("100755", items[1].GetProperty("mode").GetString());
    }

    [Fact]
    public async Task UpdateBranch_KeepsSlashesInBranchNameAndSendsBearerToken()
    {
        _handler.Respond("{}");

        await _service.UpdateBranchAsync("o", "r", "feature/x y", "c1");

        Assert.Equal("https://api.github.com/repos/o/r/git/refs/heads/feature/x%20y", _handler.Uri);
        Assert.Equal(HttpMethod.Patch, _handler.Method);
        Assert.Equal("Bearer token", _handler.Authorization);
        Assert.Contains("\"force\":false", _handler.Body);
    }

    [Fact]
    public async Task CreateBranch_SendsFullRefName()
    {
        _handler.Respond("{}");

        await _service.CreateBranchAsync("o", "r", "feature/x", "c1");

        Assert.Contains("\"ref\":\"refs/heads/feature/x\"", _handler.Body);
    }

    [Fact]
    public async Task ErrorResponse_ThrowsWithGitHubMessage()
    {
        _handler.Respond("""{ "message": "Update is not a fast forward" }""", HttpStatusCode.UnprocessableEntity);

        var ex = await Assert.ThrowsAsync<GitHubApiException>(() => _service.UpdateBranchAsync("o", "r", "main", "c1"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.Status);
        Assert.Equal("Update is not a fast forward", ex.ApiMessage);
    }

    [Fact]
    public async Task UnauthorizedResponse_RaisesUnauthorizedEvent()
    {
        var raised = false;
        _service.Unauthorized += () => raised = true;
        _handler.Respond("""{ "message": "Bad credentials" }""", HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<GitHubApiException>(() => _service.GetBranchHeadAsync("o", "r", "main"));

        Assert.True(raised);
    }

    [Fact]
    public async Task CreateFirstFile_PutsBase64ContentViaContentsApi()
    {
        _handler.Respond("{}");

        await _service.CreateFirstFileAsync("o", "r", "docs/Read Me.md", "# Hallo", "Initial commit");

        Assert.Equal(HttpMethod.Put, _handler.Method);
        Assert.Equal("https://api.github.com/repos/o/r/contents/docs/Read%20Me.md", _handler.Uri);
        using var body = JsonDocument.Parse(_handler.Body!);
        Assert.Equal("# Hallo", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(body.RootElement.GetProperty("content").GetString()!)));
        Assert.Equal("Initial commit", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task DeleteBranch_DeletesRefWithSlashes()
    {
        _handler.Respond("");

        await _service.DeleteBranchAsync("o", "r", "feature/x");

        Assert.Equal(HttpMethod.Delete, _handler.Method);
        Assert.Equal("https://api.github.com/repos/o/r/git/refs/heads/feature/x", _handler.Uri);
    }

    [Fact]
    public async Task RenameBranch_SendsNewName()
    {
        _handler.Respond("{}");

        await _service.RenameBranchAsync("o", "r", "feature/x", "feature/y");

        Assert.Equal("https://api.github.com/repos/o/r/branches/feature/x/rename", _handler.Uri);
        Assert.Contains("\"new_name\":\"feature/y\"", _handler.Body);
    }

    [Fact]
    public async Task Compare_ParsesCountsCommitsAndFiles()
    {
        _handler.Respond("""
            { "ahead_by": 2, "behind_by": 1,
              "commits": [
                { "sha": "aaaaaaa111", "author": { "login": "ada" }, "commit": { "message": "Erster\n\nText", "author": { "name": "Ada", "date": "2026-10-01T10:00:00Z" } } },
                { "sha": "bbbbbbb222", "author": null, "commit": { "message": "Zweiter", "author": { "name": "Bob L.", "date": "2026-10-02T10:00:00Z" } } } ],
              "files": [ { "filename": "src/a.cs" } ] }
            """);

        var result = await _service.CompareAsync("o", "r", "main", "feature/x");

        Assert.Equal("https://api.github.com/repos/o/r/compare/main...feature/x?per_page=100", _handler.Uri);
        Assert.Equal((2, 1), (result.AheadBy, result.BehindBy));
        Assert.Equal(["Zweiter", "Erster"], result.Commits.Select(c => c.Title));
        Assert.Equal("Bob L.", result.Commits[0].Author);
        Assert.Equal("ada", result.Commits[1].Author);
        Assert.Equal(["src/a.cs"], result.ChangedFiles);
    }

    [Fact]
    public async Task GetPullRequests_MapsStateDraftAndFork()
    {
        _handler.Respond("""
            [ { "number": 5, "title": "Offen", "state": "open", "draft": true, "merged_at": null, "user": { "login": "ada" },
                "head": { "ref": "feature/x", "repo": { "full_name": "o/r" } }, "base": { "ref": "main" },
                "updated_at": "2026-10-29T10:00:00Z", "html_url": "https://github.com/o/r/pull/5" },
              { "number": 4, "title": "Gemergt", "state": "closed", "draft": false, "merged_at": "2026-10-20T10:00:00Z", "user": { "login": "bob" },
                "head": { "ref": "fix", "repo": null }, "base": { "ref": "main" },
                "updated_at": "2026-10-20T10:00:00Z", "html_url": "https://github.com/o/r/pull/4" } ]
            """);

        var result = await _service.GetPullRequestsAsync("o", "r", PullRequestFilter.All);

        Assert.Contains("state=all", _handler.Uri);
        Assert.Equal((PullRequestState.Open, true, "o/r"), (result[0].State, result[0].Draft, result[0].HeadRepo));
        Assert.Equal((PullRequestState.Merged, ""), (result[1].State, result[1].HeadRepo));
    }

    [Fact]
    public async Task GetPullRequest_ReadsMergeableWhichMayBeUnknown()
    {
        _handler.Respond("""
            { "number": 5, "title": "T", "state": "open", "draft": false, "merged_at": null, "user": { "login": "ada" },
              "head": { "ref": "feature/x", "sha": "h1", "repo": { "full_name": "o/r" } }, "base": { "ref": "main" },
              "updated_at": "2026-10-29T10:00:00Z", "html_url": "u", "body": null, "mergeable": null, "mergeable_state": "unknown",
              "commits": 3, "additions": 10, "deletions": 2, "changed_files": 4 }
            """);

        var result = await _service.GetPullRequestAsync("o", "r", 5);

        Assert.Null(result.Mergeable);
        Assert.Equal("", result.Body);
        Assert.Equal(("h1", 3, 10, 2, 4), (result.HeadSha, result.Commits, result.Additions, result.Deletions, result.ChangedFiles));
    }

    [Fact]
    public async Task MergePullRequest_SendsMethodAndHeadSha()
    {
        _handler.Respond("""{ "merged": true }""");

        await _service.MergePullRequestAsync("o", "r", 5, MergeMethod.Squash, "h1");

        Assert.Equal(HttpMethod.Put, _handler.Method);
        Assert.Equal("https://api.github.com/repos/o/r/pulls/5/merge", _handler.Uri);
        Assert.Contains("\"merge_method\":\"squash\"", _handler.Body);
        Assert.Contains("\"sha\":\"h1\"", _handler.Body);
    }

    [Fact]
    public async Task RateLimitHeaders_UpdateRateLimit()
    {
        _handler.Respond("""{ "object": { "sha": "c9" } }""", headers: new()
        {
            ["x-ratelimit-limit"] = "5000",
            ["x-ratelimit-remaining"] = "4999",
            ["x-ratelimit-reset"] = "1800000000"
        });

        var head = await _service.GetBranchHeadAsync("o", "r", "main");

        Assert.Equal("c9", head);
        Assert.Equal(4999, _service.RateLimit?.Remaining);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private string _response = "{}";
        private HttpStatusCode _status = HttpStatusCode.OK;
        private Dictionary<string, string> _headers = [];

        public string? Uri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Body { get; private set; }
        public string? Authorization { get; private set; }

        public void Respond(string json, HttpStatusCode status = HttpStatusCode.OK, Dictionary<string, string>? headers = null)
        {
            (_response, _status, _headers) = (json, status, headers ?? []);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri!.AbsoluteUri;
            Method = request.Method;
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            var response = new HttpResponseMessage(_status) { Content = new StringContent(_response) };
            foreach (var (name, value) in _headers)
                response.Headers.Add(name, value);
            return response;
        }
    }
}
