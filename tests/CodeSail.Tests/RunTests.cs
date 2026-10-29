using Bunit;
using CodeSail.Components;
using CodeSail.Models;
using CodeSail.Services.Run;

namespace CodeSail.Tests;

public class RunSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("src/app.js", RunLanguage.JavaScript)]
    [InlineData("lib/util.MJS", RunLanguage.JavaScript)]
    [InlineData("main.ts", RunLanguage.TypeScript)]
    [InlineData("types.mts", RunLanguage.TypeScript)]
    [InlineData("App.tsx", RunLanguage.None)]
    [InlineData("README.md", RunLanguage.None)]
    public void LanguageOf_DetectsRunnableFiles(string path, RunLanguage language) =>
        Assert.Equal(language, RunSession.LanguageOf(path));

    [Fact]
    public void Apply_CollectsOutputAndFinishesOnDone()
    {
        var session = new RunSession(1, "app.js", Start);

        session.Apply(
        [
            new RunMessage("status", Text: "Läuft …"),
            new RunMessage("console", "log", "Hallo"),
            new RunMessage("console", "warn", "Achtung"),
            new RunMessage("done")
        ], Start.AddMilliseconds(250));

        Assert.Equal(RunStatus.Finished, session.Status);
        Assert.Null(session.StatusText);
        Assert.Equal(TimeSpan.FromMilliseconds(250), session.Duration);
        Assert.Equal(["Hallo", "Achtung"], session.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Apply_ErrorWithLineEndsAsFailed()
    {
        var session = new RunSession(1, "app.ts", Start);

        session.Apply(
        [
            new RunMessage("problem", "warn", "Typfehler", Line: 3, Column: 7),
            new RunMessage("error", "error", "Error: boom", Line: 9, Column: 3, Stack: "at app.ts:9:3"),
            new RunMessage("end", Status: "failed")
        ], Start.AddSeconds(1));

        Assert.Equal(RunStatus.Failed, session.Status);
        Assert.Equal(1, session.ErrorCount);
        Assert.Equal((9, 3), (session.Lines[1].Line, session.Lines[1].Column));
        Assert.Equal("at app.ts:9:3", session.Lines[1].Detail);
    }

    [Fact]
    public void OutputAfterDone_IsStillShown()
    {
        var session = new RunSession(1, "app.js", Start);
        session.Apply([new RunMessage("done")], Start);

        session.Apply([new RunMessage("console", "log", "aus einem Timer")], Start.AddSeconds(2));

        Assert.Equal(RunStatus.Finished, session.Status);
        Assert.Single(session.Lines);
    }

    [Fact]
    public void ConsoleClear_RemovesEarlierLines()
    {
        var session = new RunSession(1, "app.js", Start);

        session.Apply([new RunMessage("console", "log", "a"), new RunMessage("clear"), new RunMessage("console", "log", "b")], Start);

        Assert.Equal(["b"], session.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Output_IsLimited()
    {
        var session = new RunSession(1, "app.js", Start);

        session.Apply(Enumerable.Range(0, RunSession.MaxLines + 50).Select(i => new RunMessage("console", "log", $"{i}")), Start);

        Assert.Equal(RunSession.MaxLines, session.Lines.Count);
    }

    [Fact]
    public void Stop_OnlyAffectsRunningSession()
    {
        var running = new RunSession(1, "a.js", Start);
        var finished = new RunSession(2, "b.js", Start);
        finished.Apply([new RunMessage("done")], Start);

        running.Stop(Start.AddSeconds(3));
        finished.Stop(Start.AddSeconds(3));

        Assert.Equal(RunStatus.Stopped, running.Status);
        Assert.Equal(RunStatus.Finished, finished.Status);
    }
}

public class RunPanelTests : MudTestContext
{
    private static CachedFile File(string path) =>
        new("k", "o", "r", "main", path, "console.log(1)", "console.log(1)", "sha", DateTimeOffset.UtcNow);

    [Fact]
    public async Task ShowsOutputAndJumpsToErrorLine()
    {
        (int Line, int Column)? selected = null;
        var cut = Render<RunPanel>(p => p.Add(x => x.LocationSelected, location => selected = location));

        await cut.InvokeAsync(() => cut.Instance.RunAsync(File("src/app.js")));
        await cut.InvokeAsync(() => cut.Instance.OnRunMessages(1,
        [
            new RunMessage("console", "log", "Hallo Welt"),
            new RunMessage("error", "error", "Error: boom", Line: 4, Column: 9),
            new RunMessage("end", Status: "failed")
        ]));

        Assert.Contains("Hallo Welt", cut.Markup);
        Assert.Contains("app.js:4", cut.Markup);
        Assert.Contains("mit Fehler beendet", cut.Markup);

        cut.Find(".run-line.clickable").Click();
        Assert.Equal((4, 9), selected);
    }

    [Fact]
    public async Task IgnoresMessagesFromEarlierRuns()
    {
        var cut = Render<RunPanel>();
        await cut.InvokeAsync(() => cut.Instance.RunAsync(File("a.js")));
        await cut.InvokeAsync(() => cut.Instance.RunAsync(File("b.js")));

        await cut.InvokeAsync(() => cut.Instance.OnRunMessages(1, [new RunMessage("console", "log", "alt")]));

        Assert.DoesNotContain("alt", cut.Markup);
    }

    [Fact]
    public async Task DoesNothingForFilesThatCannotRun()
    {
        var cut = Render<RunPanel>();

        await cut.InvokeAsync(() => cut.Instance.RunAsync(File("README.md")));

        Assert.False(cut.Instance.IsRunning);
        Assert.Contains("Cmd+Enter", cut.Markup);
    }
}
