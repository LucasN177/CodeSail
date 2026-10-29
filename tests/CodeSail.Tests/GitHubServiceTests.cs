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
    public async Task GetCommits_FiltersByPathAndPages()
    {
        _handler.Respond("""
            [ { "sha": "abc1234567", "author": { "login": "ada" },
                "commit": { "message": "Titel\n\nText", "author": { "name": "Ada", "date": "2026-10-01T10:00:00Z" } } } ]
            """);

        var commits = await _service.GetCommitsAsync("o", "r", "feature/x", "src/a b.cs", 2);

        Assert.Equal("https://api.github.com/repos/o/r/commits?sha=feature%2Fx&per_page=30&page=2&path=src%2Fa%20b.cs", _handler.Uri);
        var commit = Assert.Single(commits);
        Assert.Equal(("Titel", "Text", "ada", "abc1234"), (commit.Title, commit.Body, commit.Author, commit.ShortSha));
    }

    [Fact]
    public async Task MergeBranch_ReturnsNullWhenNothingToMerge()
    {
        _handler.Respond("", HttpStatusCode.NoContent);

        var sha = await _service.MergeBranchAsync("o", "r", "feature", "main", "Merge main into feature");

        Assert.Null(sha);
        Assert.Equal("https://api.github.com/repos/o/r/merges", _handler.Uri);
        Assert.Contains("\"base\":\"feature\"", _handler.Body);
        Assert.Contains("\"head\":\"main\"", _handler.Body);
    }

    [Fact]
    public async Task GetBlame_ParsesRangesFromGraphQl()
    {
        _handler.Respond("""
            { "data": { "repository": { "object": { "blame": { "ranges": [
              { "startingLine": 1, "endingLine": 3,
                "commit": { "oid": "c1", "message": "Anfang", "committedDate": "2026-01-01T00:00:00Z", "author": { "name": "Ada L.", "user": { "login": "ada" } } } },
              { "startingLine": 4, "endingLine": 4,
                "commit": { "oid": "c2", "message": "Fix", "committedDate": "2026-02-01T00:00:00Z", "author": { "name": "Bob", "user": null } } } ] } } } } }
            """);

        var ranges = await _service.GetBlameAsync("o", "r", "main", "src/a.cs");

        Assert.Equal("https://api.github.com/graphql", _handler.Uri);
        Assert.Contains("\"expression\":\"main\"", _handler.Body);
        Assert.Equal([(1, 3, "ada"), (4, 4, "Bob")], ranges.Select(r => (r.StartLine, r.EndLine, r.Commit.Author)));
    }

    [Fact]
    public async Task GetBlame_ReportsGraphQlErrors()
    {
        _handler.Respond("""{ "data": null, "errors": [ { "message": "Could not resolve file" } ] }""");

        var ex = await Assert.ThrowsAsync<GitHubApiException>(() => _service.GetBlameAsync("o", "r", "main", "fehlt.cs"));

        Assert.Equal("Could not resolve file", ex.ApiMessage);
    }

    [Fact]
    public async Task GetCiStatus_CombinesSourcesAndNotesMissingPermission()
    {
        _handler.RespondInOrder(
            ("""{ "message": "Resource not accessible" }""", HttpStatusCode.Forbidden),
            ("""
             { "check_runs": [
               { "name": "build", "status": "completed", "conclusion": "success", "html_url": "u1" },
               { "name": "test", "status": "in_progress", "conclusion": null, "html_url": "u2" } ] }
             """, HttpStatusCode.OK));

        var ci = await _service.GetCiStatusAsync("o", "r", "h1");

        Assert.True(ci.MissingPermission);
        Assert.Equal([("build", CheckState.Success), ("test", CheckState.Pending)], ci.Items.Select(i => (i.Name, i.State)));
        Assert.Equal(CheckState.Pending, ci.Overall);
    }

    [Fact]
    public async Task GetConversation_MergesAndSortsAndSkipsEmptyReviews()
    {
        _handler.RespondInOrder(
            ("""[ { "user": { "login": "bob" }, "body": "Später", "created_at": "2026-10-03T00:00:00Z" } ]""", HttpStatusCode.OK),
            ("""
             [ { "user": { "login": "ada" }, "body": "", "state": "APPROVED", "submitted_at": "2026-10-02T00:00:00Z" },
               { "user": { "login": "ada" }, "body": "", "state": "COMMENTED", "submitted_at": "2026-10-02T01:00:00Z" } ]
             """, HttpStatusCode.OK),
            ("""[ { "user": { "login": "eve" }, "body": "Hier", "created_at": "2026-10-01T00:00:00Z", "path": "a.cs", "line": 7 } ]""", HttpStatusCode.OK));

        var items = await _service.GetConversationAsync("o", "r", 5);

        Assert.Equal(
            [(ConversationKind.LineComment, "eve"), (ConversationKind.Review, "ada"), (ConversationKind.Comment, "bob")],
            items.Select(i => (i.Kind, i.Author)));
        Assert.Equal(("a.cs", 7), (items[0].Path, items[0].Line));
        Assert.Equal("APPROVED", items[1].ReviewState);
    }

    [Fact]
    public async Task SubmitReview_SendsEvent()
    {
        _handler.Respond("{}");

        await _service.SubmitReviewAsync("o", "r", 5, ReviewAction.RequestChanges, "Bitte Tests ergänzen");

        Assert.Equal("https://api.github.com/repos/o/r/pulls/5/reviews", _handler.Uri);
        Assert.Contains("\"event\":\"REQUEST_CHANGES\"", _handler.Body);
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

        private readonly Queue<(string Json, HttpStatusCode Status)> _queue = new();

        public List<string> Uris { get; } = [];

        public void Respond(string json, HttpStatusCode status = HttpStatusCode.OK, Dictionary<string, string>? headers = null)
        {
            (_response, _status, _headers) = (json, status, headers ?? []);
        }

        /// <summary>Antworten für mehrere aufeinanderfolgende Anfragen, in dieser Reihenfolge.</summary>
        public void RespondInOrder(params (string Json, HttpStatusCode Status)[] responses)
        {
            foreach (var response in responses)
                _queue.Enqueue(response);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri!.AbsoluteUri;
            Uris.Add(Uri);
            Method = request.Method;
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            var (json, status) = _queue.Count > 0 ? _queue.Dequeue() : (_response, _status);
            var response = new HttpResponseMessage(status) { Content = new StringContent(json) };
            foreach (var (name, value) in _headers)
                response.Headers.Add(name, value);
            return response;
        }
    }
}
