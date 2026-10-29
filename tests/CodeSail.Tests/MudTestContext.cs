using Bunit;
using MudBlazor.Services;

namespace CodeSail.Tests;

/// <summary>
/// bUnit-Kontext mit MudBlazor-Diensten. Einige davon können nur asynchron entsorgt werden,
/// deshalb übernimmt xUnit das Aufräumen über <see cref="IAsyncLifetime"/>.
/// </summary>
public abstract class MudTestContext : BunitContext, IAsyncLifetime
{
    protected MudTestContext()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();
}
