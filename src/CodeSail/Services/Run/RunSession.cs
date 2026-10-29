namespace CodeSail.Services.Run;

public enum RunLanguage
{
    None,
    JavaScript,
    TypeScript
}

public enum RunStatus
{
    Idle,
    Running,
    Finished,
    Failed,
    Stopped
}

/// <summary>Nachricht aus wwwroot/js/runner.js (Konsole, Fehler, Status).</summary>
public sealed record RunMessage(
    string Kind,
    string? Level = null,
    string? Text = null,
    int? Line = null,
    int? Column = null,
    string? Stack = null,
    string? Status = null);

/// <param name="Level">log, info, warn, error, debug</param>
/// <param name="Line">Zeile in der ausgeführten Datei, falls bekannt (antippbar).</param>
public sealed record OutputLine(string Level, string Text, int? Line = null, int? Column = null, string? Detail = null);

/// <summary>Zustand eines Laufs: sammelt die Ausgabe und leitet den Status aus den Nachrichten ab.</summary>
public sealed class RunSession
{
    public const int MaxLines = 5000;

    private readonly List<OutputLine> _lines = [];
    private DateTimeOffset _started;

    public RunSession(int id, string fileName, DateTimeOffset started)
    {
        Id = id;
        FileName = fileName;
        _started = started;
    }

    public int Id { get; }

    public string FileName { get; }

    public RunStatus Status { get; private set; } = RunStatus.Running;

    /// <summary>Kurzer Zustandstext, z. B. "TypeScript wird übersetzt …".</summary>
    public string? StatusText { get; private set; }

    public TimeSpan? Duration { get; private set; }

    public IReadOnlyList<OutputLine> Lines => _lines;

    public int ErrorCount => _lines.Count(l => l.Level == "error");

    public static RunLanguage LanguageOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".js" or ".mjs" or ".cjs" => RunLanguage.JavaScript,
        ".ts" or ".mts" or ".cts" => RunLanguage.TypeScript,
        _ => RunLanguage.None
    };

    public void Apply(IEnumerable<RunMessage> messages, DateTimeOffset now)
    {
        foreach (var message in messages)
        {
            switch (message.Kind)
            {
                case "status":
                    StatusText = message.Text;
                    break;
                case "console":
                    Add(new OutputLine(message.Level ?? "log", message.Text ?? ""));
                    break;
                case "problem":
                case "error":
                    Add(new OutputLine(message.Level ?? "error", message.Text ?? "Unbekannter Fehler", message.Line, message.Column, message.Stack));
                    break;
                case "clear":
                    _lines.Clear();
                    break;
                case "done":
                    // Timer und Promises können danach noch Ausgaben liefern; der Lauf gilt trotzdem als fertig
                    if (Status == RunStatus.Running)
                        End(RunStatus.Finished, now);
                    break;
                case "end":
                    End(message.Status == "failed" ? RunStatus.Failed : RunStatus.Finished, now);
                    break;
            }
        }
    }

    public void Stop(DateTimeOffset now)
    {
        if (Status == RunStatus.Running)
            End(RunStatus.Stopped, now);
    }

    private void End(RunStatus status, DateTimeOffset now)
    {
        Status = status;
        StatusText = null;
        Duration ??= now - _started;
    }

    private void Add(OutputLine line)
    {
        if (_lines.Count < MaxLines)
            _lines.Add(line);
    }
}
