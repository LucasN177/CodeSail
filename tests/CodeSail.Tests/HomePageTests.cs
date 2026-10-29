using Bunit;
using CodeSail.Pages;
using CodeSail.Services;
using CodeSail.Services.GitHub;
using CodeSail.Services.Storage;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace CodeSail.Tests;

public class HomePageTests : MudTestContext
{
    private readonly FakeTokenStore _store = new();
    private readonly FakeGitHubService _gitHub;

    public HomePageTests()
    {
        _gitHub = new FakeGitHubService(_store);
        _gitHub.Users["good"] = "octocat";

        Services.AddSingleton<ITokenStore>(_store);
        Services.AddSingleton<IGitHubService>(_gitHub);
        Services.AddSingleton<AuthState>();
    }

    [Fact]
    public void Home_ShowsWelcomeText()
    {
        var cut = Render<Home>();

        Assert.Contains("Willkommen bei CodeSail", cut.Markup);
    }

    [Fact]
    public void Login_WithValidToken_NavigatesToRepos()
    {
        var cut = Render<Home>();

        cut.Find("input[type=password]").Input("good");
        cut.Find("form").Submit();

        var nav = Services.GetRequiredService<NavigationManager>();
        cut.WaitForAssertion(() => Assert.EndsWith("/repos", nav.Uri));
        Assert.Equal("octocat", Services.GetRequiredService<AuthState>().Login);
    }

    [Fact]
    public void Login_WithInvalidToken_ShowsError()
    {
        var cut = Render<Home>();

        cut.Find("input[type=password]").Input("bad");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("ungültig", cut.Markup));
        Assert.Null(_store.Token);
    }

    [Fact]
    public async Task Home_WhenAlreadyLoggedIn_RedirectsToRepos()
    {
        _store.Token = "good";
        await Services.GetRequiredService<AuthState>().InitializeAsync();

        Render<Home>();

        Assert.EndsWith("/repos", Services.GetRequiredService<NavigationManager>().Uri);
    }
}
