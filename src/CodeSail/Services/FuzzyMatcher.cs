namespace CodeSail.Services;

/// <summary>Unscharfe Dateisuche wie Cmd+P in VS Code: "wsvc" findet "WorkspaceService.cs".</summary>
public static class FuzzyMatcher
{
    /// <summary>
    /// Bewertet, wie gut <paramref name="query"/> zu <paramref name="path"/> passt; null bei keinem Treffer.
    /// Alle Zeichen der Suche müssen in dieser Reihenfolge vorkommen. Höher ist besser: Treffer im Dateinamen,
    /// am Wortanfang und zusammenhängende Zeichen zählen mehr, lange Pfade etwas weniger.
    /// </summary>
    public static int? Score(string query, string path)
    {
        query = query.Replace(" ", "");
        if (query.Length == 0)
            return 0;

        var nameStart = path.LastIndexOf('/') + 1;
        var score = 0;
        var position = 0;
        var previous = -2;

        foreach (var wanted in query)
        {
            // Direkt folgendes Zeichen oder ein Wortanfang im Dateinamen ist besser als der erstbeste Treffer
            var next = FindNext(path, wanted, position, i => i == previous + 1)
                ?? FindNext(path, wanted, Math.Max(position, nameStart), i => i == nameStart || IsWordStart(path, i))
                ?? FindNext(path, wanted, position, _ => true);
            if (next is not int found)
                return null;

            score += 1;
            if (found >= nameStart)
                score += 3;
            if (found == previous + 1)
                score += 4;
            if (found == 0 || IsWordStart(path, found))
                score += 3;

            previous = found;
            position = found + 1;
        }

        // Exakter Dateiname schlägt alles
        if (string.Equals(path[nameStart..], query, StringComparison.OrdinalIgnoreCase))
            score += 50;

        return score * 100 - path.Length;
    }

    public static IReadOnlyList<string> Search(string query, IEnumerable<string> paths, int limit = 50) =>
        paths.Select(p => (Path: p, Score: Score(query, p)))
            .Where(x => x.Score is not null)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(x => x.Path)
            .ToList();

    private static int? FindNext(string path, char wanted, int from, Func<int, bool> accept)
    {
        for (var i = from; i < path.Length; i++)
        {
            if (char.ToLowerInvariant(path[i]) == char.ToLowerInvariant(wanted) && accept(i))
                return i;
        }
        return null;
    }

    private static bool IsWordStart(string path, int index)
    {
        if (index == 0)
            return true;
        var before = path[index - 1];
        return before is '/' or '.' or '-' or '_' or ' ' || (char.IsUpper(path[index]) && char.IsLower(before));
    }
}
