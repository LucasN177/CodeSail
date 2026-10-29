using CodeSail.Models;
using CodeSail.Services.GitHub;
using CodeSail.Services.Storage;

namespace CodeSail.Services.Preview;

/// <summary>Erstellt die Vorschau einer Datei und lädt dafür eingebundene Dateien aus Cache oder GitHub.</summary>
public sealed class PreviewService(IGitHubService gitHub, IFileCache cache)
{
    /// <summary>Größere Dateien werden nicht eingebettet, damit die Vorschau flüssig bleibt.</summary>
    private const int MaxAssetBytes = 5 * 1024 * 1024;

    // Blobs ändern sich nie (die SHA ist der Inhalt), daher reicht ein einfacher Speicher pro Sitzung
    private readonly Dictionary<string, byte[]> _blobs = [];

    public async Task<string> BuildAsync(CachedFile file, RepoTree tree)
    {
        var kind = PreviewBuilder.KindOf(file.Path);
        if (kind == PreviewKind.None)
            throw new NotSupportedException("Für diesen Dateityp gibt es keine Vorschau.");

        var html = kind == PreviewKind.Markdown ? PreviewBuilder.RenderMarkdown(file.Content) : file.Content;

        var assets = new Dictionary<string, PreviewAsset>();
        foreach (var reference in PreviewBuilder.FindLocalReferences(html))
        {
            if (PreviewBuilder.ResolvePath(file.Path, reference) is { } path
                && await LoadAssetAsync(file, path, tree) is { } asset)
                assets[reference] = asset;
        }

        return PreviewBuilder.BuildDocument(PreviewBuilder.EmbedAssets(html, assets), kind, file.Name);
    }

    private async Task<PreviewAsset?> LoadAssetAsync(CachedFile from, string path, RepoTree tree)
    {
        // Lokale Fassung zuerst, damit auch noch nicht committete CSS-/JS-Änderungen sichtbar sind
        var local = await cache.GetFileAsync(CacheKeys.File(from.Owner, from.Repo, from.Branch, path));
        if (local is not null)
            return local.Deleted ? null : new PreviewAsset(path, System.Text.Encoding.UTF8.GetBytes(local.Content));

        if (!tree.BlobShas.TryGetValue(path, out var sha))
            return null;

        if (!_blobs.TryGetValue(sha, out var bytes))
        {
            try
            {
                bytes = await gitHub.GetBlobAsync(from.Owner, from.Repo, sha);
            }
            catch (Exception)
            {
                // Offline oder nicht ladbar: der Verweis bleibt dann einfach unaufgelöst
                return null;
            }
            _blobs[sha] = bytes;
        }

        return bytes.Length > MaxAssetBytes ? null : new PreviewAsset(path, bytes);
    }
}
