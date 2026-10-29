using Bunit;
using CodeSail.Pages;
using MudBlazor.Services;

namespace CodeSail.Tests;

public class HomePageTests : BunitContext
{
    public HomePageTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Home_ShowsWelcomeText()
    {
        var cut = Render<Home>();

        Assert.Contains("Willkommen bei CodeSail", cut.Markup);
    }
}
