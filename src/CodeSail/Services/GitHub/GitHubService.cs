using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeSail.Models;
using CodeSail.Services.Storage;
using Octokit;
using Octokit.Internal;
using PullRequestFile = CodeSail.Models.PullRequestFile;

namespace CodeSail.Services.GitHub;

public sealed class GitHubService(ITokenStore tokenStore, HttpClient http) : IGitHubService
{
    private static readonly ProductHeaderValue Product = new("CodeSail");
    private static readonly HttpClient DefaultHttp = new() { BaseAddress = new Uri("https://api.github.com/") };

    public GitHubService(ITokenStore tokenStore) : this(tokenStore, DefaultHttp)
    {
    }

    private GitHubClient? _client;
    private string? _clientToken;

    public RateLimitInfo? RateLimit { get; private set; }

    public event Action? RateLimitChanged;

    public event Action? Unauthorized;

    public Task<string> GetCurrentUserLoginAsync() =>
        RunAsync(async c => (await c.User.Current()).Login);

    public Task<IReadOnlyList<RepoSummary>> GetRepositoriesAsync() =>
        RunAsync<IReadOnlyList<RepoSummary>>(async c =>
            (await c.Repository.GetAllForCurrent(new RepositoryRequest { Sort = RepositorySort.Updated }))
                .Select(ToSummary).ToList());

    public Task<RepoSummary> GetRepositoryAsync(string owner, string name) =>
        RunAsync(async c => ToSummary(await c.Repository.Get(owner, name)));

    public Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(string owner, string name) =>
        RunAsync<IReadOnlyList<BranchInfo>>(async c =>
            (await c.Repository.Branch.GetAll(owner, name)).Select(b => new BranchInfo(b.Name, b.Commit.Sha, b.Protected)).ToList());

    public Task<TreeSnapshot> GetTreeAsync(string owner, string name, string commitSha) =>
        RunAsync(async c =>
        {
            var tree = await c.Git.Tree.GetRecursive(owner, name, commitSha);

            // Submodule (TreeType.Commit) können nicht geöffnet werden und werden ausgelassen
            var entries = tree.Tree
                .Where(t => t.Type.Value is TreeType.Blob or TreeType.Tree)
                .Select(t => t.Type.Value == TreeType.Tree
                    ? new TreeEntry(t.Path, IsFolder: true)
                    : new TreeEntry(t.Path, IsFolder: false, t.Sha, t.Mode, t.Size))
                .ToList();

            return new TreeSnapshot(tree.Sha, entries, tree.Truncated);
        });

    public Task<byte[]> GetBlobAsync(string owner, string name, string blobSha) =>
        RunAsync(async c =>
        {
            var blob = await c.Git.Blob.Get(owner, name, blobSha);
            return blob.Encoding.Value == EncodingType.Base64
                ? Convert.FromBase64String(blob.Content)
                : System.Text.Encoding.UTF8.GetBytes(blob.Content);
        });

