using System.Text;
using CodeSail.Models;
using CodeSail.Services.GitHub;
using CodeSail.Services.Storage;

namespace CodeSail.Services;

/// <summary>Ergebnis eines Ladevorgangs. <see cref="FromCache"/> ist true, wenn GitHub nicht erreichbar war.</summary>
public sealed record Loaded<T>(T Value, bool FromCache);

public sealed record RepoBranches(string DefaultBranch, IReadOnlyList<BranchInfo> Branches);

/// <summary>
/// Lädt Branches, Dateibäume und Dateien bevorzugt von GitHub und hält eine Kopie im Offline-Cache.
/// Ohne Verbindung wird auf den Cache ausgewichen; lokale Änderungen haben immer Vorrang.
/// </summary>
public sealed class WorkspaceService(IGitHubService gitHub, IFileCache cache)
{
    public async Task<Loaded<RepoBranches>> LoadBranchesAsync(string owner, string repo)
    {
        var key = CacheKeys.Repo(owner, repo);
        try
        {
            var summary = await gitHub.GetRepositoryAsync(owner, repo);
            var branches = await gitHub.GetBranchesAsync(owner, repo);
            await cache.SaveRepoAsync(new CachedRepo(key, summary.DefaultBranch, branches.ToList()));
            return new(new RepoBranches(summary.DefaultBranch, branches), FromCache: false);
        }
        catch (Exception ex) when (IsOffline(ex))
        {
            var cached = await cache.GetRepoAsync(key);
            if (cached is null)
                throw;
            return new(new RepoBranches(cached.DefaultBranch, cached.Branches), FromCache: true);
        }
    }

    public async Task<Loaded<RepoTree>> LoadTreeAsync(string owner, string repo, BranchInfo branch)
    {
        var key = CacheKeys.Branch(owner, repo, branch.Name);
        try
        {
            var snapshot = await gitHub.GetTreeAsync(owner, repo, branch.CommitSha);
            await cache.SaveTreeAsync(new CachedTree(key, snapshot.Sha, snapshot.Entries, snapshot.Truncated));
            return new(RepoTree.FromEntries(snapshot.Sha, snapshot.Entries, snapshot.Truncated), FromCache: false);
        }
        catch (Exception ex) when (IsOffline(ex))
        {
            var cached = await cache.GetTreeAsync(key);
            if (cached is null)
                throw;
            return new(RepoTree.FromEntries(cached.Sha, cached.Entries, cached.Truncated), FromCache: true);
        }
    }

    /// <summary>Lokal geänderte Dateien eines Branches, z. B. um sie nach einem Neustart wieder zu öffnen.</summary>
    public async Task<IReadOnlyList<CachedFile>> GetDirtyFilesAsync(string owner, string repo, string branch) =>
        (await cache.GetFilesAsync(CacheKeys.Branch(owner, repo, branch) + ":"))
            .Where(f => f.IsDirty)
            .OrderBy(f => f.UpdatedAt)
            .ToList();

    public async Task<int> CountDirtyFilesAsync() =>
        (await cache.GetFilesAsync("")).Count(f => f.IsDirty);

    /// <param name="remoteBlobSha">Aktuelle Blob-SHA laut Dateibaum; null, wenn unbekannt.</param>
    public async Task<CachedFile> OpenFileAsync(string owner, string repo, string branch, string path, string? remoteBlobSha)
    {
        var key = CacheKeys.File(owner, repo, branch, path);
        var cached = await cache.GetFileAsync(key);

        // Lokale Änderungen nie überschreiben; unveränderte Kopien nur, wenn GitHub neuer ist
        if (cached is not null && (cached.IsDirty || remoteBlobSha is null || cached.BlobSha == remoteBlobSha))
            return cached;

        if (remoteBlobSha is null)
            throw new InvalidOperationException("Die Datei ist weder im Cache noch im Dateibaum vorhanden.");

        byte[] bytes;
        try
        {
            bytes = await gitHub.GetBlobAsync(owner, repo, remoteBlobSha);
        }
        catch (Exception ex) when (IsOffline(ex) && cached is not null)
        {
            return cached;
        }

        var content = DecodeText(bytes)
            ?? throw new NotSupportedException("Binärdateien können nicht im Editor geöffnet werden.");

        var file = new CachedFile(key, owner, repo, branch, path, content, content, remoteBlobSha, DateTimeOffset.UtcNow);
        await cache.SaveFileAsync(file);
        return file;
    }

    public async Task<CachedFile> SaveContentAsync(CachedFile file, string content)
    {
        if (content == file.Content)
            return file;

        var updated = file with { Content = content, UpdatedAt = DateTimeOffset.UtcNow };
        await cache.SaveFileAsync(updated);
        return updated;
    }

    public Task<CachedFile> DiscardChangesAsync(CachedFile file) => SaveContentAsync(file, file.OriginalContent);

    public Task ClearCacheAsync() => cache.ClearAsync();

    /// <summary>Dekodiert UTF-8-Text. Liefert null für Binärdateien (Null-Bytes am Anfang).</summary>
    public static string? DecodeText(byte[] bytes)
    {
        if (bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).Contains((byte)0))
            return null;

        // Ein vorhandenes BOM bleibt als Zeichen erhalten, damit es beim Commit nicht verloren geht
        return Encoding.UTF8.GetString(bytes);
    }

    private static bool IsOffline(Exception ex) => ex is HttpRequestException or TaskCanceledException;
}
