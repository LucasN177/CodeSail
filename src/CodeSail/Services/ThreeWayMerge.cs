namespace CodeSail.Services;

/// <summary>Ergebnis eines Drei-Wege-Merges; <see cref="Conflicts"/> zählt die markierten Stellen.</summary>
public sealed record MergeResult(string Text, int Conflicts);

/// <summary>
/// Zeilenbasierter Drei-Wege-Merge wie "git merge-file": Änderungen beider Seiten gegenüber dem gemeinsamen
/// Ausgangsstand werden übernommen; wo beide Seiten dieselben (oder direkt benachbarte) Zeilen unterschiedlich
/// ändern, entsteht ein Konflikt mit Git-üblichen Markierungen.
/// </summary>
public static class ThreeWayMerge
{
    public const string MineMarker = "<<<<<<<";
    public const string Separator = "=======";
    public const string TheirsMarker = ">>>>>>>";

    /// <summary>Ersetzt <c>Base[BaseStart..BaseEnd)</c> durch <see cref="Lines"/>.</summary>
    private sealed record Hunk(int BaseStart, int BaseEnd, string[] Lines, bool Mine);

    public static MergeResult Merge(string baseText, string mine, string theirs, string mineLabel = "Deine Änderung", string theirsLabel = "GitHub")
    {
        if (mine == theirs)
            return new MergeResult(mine, 0);
        if (mine == baseText)
            return new MergeResult(theirs, 0);
        if (theirs == baseText)
            return new MergeResult(mine, 0);

        var b = baseText.Split('\n');
        var hunks = Diff(b, mine.Split('\n'), isMine: true)
            .Concat(Diff(b, theirs.Split('\n'), isMine: false))
            .OrderBy(h => h.BaseStart).ThenBy(h => h.BaseEnd)
            .ToList();

        var output = new List<string>();
        var conflicts = 0;
        var position = 0;
        var index = 0;
        while (index < hunks.Count)
        {
            // Überlappende oder aneinandergrenzende Änderungen zu einer Gruppe zusammenfassen
            var group = new List<Hunk> { hunks[index++] };
            var low = group[0].BaseStart;
            var high = group[0].BaseEnd;
            while (index < hunks.Count && hunks[index].BaseStart <= high)
            {
                high = Math.Max(high, hunks[index].BaseEnd);
                group.Add(hunks[index++]);
            }

            output.AddRange(b[position..low]);
            var mineLines = Apply(b, low, high, group.Where(h => h.Mine));
            var theirLines = Apply(b, low, high, group.Where(h => !h.Mine));

            if (group.All(h => h.Mine))
                output.AddRange(mineLines);
            else if (group.All(h => !h.Mine))
                output.AddRange(theirLines);
            else if (mineLines.SequenceEqual(theirLines))
                output.AddRange(mineLines);
            else
            {
                conflicts++;
                output.Add($"{MineMarker} {mineLabel}");
                output.AddRange(mineLines);
                output.Add(Separator);
                output.AddRange(theirLines);
                output.Add($"{TheirsMarker} {theirsLabel}");
            }
            position = high;
        }
        output.AddRange(b[position..]);

        return new MergeResult(string.Join('\n', output), conflicts);
    }

    /// <summary>true, wenn der Text noch nicht aufgelöste Konfliktmarkierungen enthält.</summary>
    public static bool HasConflictMarkers(string text)
    {
        var lines = text.Split('\n');
        return lines.Any(l => l.StartsWith(MineMarker + " ", StringComparison.Ordinal))
            && lines.Any(l => l.TrimEnd('\r') == Separator)
            && lines.Any(l => l.StartsWith(TheirsMarker, StringComparison.Ordinal));
    }

    /// <summary>Erste Zeile (1-basiert) mit einer Konfliktmarkierung, für den Sprung im Editor.</summary>
    public static int? FirstConflictLine(string text)
    {
        var lines = text.Split('\n');
        var index = Array.FindIndex(lines, l => l.StartsWith(MineMarker + " ", StringComparison.Ordinal));
        return index < 0 ? null : index + 1;
    }

    private static List<string> Apply(string[] b, int low, int high, IEnumerable<Hunk> hunks)
    {
        var result = new List<string>();
        var position = low;
        foreach (var hunk in hunks.OrderBy(h => h.BaseStart))
        {
            result.AddRange(b[position..hunk.BaseStart]);
            result.AddRange(hunk.Lines);
            position = hunk.BaseEnd;
        }
        result.AddRange(b[position..high]);
        return result;
    }

    /// <summary>Unterschiede als Ersetzungsblöcke gegenüber <paramref name="a"/> (Myers-Algorithmus).</summary>
    private static List<Hunk> Diff(string[] a, string[] b, bool isMine)
    {
        var hunks = new List<Hunk>();
        var (i, j) = (0, 0);
        int? start = null;
        var inserted = new List<string>();

        foreach (var op in EditScript(a, b))
        {
            if (op == Op.Equal)
            {
                if (start is { } s)
                {
                    hunks.Add(new Hunk(s, i, [.. inserted], isMine));
                    start = null;
                    inserted.Clear();
                }
                i++;
                j++;
                continue;
            }

            start ??= i;
            if (op == Op.Delete)
                i++;
            else
            {
                inserted.Add(b[j]);
                j++;
            }
        }
        if (start is { } last)
            hunks.Add(new Hunk(last, i, [.. inserted], isMine));
        return hunks;
    }

    private enum Op
    {
        Equal,
        Delete,
        Insert
    }

    /// <summary>Kürzeste Folge von Gleich/Löschen/Einfügen nach Myers (O((N+M)·D)).</summary>
    private static List<Op> EditScript(string[] a, string[] b)
    {
        var n = a.Length;
        var m = b.Length;
        var max = n + m;
        var v = new int[2 * max + 2];
        var trace = new List<int[]>();

        for (var d = 0; d <= max; d++)
        {
            trace.Add((int[])v.Clone());
            for (var k = -d; k <= d; k += 2)
            {
                var x = k == -d || (k != d && v[max + k - 1] < v[max + k + 1])
                    ? v[max + k + 1]
                    : v[max + k - 1] + 1;
                var y = x - k;
                while (x < n && y < m && a[x] == b[y])
                {
                    x++;
                    y++;
                }
                v[max + k] = x;
                if (x >= n && y >= m)
                    return Backtrack(trace, a, b, max, d);
            }
        }
        return [];
    }

    private static List<Op> Backtrack(List<int[]> trace, string[] a, string[] b, int max, int dEnd)
    {
        var ops = new List<Op>();
        var x = a.Length;
        var y = b.Length;
        for (var d = dEnd; d > 0; d--)
        {
            var v = trace[d];
            var k = x - y;
            var prevK = k == -d || (k != d && v[max + k - 1] < v[max + k + 1]) ? k + 1 : k - 1;
            var prevX = v[max + prevK];
            var prevY = prevX - prevK;
            while (x > prevX && y > prevY)
            {
                ops.Add(Op.Equal);
                x--;
                y--;
            }
            ops.Add(x == prevX ? Op.Insert : Op.Delete);
            (x, y) = (prevX, prevY);
        }
        while (x > 0 && y > 0)
        {
            ops.Add(Op.Equal);
            x--;
            y--;
        }
        ops.Reverse();
        return ops;
    }
}
