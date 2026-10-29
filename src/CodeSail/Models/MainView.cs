namespace CodeSail.Models;

/// <summary>Ansicht im Hauptbereich anstelle des Editors (immer höchstens eine gleichzeitig).</summary>
public abstract record MainView;

public sealed record PullRequestMainView(int Number) : MainView;

/// <summary>Bild oder andere Binärdatei aus dem Repository.</summary>
public sealed record BinaryMainView(string Path) : MainView;

public sealed record CommitMainView(string Sha) : MainView;

public sealed record FileHistoryMainView(string Path) : MainView;

public sealed record BlameMainView(string Path) : MainView;
