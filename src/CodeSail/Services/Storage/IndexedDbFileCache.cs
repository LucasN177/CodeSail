using CodeSail.Models;
using Microsoft.JSInterop;

namespace CodeSail.Services.Storage;

public sealed class IndexedDbFileCache(IJSRuntime js) : IFileCache, IAsyncDisposable
{
    private Task<IJSObjectReference>? _module;

    private Task<IJSObjectReference> Module =>
        _module ??= js.InvokeAsync<IJSObjectReference>("import", "./js/codesail.js").AsTask();

    public Task<CachedFile?> GetFileAsync(string key) => GetAsync<CachedFile>("files", key);

    public Task SaveFileAsync(CachedFile file) => PutAsync("files", file);

    public async Task RemoveFileAsync(string key) => await (await Module).InvokeVoidAsync("remove", "files", key);

    public async Task<IReadOnlyList<CachedFile>> GetFilesAsync(string keyPrefix) =>
        await (await Module).InvokeAsync<List<CachedFile>>("getByPrefix", "files", keyPrefix);

    public Task<CachedTree?> GetTreeAsync(string key) => GetAsync<CachedTree>("trees", key);

    public Task SaveTreeAsync(CachedTree tree) => PutAsync("trees", tree);

    public Task<CachedRepo?> GetRepoAsync(string key) => GetAsync<CachedRepo>("repos", key);

    public Task SaveRepoAsync(CachedRepo repo) => PutAsync("repos", repo);

    public Task<CachedRepoList?> GetRepoListAsync() => GetAsync<CachedRepoList>("repos", CacheKeys.RepoList);

    public Task SaveRepoListAsync(CachedRepoList list) => PutAsync("repos", list);

    public async Task ClearAsync() => await (await Module).InvokeVoidAsync("clearAll");

    public async Task<bool> RequestPersistenceAsync() =>
        await (await Module).InvokeAsync<bool>("requestPersistence");

    public async Task<long?> EstimateUsageAsync() =>
        await (await Module).InvokeAsync<long?>("estimateUsage");

    private async Task<T?> GetAsync<T>(string store, string key) where T : class =>
        await (await Module).InvokeAsync<T?>("get", store, key);

    private async Task PutAsync<T>(string store, T value) =>
        await (await Module).InvokeVoidAsync("put", store, value);

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
            return;
        try
        {
            await (await _module).DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
