using CodeSail.Models;
using CodeSail.Services.Storage;
using Octokit;
using Octokit.Internal;

namespace CodeSail.Services.GitHub;

public sealed class GitHubService(ITokenStore tokenStore) : IGitHubService
{
    private static readonly ProductHeaderValue Product = new("CodeSail");

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
                : new TreeEntry(t.Path, IsFolder: false, t.Sha))
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
