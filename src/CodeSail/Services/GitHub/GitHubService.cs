using System.Net.Http.Json;
using System.Text.Json;
using CodeSail.Models;
using CodeSail.Services.Storage;
using Octokit;
using Octokit.Internal;

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

    public async Task<string> GetCurrentUserLoginAsync()
    {
        var client = await GetClientAsync();
        var user = await client.User.Current();
        UpdateRateLimit(client);
        return user.Login;
    }

    public async Task<IReadOnlyList<RepoSummary>> GetRepositoriesAsync()
    {
        var client = await GetClientAsync();
        var repos = await client.Repository.GetAllForCurrent(new RepositoryRequest { Sort = RepositorySort.Updated });
        UpdateRateLimit(client);
        return repos.Select(ToSummary).ToList();
    }

    public async Task<RepoSummary> GetRepositoryAsync(string owner, string name)
    {
        var client = await GetClientAsync();
        var repo = await client.Repository.Get(owner, name);
        UpdateRateLimit(client);
        return ToSummary(repo);
    }

    public async Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(string owner, string name)
    {
        var client = await GetClientAsync();
        var branches = await client.Repository.Branch.GetAll(owner, name);
        UpdateRateLimit(client);
        return branches.Select(b => new BranchInfo(b.Name, b.Commit.Sha)).ToList();
    }

    public async Task<TreeSnapshot> GetTreeAsync(string owner, string name, string commitSha)
    {
        var client = await GetClientAsync();
        var tree = await client.Git.Tree.GetRecursive(owner, name, commitSha);
        UpdateRateLimit(client);

        // Submodule (TreeType.Commit) können nicht geöffnet werden und werden ausgelassen
        var entries = tree.Tree
            .Where(t => t.Type.Value is TreeType.Blob or TreeType.Tree)
            .Select(t => t.Type.Value == TreeType.Tree
                ? new TreeEntry(t.Path, IsFolder: true)
                : new TreeEntry(t.Path, IsFolder: false, t.Sha, t.Mode))
            .ToList();

        return new TreeSnapshot(tree.Sha, entries, tree.Truncated);
    }

    public async Task<byte[]> GetBlobAsync(string owner, string name, string blobSha)
    {
        var client = await GetClientAsync();
        var blob = await client.Git.Blob.Get(owner, name, blobSha);
        UpdateRateLimit(client);

        return blob.Encoding.Value == EncodingType.Base64
            ? Convert.FromBase64String(blob.Content)
            : System.Text.Encoding.UTF8.GetBytes(blob.Content);
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

    public async Task<string> CreatePullRequestAsync(string owner, string name, string title, string body, string head, string baseBranch)
    {
        var json = await SendAsync(HttpMethod.Post, $"{RepoPath(owner, name)}/pulls",
            new { title, body, head, @base = baseBranch });
        return json.GetProperty("html_url").GetString()!;
    }

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
