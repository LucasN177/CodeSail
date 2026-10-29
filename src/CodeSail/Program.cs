using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using CodeSail;
using CodeSail.Services;
using CodeSail.Services.GitHub;
using CodeSail.Services.Storage;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddMudServices();

builder.Services.AddScoped<ITokenStore, BrowserTokenStore>();
builder.Services.AddScoped<IGitHubService, GitHubService>();
builder.Services.AddScoped<IFileCache, IndexedDbFileCache>();
builder.Services.AddScoped<BrowserEnvironment>();
builder.Services.AddScoped<WorkspaceService>();
builder.Services.AddScoped<AuthState>();

await builder.Build().RunAsync();
