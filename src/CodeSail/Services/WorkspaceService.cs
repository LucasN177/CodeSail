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
    public async Task<Loaded<IReadOnlyList<RepoSummary>>> LoadRepositoriesAsync()
    {
        try
        {
            var repos = await gitHub.GetRepositoriesAsync();
            await cache.SaveRepoListAsync(new CachedRepoList(CacheKeys.RepoList, repos.ToList()));
            return new(repos, FromCache: false);
        }
        catch (Exception ex) when (IsOffline(ex))
        {
            var cached = await cache.GetRepoListAsync();
            if (cached is null)
                throw;
            return new(cached.Repos, FromCache: true);
        }
    }

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
        (await GetChangesAsync(owner, repo, branch))
            .Where(f => !f.Deleted)
            .OrderBy(f => f.UpdatedAt)
            .ToList();

    /// <summary>Alle lokalen Änderungen eines Branches (geändert, neu, gelöscht), sortiert nach Pfad.</summary>
    public async Task<IReadOnlyList<CachedFile>> GetChangesAsync(string owner, string repo, string branch) =>
        (await cache.GetFilesAsync(CacheKeys.Branch(owner, repo, branch) + ":"))
            .Where(f => f.IsDirty)
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
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

    /// <summary>Stellt den Stand von GitHub wieder her. Neue Dateien verschwinden ganz (Rückgabe null).</summary>
    public async Task<CachedFile?> DiscardChangesAsync(CachedFile file)
    {
        if (file.IsNew)
        {
            await cache.RemoveFileAsync(file.Key);
            return null;
        }

        var restored = file with { Content = file.OriginalContent, Deleted = false, UpdatedAt = DateTimeOffset.UtcNow };
        await cache.SaveFileAsync(restored);
        return restored;
    }

    /// <param name="tree">Dateibaum inklusive lokaler Änderungen, um doppelte Pfade zu erkennen.</param>
    public async Task<CachedFile> CreateFileAsync(string owner, string repo, string branch, string path, RepoTree tree, string content = "")
    {
        path = NormalizePath(path);
        if (tree.FilePaths.Contains(path))
            throw new InvalidOperationException($"Es gibt bereits eine Datei {path}.");
        if (tree.Entries.Any(e => e.IsFolder && e.Path == path))
            throw new InvalidOperationException($"Es gibt bereits einen Ordner {path}.");

        var key = CacheKeys.File(owner, repo, branch, path);
        var file = await cache.GetFileAsync(key) is { Deleted: true } deleted
            // Eine gerade gelöschte Datei wird wiederhergestellt und gilt dann als geändert
            ? deleted with { Deleted = false, Content = content, UpdatedAt = DateTimeOffset.UtcNow }
            : new CachedFile(key, owner, repo, branch, path, content, "", null, DateTimeOffset.UtcNow);

        await cache.SaveFileAsync(file);
        return file;
    }

    /// <summary>Markiert die Datei als gelöscht. Neue, noch nie committete Dateien werden direkt entfernt.</summary>
    public async Task<CachedFile?> DeleteFileAsync(CachedFile file)
    {
        if (file.IsNew)
        {
            await cache.RemoveFileAsync(file.Key);
            return null;
        }

        var deleted = file with { Deleted = true, Content = file.OriginalContent, UpdatedAt = DateTimeOffset.UtcNow };
        await cache.SaveFileAsync(deleted);
        return deleted;
    }

    /// <summary>Umbenennen bzw. Verschieben: Git kennt das nur als neue Datei plus gelöschte Datei.</summary>
    public async Task<CachedFile> RenameFileAsync(CachedFile file, string newPath, RepoTree tree)
    {
        var created = await CreateFileAsync(file.Owner, file.Repo, file.Branch, newPath, tree, file.Content);
        await DeleteFileAsync(file);
        return created;
    }

    /// <summary>Blendet lokal gelöschte Dateien aus und neue Dateien ein. Ohne solche Änderungen bleibt der Baum unverändert.</summary>
    public static RepoTree ApplyLocalChanges(RepoTree tree, IEnumerable<CachedFile> changes)
    {
        var list = changes.ToList();
        var deleted = list.Where(f => f.Deleted).Select(f => f.Path).ToHashSet();
        var added = list.Where(f => f.IsNew && !tree.BlobShas.ContainsKey(f.Path)).Select(f => f.Path).ToList();
        if (deleted.Count == 0 && added.Count == 0)
            return tree;

        var entries = tree.Entries
            .Where(e => e.IsFolder || !deleted.Contains(e.Path))
            .Concat(added.Select(p => new TreeEntry(p, IsFolder: false)))
            .ToList();
        return RepoTree.FromEntries(tree.Sha, entries, tree.Truncated);
    }

    /// <summary>Legt in einem leeren Repository eine README.md als ersten Commit an.</summary>
    public Task InitializeRepositoryAsync(string owner, string repo) =>
        gitHub.CreateFirstFileAsync(owner, repo, "README.md", $"# {repo}\n", "Initial commit");

    /// <summary>Legt einen Branch ab dem aktuellen Stand auf GitHub an (lokale Änderungen bleiben auf dem alten Branch).</summary>
    public async Task<BranchInfo> CreateBranchAsync(string owner, string repo, string fromBranch, string name, IEnumerable<BranchInfo> existing)
    {
        name = ValidateBranchName(name, existing);
        var head = await gitHub.GetBranchHeadAsync(owner, repo, fromBranch);
        await gitHub.CreateBranchAsync(owner, repo, name, head);
        return new BranchInfo(name, head);
    }

    /// <summary>Prüft die wichtigsten Git-Regeln für Branch-Namen (git check-ref-format).</summary>
    public static string ValidateBranchName(string name, IEnumerable<BranchInfo> existing)
    {
        name = name.Trim();
        var invalid = name.Length == 0
            || name.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || "~^:?*[\\".Contains(c))
            || name.Contains("..") || name.Contains("//") || name.Contains("@{")
            || name.StartsWith('/') || name.EndsWith('/') || name.StartsWith('-')
            || name.EndsWith('.') || name.EndsWith(".lock", StringComparison.Ordinal)
            || name.Split('/').Any(s => s.StartsWith('.'));
        if (invalid)
            throw new InvalidOperationException(
                $"\"{name}\" ist kein gültiger Branch-Name. Erlaubt sind z. B. feature/neue-seite (keine Leerzeichen, kein \"..\").");

        if (existing.Any(b => b.Name == name))
            throw new InvalidOperationException($"Der Branch {name} existiert bereits.");

        return name;
    }

    /// <summary>Vereinheitlicht einen eingegebenen Pfad und weist ungültige Pfade zurück.</summary>
    public static string NormalizePath(string path)
    {
        var segments = path.Trim().Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Any(s => s is "." or ".." or ".git"))
            throw new InvalidOperationException("Ungültiger Dateipfad.");
        return string.Join('/', segments);
    }

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
