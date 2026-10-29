using CodeSail.Models;

namespace CodeSail.Services.GitHub;

public interface IGitHubService
{
    /// <summary>Stand des API-Kontingents nach der letzten Anfrage.</summary>
    RateLimitInfo? RateLimit { get; }

    event Action? RateLimitChanged;

    /// <summary>Prüft das Token und liefert den Login des Nutzers.</summary>
    Task<string> GetCurrentUserLoginAsync();

    Task<IReadOnlyList<RepoSummary>> GetRepositoriesAsync();

    Task<RepoSummary> GetRepositoryAsync(string owner, string name);

    Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(string owner, string name);

    /// <summary>Lädt den kompletten Datei-Baum eines Commits mit einer einzigen Anfrage.</summary>
    Task<TreeSnapshot> GetTreeAsync(string owner, string name, string commitSha);

    /// <summary>Lädt den Rohinhalt einer Datei über ihre Blob-SHA (auch Dateien über 1 MB).</summary>
    Task<byte[]> GetBlobAsync(string owner, string name, string blobSha);
}

public sealed record TreeSnapshot(string Sha, List<TreeEntry> Entries, bool Truncated);
