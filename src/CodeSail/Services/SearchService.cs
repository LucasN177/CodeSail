using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using CodeSail.Models;
using CodeSail.Services.GitHub;
using CodeSail.Services.Storage;

namespace CodeSail.Services;

public sealed record SearchOptions(string Query, bool MatchCase = false, bool WholeWord = false, bool UseRegex = false);

/// <param name="Column">1-basiert, wie im Editor.</param>
/// <param name="PreviewStart">Position des Treffers innerhalb von <paramref name="Preview"/>.</param>
public sealed record SearchMatch(int Line, int Column, int Length, string Preview, int PreviewStart);

public sealed record SearchFileResult(string Path, IReadOnlyList<SearchMatch> Matches);

/// <param name="NotLoaded">Durchsuchbare Dateien, die noch nie geladen wurden und deshalb fehlen.</param>
public sealed record SearchResult(IReadOnlyList<SearchFileResult> Files, int TotalMatches, int SearchedFiles, int NotLoaded, bool Truncated);

/// <summary>
/// Volltextsuche über die lokalen Kopien eines Branches, inklusive nicht committeter Änderungen und offline.
/// Dateien, die noch nie geöffnet wurden, lassen sich gesammelt nachladen (danach liegen sie im Offline-Cache).
/// </summary>
public sealed class SearchService(WorkspaceService workspace, IFileCache cache)
{
    public const int MaxMatches = 2000;
    public const long MaxFileSize = 1024 * 1024;
    private const int PreviewLength = 160;

    // Pfade, die sich beim Laden als Binärdatei herausgestellt haben (pro Sitzung)
    private readonly ConcurrentDictionary<string, bool> _binary = new();

    /// <summary>Dateien, die die Suche berücksichtigt: keine bekannten Binärformate, nicht größer als 1 MB.</summary>
    public IReadOnlyList<TreeEntry> SearchableFiles(RepoTree tree) =>
        tree.Entries
            .Where(e => !e.IsFolder && !FileKinds.IsKnownBinary(e.Path) && (e.Size ?? 0) <= MaxFileSize && !_binary.ContainsKey(e.Path))
            .ToList();

    /// <summary>Durchsuchbare Dateien, die noch nicht im Cache liegen.</summary>
    public async Task<IReadOnlyList<TreeEntry>> MissingFilesAsync(string owner, string repo, string branch, RepoTree tree)
    {
        var cached = (await cache.GetFilesAsync(CacheKeys.Branch(owner, repo, branch) + ":")).Select(f => f.Path).ToHashSet();
        return SearchableFiles(tree).Where(e => !cached.Contains(e.Path)).ToList();
    }

    /// <summary>Lädt Dateien parallel nach (höchstens 6 gleichzeitig) und meldet den Fortschritt.</summary>
    public async Task DownloadAsync(string owner, string repo, string branch, IReadOnlyList<TreeEntry> files,
        IProgress<int> progress, CancellationToken cancellationToken)
    {
        // Bewusst ohne Parallel.ForEachAsync: WebAssembly hat keine Threads, gleichzeitig laufen hier nur die Anfragen
        var done = 0;
        using var slots = new SemaphoreSlim(6);
        await Task.WhenAll(files.Select(async entry =>
        {
            await slots.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await workspace.OpenFileAsync(owner, repo, branch, entry.Path, entry.Sha);
            }
            catch (NotSupportedException)
            {
                _binary[entry.Path] = true;
            }
            finally
            {
                slots.Release();
            }
            progress.Report(Interlocked.Increment(ref done));
        }));
    }

    public async Task<SearchResult> SearchAsync(string owner, string repo, string branch, RepoTree tree, SearchOptions options)
    {
        var regex = BuildRegex(options);
        var files = (await cache.GetFilesAsync(CacheKeys.Branch(owner, repo, branch) + ":"))
            .Where(f => !f.Deleted)
            .ToDictionary(f => f.Path);

        var searchable = SearchableFiles(tree).Select(e => e.Path).ToList();
        // Neue, noch nicht committete Dateien stehen nicht im Baum von GitHub
        searchable.AddRange(files.Values.Where(f => f.IsNew && !tree.BlobShas.ContainsKey(f.Path)).Select(f => f.Path));

        var results = new List<SearchFileResult>();
        var total = 0;
        var searched = 0;
        var notLoaded = 0;
        foreach (var path in searchable.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!files.TryGetValue(path, out var file))
            {
                notLoaded++;
                continue;
            }

            searched++;
            if (total >= MaxMatches)
                continue;

            var matches = Find(file.Content, regex, MaxMatches - total);
            if (matches.Count == 0)
                continue;
            results.Add(new SearchFileResult(path, matches));
            total += matches.Count;
        }

        return new SearchResult(results, total, searched, notLoaded, total >= MaxMatches);
    }

    /// <summary>Baut den Suchausdruck; ungültige reguläre Ausdrücke lösen <see cref="ArgumentException"/> aus.</summary>
    public static Regex BuildRegex(SearchOptions options)
    {
        var pattern = options.UseRegex ? options.Query : Regex.Escape(options.Query);
        if (options.WholeWord)
            pattern = $@"\b(?:{pattern})\b";
        var flags = RegexOptions.CultureInvariant | (options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        return new Regex(pattern, flags, TimeSpan.FromSeconds(1));
    }

    public static List<SearchMatch> Find(string content, Regex regex, int limit)
    {
        var result = new List<SearchMatch>();
        var lines = content.Split('\n');
        for (var i = 0; i < lines.Length && result.Count < limit; i++)
        {
            var line = lines[i].TrimEnd('\r');
            foreach (Match match in regex.Matches(line))
            {
                if (match.Length == 0)
                    continue;
                // Ausschnitt um den Treffer, damit lange Zeilen lesbar bleiben
                var start = Math.Max(0, match.Index - 40);
                var preview = line.Substring(start, Math.Min(PreviewLength, line.Length - start));
                var leading = preview.Length - preview.TrimStart().Length;
                result.Add(new SearchMatch(i + 1, match.Index + 1, match.Length,
                    preview.TrimStart(), match.Index - start - leading));
                if (result.Count >= limit)
                    break;
            }
        }
        return result;
    }
}
