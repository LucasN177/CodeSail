using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using CodeSail;
using CodeSail.Services;
using CodeSail.Services.GitHub;
using CodeSail.Services.Preview;
using CodeSail.Services.Storage;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddMudServices();

builder.Services.AddScoped<ITokenStore, BrowserTokenStore>();
// Ausdrücklich ohne den HttpClient der App, der auf die eigene Adresse statt auf api.github.com zeigt
builder.Services.AddScoped<IGitHubService>(sp => new GitHubService(sp.GetRequiredService<ITokenStore>()));
builder.Services.AddScoped<IFileCache, IndexedDbFileCache>();
builder.Services.AddScoped<BrowserEnvironment>();
builder.Services.AddScoped<WorkspaceService>();
builder.Services.AddScoped<CommitService>();
builder.Services.AddScoped<PreviewService>();
builder.Services.AddScoped<AuthState>();

await builder.Build().RunAsync();