    /// <summary>
    /// Die Git-Data-API funktioniert in leeren Repositories nicht. Die Contents-API legt dagegen
    /// mit der ersten Datei auch den ersten Commit und den Standard-Branch an.
    /// </summary>
    public Task CreateFirstFileAsync(string owner, string name, string path, string content, string message) =>
        SendAsync(HttpMethod.Put, $"{RepoPath(owner, name)}/contents/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}",
            new { message, content = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content)) });

    /// <summary>Führt einen Octokit-Aufruf aus, aktualisiert das API-Kontingent und meldet abgelaufene Tokens.</summary>
    private async Task<T> RunAsync<T>(Func<GitHubClient, Task<T>> call)
    {
        var client = await GetClientAsync();
        try
        {
            return await call(client);
        }
        catch (AuthorizationException)
        {
            Unauthorized?.Invoke();
            throw;
        }
        finally
        {
            UpdateRateLimit(client);
        }
    }

    // Git-Data-Aufrufe laufen direkt über HTTP: Octokit lässt null-Werte weg, GitHub braucht aber
    // "sha": null zum Löschen von Dateien, und Branch-Namen mit "/" müssen unkodiert im Pfad stehen.

    public async Task<string> GetBranchHeadAsync(string owner, string name, string branch)
    {
        var json = await SendAsync(HttpMethod.Get, $"{RepoPath(owner, name)}/git/ref/heads/{BranchPath(branch)}");
        return json.GetProperty("object").GetProperty("sha").GetString()!;
    }

    public async Task<string> CreateTreeAsync(string owner, string name, string baseTreeSha, IReadOnlyList<TreeChange> changes)
    {
        var items = changes.Select(object (c) => c.Content is null
            ? new { path = c.Path, mode = c.Mode, type = "blob", sha = (string?)null }
            : new { path = c.Path, mode = c.Mode, type = "blob", content = c.Content });

        var json = await SendAsync(HttpMethod.Post, $"{RepoPath(owner, name)}/git/trees",
            new { base_tree = baseTreeSha, tree = items.ToList() });
        return json.GetProperty("sha").GetString()!;
    }

    public async Task<string> CreateCommitAsync(string owner, string name, string message, string treeSha, string parentSha)
    {
        var json = await SendAsync(HttpMethod.Post, $"{RepoPath(owner, name)}/git/commits",
            new { message, tree = treeSha, parents = new[] { parentSha } });
        return json.GetProperty("sha").GetString()!;
    }

    public Task UpdateBranchAsync(string owner, string name, string branch, string commitSha) =>
        SendAsync(HttpMethod.Patch, $"{RepoPath(owner, name)}/git/refs/heads/{BranchPath(branch)}",
            new { sha = commitSha, force = false });

    public Task CreateBranchAsync(string owner, string name, string branch, string commitSha) =>
        SendAsync(HttpMethod.Post, $"{RepoPath(owner, name)}/git/refs",
            new { @ref = $"refs/heads/{branch}", sha = commitSha });

    public async Task<string> CreatePullRequestAsync(string owner, string name, string title, string body, string head, string baseBranch, bool draft = false)
    {
        var json = await SendAsync(HttpMethod.Post, $"{RepoPath(owner, name)}/pulls",
            new { title, body, head, @base = baseBranch, draft });
        return json.GetProperty("html_url").GetString()!;
    }

    public Task DeleteBranchAsync(string owner, string name, string branch) =>
        SendAsync(HttpMethod.Delete, $"{RepoPath(owner, name)}/git/refs/heads/{BranchPath(branch)}");

    public Task RenameBranchAsync(string owner, string name, string branch, string newName) =>
        SendAsync(HttpMethod.Post, $"{RepoPath(owner, name)}/branches/{BranchPath(branch)}/rename", new { new_name = newName });

    public async Task<BranchComparison> CompareAsync(string owner, string name, string baseBranch, string headBranch)
    {
        var json = await SendAsync(HttpMethod.Get,
            $"{RepoPath(owner, name)}/compare/{BranchPath(baseBranch)}...{BranchPath(headBranch)}?per_page=100");

        var commits = json.GetProperty("commits").EnumerateArray()
            .Select(ToCommitSummary)
            // Neueste zuerst, wie in der Commit-Liste auf GitHub
            .Reverse()
            .ToList();

        var files = json.TryGetProperty("files", out var f)
            ? f.EnumerateArray().Select(x => x.GetProperty("filename").GetString()!).ToList()
            : [];

        return new BranchComparison(baseBranch, headBranch,
            json.GetProperty("ahead_by").GetInt32(), json.GetProperty("behind_by").GetInt32(), commits, files);
    }

    public async Task<IReadOnlyList<PullRequestSummary>> GetPullRequestsAsync(string owner, string name, PullRequestFilter filter)
    {
        var state = filter switch { PullRequestFilter.Closed => "closed", PullRequestFilter.All => "all", _ => "open" };
        var json = await SendAsync(HttpMethod.Get,
            $"{RepoPath(owner, name)}/pulls?state={state}&sort=updated&direction=desc&per_page=50");
        return json.EnumerateArray().Select(ToPullRequestSummary).ToList();
    }

    public async Task<PullRequestDetails> GetPullRequestAsync(string owner, string name, int number)
    {
        var json = await SendAsync(HttpMethod.Get, $"{RepoPath(owner, name)}/pulls/{number}");
        return new PullRequestDetails(
            ToPullRequestSummary(json),
            String(json, "body") ?? "",
            json.GetProperty("head").GetProperty("sha").GetString()!,
            json.TryGetProperty("mergeable", out var mergeable) && mergeable.ValueKind != JsonValueKind.Null ? mergeable.GetBoolean() : null,
            String(json, "mergeable_state") ?? "unknown",
            json.GetProperty("commits").GetInt32(),
            json.GetProperty("additions").GetInt32(),
            json.GetProperty("deletions").GetInt32(),
            json.GetProperty("changed_files").GetInt32());
    }

    public async Task<IReadOnlyList<PullRequestFile>> GetPullRequestFilesAsync(string owner, string name, int number)
    {
        var json = await SendAsync(HttpMethod.Get, $"{RepoPath(owner, name)}/pulls/{number}/files?per_page=100");
        return json.EnumerateArray().Select(ToChangedFile).ToList();
    }

    public Task MergePullRequestAsync(string owner, string name, int number, MergeMethod method, string headSha) =>
        SendAsync(HttpMethod.Put, $"{RepoPath(owner, name)}/pulls/{number}/merge",
            new { merge_method = method.ToString().ToLowerInvariant(), sha = headSha });

    public Task SetPullRequestOpenAsync(string owner, string name, int number, bool open) =>
        SendAsync(HttpMethod.Patch, $"{RepoPath(owner, name)}/pulls/{number}", new { state = open ? "open" : "closed" });

    public async Task<IReadOnlyList<CommitSummary>> GetCommitsAsync(string owner, string name, string branch, string? path, int page)
    {
        var query = $"sha={Uri.EscapeDataString(branch)}&per_page=30&page={page}"
            + (path is null ? "" : $"&path={Uri.EscapeDataString(path)}");
        var json = await SendAsync(HttpMethod.Get, $"{RepoPath(owner, name)}/commits?{query}");
        return json.EnumerateArray().Select(ToCommitSummary).ToList();
    }

    public async Task<CommitDetails> GetCommitAsync(string owner, string name, string sha)
    {
        var json = await SendAsync(HttpMethod.Get, $"{RepoPath(owner, name)}/commits/{Uri.EscapeDataString(sha)}");
        var stats = json.GetProperty("stats");
        var files = json.TryGetProperty("files", out var f) ? f.EnumerateArray().Select(ToChangedFile).ToList() : [];
        return new CommitDetails(
            ToCommitSummary(json),
            json.GetProperty("parents").EnumerateArray().Select(p => p.GetProperty("sha").GetString()!).ToList(),
            stats.GetProperty("additions").GetInt32(),
            stats.GetProperty("deletions").GetInt32(),
            files,
            String(json, "html_url") ?? "");
    }

    public Task UpdatePullRequestAsync(string owner, string name, int number, string title, string body) =>
        SendAsync(HttpMethod.Patch, $"{RepoPath(owner, name)}/pulls/{number}", new { title, body });

    public async Task<IReadOnlyList<ConversationItem>> GetConversationAsync(string owner, string name, int number)
    {
        var comments = await SendAsync(HttpMethod.Get, $"{RepoPath(owner, name)}/issues/{number}/comments?per_page=100");
        var reviews = await SendAsync(HttpMethod.Get, $"{RepoPath(owner, name)}/pulls/{number}/reviews?per_page=100");
        var lineComments = await SendAsync(HttpMethod.Get, $"{RepoPath(owner, name)}/pulls/{number}/comments?per_page=100");

        var items = new List<ConversationItem>();
        items.AddRange(comments.EnumerateArray().Select(c => new ConversationItem(
            ConversationKind.Comment, Login(c, "user") ?? "", String(c, "body") ?? "", c.GetProperty("created_at").GetDateTimeOffset())));

        // Reviews ohne Text und ohne Entscheidung (reine Container für Zeilenkommentare) weglassen
        items.AddRange(reviews.EnumerateArray()
            .Where(r => String(r, "state") is not "PENDING" && (String(r, "body") is { Length: > 0 } || String(r, "state") != "COMMENTED"))
            .Select(r => new ConversationItem(
                ConversationKind.Review, Login(r, "user") ?? "", String(r, "body") ?? "",
                r.TryGetProperty("submitted_at", out var at) && at.ValueKind == JsonValueKind.String ? at.GetDateTimeOffset() : DateTimeOffset.MinValue,
                String(r, "state"))));

        items.AddRange(lineComments.EnumerateArray().Select(c => new ConversationItem(
            ConversationKind.LineComment, Login(c, "user") ?? "", String(c, "body") ?? "", c.GetProperty("created_at").GetDateTimeOffset(),
            Path: String(c, "path"),
            Line: c.TryGetProperty("line", out var line) && line.ValueKind == JsonValueKind.Number ? line.GetInt32() : null)));

        return items.OrderBy(i => i.Date).ToList();
    }

    public Task AddCommentAsync(string owner, string name, int number, string body) =>
        SendAsync(HttpMethod.Post, $"{RepoPath(owner, name)}/issues/{number}/comments", new { body });

    public Task SubmitReviewAsync(string owner, string name, int number, ReviewAction action, string body)
    {
        var @event = action switch
        {
            ReviewAction.Approve => "APPROVE",
            ReviewAction.RequestChanges => "REQUEST_CHANGES",
            _ => "COMMENT"
        };
        return SendAsync(HttpMethod.Post, $"{RepoPath(owner, name)}/pulls/{number}/reviews", new { @event, body });
    }

    public async Task<CiStatus> GetCiStatusAsync(string owner, string name, string sha)
    {
        var items = new List<CheckItem>();
        var missingPermission = false;

        // Zwei Quellen: klassische Status-Checks und Check-Runs (z. B. GitHub Actions). Jede braucht eigene Leserechte.
        try
        {
            var status = await SendAsync(HttpMethod.Get, $"{RepoPath(owner, name)}/commits/{Uri.EscapeDataString(sha)}/status");
            items.AddRange(status.GetProperty("statuses").EnumerateArray().Select(s => new CheckItem(
                String(s, "context") ?? "Status",
                String(s, "state") switch { "success" => CheckState.Success, "pending" => CheckState.Pending, _ => CheckState.Failure },
                String(s, "description"),
                String(s, "target_url"))));
        }
        catch (GitHubApiException ex) when (ex.Status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            missingPermission = true;
        }

        try
        {
            var runs = await SendAsync(HttpMethod.Get, $"{RepoPath(owner, name)}/commits/{Uri.EscapeDataString(sha)}/check-runs?per_page=100");
            items.AddRange(runs.GetProperty("check_runs").EnumerateArray().Select(r => new CheckItem(
                String(r, "name") ?? "Check",
                String(r, "status") != "completed" ? CheckState.Pending
                    : String(r, "conclusion") switch
                    {
                        "success" => CheckState.Success,
                        "neutral" or "skipped" => CheckState.Neutral,
                        _ => CheckState.Failure
                    },
                String(r, "conclusion") ?? String(r, "status"),
                String(r, "html_url"))));
        }
        catch (GitHubApiException ex) when (ex.Status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            missingPermission = true;
        }

        return new CiStatus(items, missingPermission);
    }

    public async Task<string?> MergeBranchAsync(string owner, string name, string baseBranch, string head, string message)
    {
        // 201 mit Merge-Commit, 204 ohne Inhalt, wenn baseBranch schon alles enthält
        var json = await SendAsync(HttpMethod.Post, $"{RepoPath(owner, name)}/merges",
            new { @base = baseBranch, head, commit_message = message });
        return json.ValueKind == JsonValueKind.Object ? String(json, "sha") : null;
    }

    public async Task<IReadOnlyList<BlameRange>> GetBlameAsync(string owner, string name, string branch, string path)
    {
        const string query = """
            query($owner: String!, $name: String!, $expression: String!, $path: String!) {
              repository(owner: $owner, name: $name) {
                object(expression: $expression) {
                  ... on Commit {
                    blame(path: $path) {
                      ranges {
                        startingLine
                        endingLine
                        commit { oid message committedDate author { name user { login } } }
                      }
                    }
                  }
                }
              }
            }
            """;
        var json = await SendAsync(HttpMethod.Post, "graphql",
            new { query, variables = new { owner, name, expression = branch, path } });

        // GraphQL meldet Fehler mit Status 200 im Feld "errors"
        if (json.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
            throw new GitHubApiException(System.Net.HttpStatusCode.BadRequest, String(errors[0], "message") ?? "GraphQL-Fehler");

        var obj = json.GetProperty("data").GetProperty("repository").GetProperty("object");
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty("blame", out var blame))
            throw new GitHubApiException(System.Net.HttpStatusCode.NotFound, "Branch oder Datei nicht gefunden");

        return blame.GetProperty("ranges").EnumerateArray()
            .Select(r =>
            {
                var commit = r.GetProperty("commit");
                var author = commit.GetProperty("author");
                return new BlameRange(
                    r.GetProperty("startingLine").GetInt32(),
                    r.GetProperty("endingLine").GetInt32(),
                    new CommitSummary(
                        commit.GetProperty("oid").GetString()!,
                        String(commit, "message") ?? "",
                        Login(author, "user") ?? String(author, "name") ?? "",
                        commit.GetProperty("committedDate").GetDateTimeOffset()));
            })
            .ToList();
    }

    private static CommitSummary ToCommitSummary(JsonElement c)
    {
        var commit = c.GetProperty("commit");
        var author = commit.GetProperty("author");
        return new CommitSummary(
            c.GetProperty("sha").GetString()!,
            String(commit, "message") ?? "",
            Login(c, "author") ?? String(author, "name") ?? "",
            author.TryGetProperty("date", out var date) ? date.GetDateTimeOffset() : null);
    }

    private static PullRequestFile ToChangedFile(JsonElement f) => new(
        f.GetProperty("filename").GetString()!,
        f.GetProperty("status").GetString()!,
        f.GetProperty("additions").GetInt32(),
        f.GetProperty("deletions").GetInt32(),
        String(f, "patch"),
        String(f, "previous_filename"));

    private static PullRequestSummary ToPullRequestSummary(JsonElement pr)
    {
        var merged = pr.TryGetProperty("merged_at", out var mergedAt) && mergedAt.ValueKind != JsonValueKind.Null;
        var state = merged ? PullRequestState.Merged
            : String(pr, "state") == "open" ? PullRequestState.Open : PullRequestState.Closed;
        var head = pr.GetProperty("head");
        var headRepo = head.TryGetProperty("repo", out var repo) && repo.ValueKind == JsonValueKind.Object
            ? String(repo, "full_name") ?? ""
            : "";

        return new PullRequestSummary(
            pr.GetProperty("number").GetInt32(),
            String(pr, "title") ?? "",
            state,
            pr.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True,
            Login(pr, "user") ?? "",
            String(head, "ref") ?? "",
            headRepo,
            String(pr.GetProperty("base"), "ref") ?? "",
            pr.GetProperty("updated_at").GetDateTimeOffset(),
            String(pr, "html_url") ?? "");
    }

    private static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Login(JsonElement element, string property) =>
        element.TryGetProperty(property, out var user) && user.ValueKind == JsonValueKind.Object ? String(user, "login") : null;

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body = null)
    {
        var token = await tokenStore.GetTokenAsync()
            ?? throw new InvalidOperationException("Kein GitHub-Token hinterlegt.");

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (body is not null)
            request.Content = JsonContent.Create(body);

        using var response = await http.SendAsync(request);
        UpdateRateLimit(response);

        var text = await response.Content.ReadAsStringAsync();
        var json = string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            Unauthorized?.Invoke();
        if (!response.IsSuccessStatusCode)
        {
            var message = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("message", out var m)
                ? m.GetString() ?? ""
                : response.ReasonPhrase ?? "";
            throw new GitHubApiException(response.StatusCode, message);
        }

        return json;
    }

    private static string RepoPath(string owner, string name) =>
        $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}";

    private static string BranchPath(string branch) =>
        string.Join('/', branch.Split('/').Select(Uri.EscapeDataString));

    private void UpdateRateLimit(HttpResponseMessage response)
    {
        if (!int.TryParse(Header(response, "x-ratelimit-limit"), out var limit)
            || !int.TryParse(Header(response, "x-ratelimit-remaining"), out var remaining)
            || !long.TryParse(Header(response, "x-ratelimit-reset"), out var reset))
            return;

        RateLimit = new RateLimitInfo(limit, remaining, DateTimeOffset.FromUnixTimeSeconds(reset));
        RateLimitChanged?.Invoke();
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private async Task<GitHubClient> GetClientAsync()
    {
        var token = await tokenStore.GetTokenAsync()
            ?? throw new InvalidOperationException("Kein GitHub-Token hinterlegt.");

        if (_client is not null && _clientToken == token)
            return _client;

        // Octokits Standard-Handler setzt Optionen (z. B. Proxy/Decompression), die in WebAssembly
        // nicht unterstützt werden. Ein schlichter HttpClientHandler nutzt intern die Browser-fetch-API.
        var connection = new Connection(
            Product,
            GitHubClient.GitHubApiUrl,
            new InMemoryCredentialStore(new Credentials(token)),
            new HttpClientAdapter(() => new HttpClientHandler()),
            new SimpleJsonSerializer());

        _client = new GitHubClient(connection);
        _clientToken = token;
        return _client;
    }

    private void UpdateRateLimit(GitHubClient client)
    {
        if (client.GetLastApiInfo()?.RateLimit is not { } rate)
            return;

        RateLimit = new RateLimitInfo(rate.Limit, rate.Remaining, rate.Reset);
        RateLimitChanged?.Invoke();
    }

    private static RepoSummary ToSummary(Repository r) =>
        new(r.Id, r.Owner.Login, r.Name, r.DefaultBranch, r.Private, r.Description);
}
