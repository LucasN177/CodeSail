using CodeSail.Models;

namespace CodeSail.Services.GitHub;

public interface IGitHubService
{
    /// <summary>Stand des API-Kontingents nach der letzten Anfrage.</summary>
    RateLimitInfo? RateLimit { get; }

    event Action? RateLimitChanged;

    /// <summary>GitHub hat das Token abgelehnt (401), z. B. weil es abgelaufen ist oder widerrufen wurde.</summary>
    event Action? Unauthorized;

    /// <summary>Prüft das Token und liefert den Login des Nutzers.</summary>
    Task<string> GetCurrentUserLoginAsync();

    Task<IReadOnlyList<RepoSummary>> GetRepositoriesAsync();

    Task<RepoSummary> GetRepositoryAsync(string owner, string name);

    Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(string owner, string name);

    /// <summary>Lädt den kompletten Datei-Baum eines Commits mit einer einzigen Anfrage.</summary>
    Task<TreeSnapshot> GetTreeAsync(string owner, string name, string commitSha);

    /// <summary>Lädt den Rohinhalt einer Datei über ihre Blob-SHA (auch Dateien über 1 MB).</summary>
    Task<byte[]> GetBlobAsync(string owner, string name, string blobSha);

    /// <summary>Aktueller Commit, auf den der Branch zeigt.</summary>
    Task<string> GetBranchHeadAsync(string owner, string name, string branch);

    /// <summary>Legt einen neuen Baum auf Basis von <paramref name="baseTreeSha"/> an und liefert seine SHA.</summary>
    Task<string> CreateTreeAsync(string owner, string name, string baseTreeSha, IReadOnlyList<TreeChange> changes);

    Task<string> CreateCommitAsync(string owner, string name, string message, string treeSha, string parentSha);

    /// <summary>Setzt den Branch auf den Commit. Schlägt mit 422 fehl, wenn das kein Fast-Forward ist.</summary>
    Task UpdateBranchAsync(string owner, string name, string branch, string commitSha);

    Task CreateBranchAsync(string owner, string name, string branch, string commitSha);

    /// <summary>Legt in einem leeren Repository die erste Datei und damit den ersten Commit an.</summary>
    Task CreateFirstFileAsync(string owner, string name, string path, string content, string message);

    /// <summary>Erstellt einen Pull Request und liefert dessen URL.</summary>
    Task<string> CreatePullRequestAsync(string owner, string name, string title, string body, string head, string baseBranch);
}

/// <param name="Content">Neuer Inhalt; null löscht die Datei.</param>
public sealed record TreeChange(string Path, string Mode, string? Content);

public sealed record TreeSnapshot(string Sha, List<TreeEntry> Entries, bool Truncated);
