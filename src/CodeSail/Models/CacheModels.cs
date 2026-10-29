using CodeSail.Services.GitHub;

namespace CodeSail.Models;

/// <summary>
/// Lokale Kopie einer Datei. <see cref="OriginalContent"/> ist der Stand von GitHub (Blob <see cref="BlobSha"/>).
/// Neue Dateien haben keine <see cref="BlobSha"/>; gelöschte sind mit <see cref="Deleted"/> markiert.
/// </summary>
public sealed record CachedFile(
    string Key,
    string Owner,
    string Repo,
    string Branch,
    string Path,
    string Content,
    string OriginalContent,
    string? BlobSha,
    DateTimeOffset UpdatedAt,
    bool Deleted = false)
{
    public bool IsNew => BlobSha is null;

    public bool IsDirty => Deleted || IsNew || Content != OriginalContent;

    public ChangeKind Change => Deleted ? ChangeKind.Deleted : IsNew ? ChangeKind.Added : ChangeKind.Modified;

    public string Name => Path[(Path.LastIndexOf('/') + 1)..];
}

public enum ChangeKind
{
    Modified,
    Added,
    Deleted
}

public sealed record CachedTree(string Key, string Sha, List<TreeEntry> Entries, bool Truncated);

public sealed record CachedRepo(string Key, string DefaultBranch, List<BranchInfo> Branches);

/// <summary>Zuletzt geladene Repository-Liste; liegt im Store "repos" unter <see cref="CacheKeys.RepoList"/>.</summary>
public sealed record CachedRepoList(string Key, List<RepoSummary> Repos);

/// <summary>Schlüssel im IndexedDB-Cache. Owner und Repo werden kleingeschrieben, da GitHub sie nicht unterscheidet.</summary>
public static class CacheKeys
{
    /// <summary>Kann nicht mit einem Repo-Schlüssel kollidieren, da diese immer ein "/" enthalten.</summary>
    public const string RepoList = "__repo-list";

    public static string Repo(string owner, string repo) => $"{owner}/{repo}".ToLowerInvariant();

    public static string Branch(string owner, string repo, string branch) => $"{Repo(owner, repo)}@{branch}";

    public static string File(string owner, string repo, string branch, string path) =>
        $"{Branch(owner, repo, branch)}:{path}";
}
