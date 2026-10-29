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
    Task<string> CreatePullRequestAsync(string owner, string name, string title, string body, string head, string baseBranch, bool draft = false);

    Task DeleteBranchAsync(string owner, string name, string branch);

    /// <summary>Benennt einen Branch um; GitHub passt offene Pull Requests dabei automatisch an.</summary>
    Task RenameBranchAsync(string owner, string name, string branch, string newName);

    Task<BranchComparison> CompareAsync(string owner, string name, string baseBranch, string headBranch);

    /// <summary>Die 50 zuletzt aktualisierten Pull Requests.</summary>
    Task<IReadOnlyList<PullRequestSummary>> GetPullRequestsAsync(string owner, string name, PullRequestFilter filter);

    Task<PullRequestDetails> GetPullRequestAsync(string owner, string name, int number);

    Task<IReadOnlyList<PullRequestFile>> GetPullRequestFilesAsync(string owner, string name, int number);

    /// <param name="headSha">Nur mergen, wenn der Branch noch auf diesem Commit steht (sonst 409).</param>
    Task MergePullRequestAsync(string owner, string name, int number, MergeMethod method, string headSha);

    /// <summary>Schließt einen Pull Request bzw. öffnet ihn wieder.</summary>
    Task SetPullRequestOpenAsync(string owner, string name, int number, bool open);

    Task UpdatePullRequestAsync(string owner, string name, int number, string title, string body);

    /// <summary>Kommentare, Reviews und Zeilenkommentare eines Pull Requests, älteste zuerst.</summary>
    Task<IReadOnlyList<ConversationItem>> GetConversationAsync(string owner, string name, int number);

    Task AddCommentAsync(string owner, string name, int number, string body);

    Task SubmitReviewAsync(string owner, string name, int number, ReviewAction action, string body);

    /// <summary>Status-Checks und Check-Runs eines Commits (CI); fehlende Leserechte werden vermerkt statt geworfen.</summary>
    Task<CiStatus> GetCiStatusAsync(string owner, string name, string sha);

    /// <summary>Commits eines Branches (neueste zuerst), optional nur die einer Datei; 30 pro Seite.</summary>
    Task<IReadOnlyList<CommitSummary>> GetCommitsAsync(string owner, string name, string branch, string? path, int page);

    Task<CommitDetails> GetCommitAsync(string owner, string name, string sha);

    /// <summary>
    /// Übernimmt <paramref name="head"/> per Merge-Commit in <paramref name="baseBranch"/> (auf GitHub).
    /// Liefert die SHA des Merge-Commits oder null, wenn nichts zu übernehmen war; 409 bei Konflikten.
    /// </summary>
    Task<string?> MergeBranchAsync(string owner, string name, string baseBranch, string head, string message);

    /// <summary>Zeilenherkunft einer Datei auf einem Branch (nur über die GraphQL-API verfügbar).</summary>
    Task<IReadOnlyList<BlameRange>> GetBlameAsync(string owner, string name, string branch, string path);
}

/// <param name="Content">Neuer Inhalt; null löscht die Datei.</param>
public sealed record TreeChange(string Path, string Mode, string? Content);

public sealed record TreeSnapshot(string Sha, List<TreeEntry> Entries, bool Truncated);
