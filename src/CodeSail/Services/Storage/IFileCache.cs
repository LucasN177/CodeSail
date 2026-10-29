using CodeSail.Models;

namespace CodeSail.Services.Storage;

/// <summary>Offline-Cache für Dateien, Dateibäume und Branch-Listen (IndexedDB).</summary>
public interface IFileCache
{
    Task<CachedFile?> GetFileAsync(string key);

    Task SaveFileAsync(CachedFile file);

    Task RemoveFileAsync(string key);

    /// <summary>Alle Dateien, deren Schlüssel mit <paramref name="keyPrefix"/> beginnt ("" = alle).</summary>
    Task<IReadOnlyList<CachedFile>> GetFilesAsync(string keyPrefix);

    Task<CachedTree?> GetTreeAsync(string key);

    Task SaveTreeAsync(CachedTree tree);

    Task<CachedRepo?> GetRepoAsync(string key);

    Task SaveRepoAsync(CachedRepo repo);

    Task ClearAsync();

    /// <summary>Bittet den Browser, den Cache nicht automatisch zu löschen.</summary>
    Task<bool> RequestPersistenceAsync();

    /// <summary>Belegter Speicher dieser App in Bytes, sofern der Browser das verrät.</summary>
    Task<long?> EstimateUsageAsync();
}
