using System.Net;
using System.Text.Json;
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
