using CodeSail.Services;
using Octokit;

namespace CodeSail.Tests;

public class AuthStateTests
{
    private readonly FakeTokenStore _store = new();
    private readonly FakeGitHubService _gitHub;
    private readonly AuthState _auth;

    public AuthStateTests()
    {
        _gitHub = new FakeGitHubService(_store);
        _gitHub.Users["good"] = "octocat";
        _auth = new AuthState(_store, _gitHub);
    }

    [Fact]
    public async Task Initialize_WithStoredValidToken_RestoresLogin()
    {
        _store.Token = "good";

        await _auth.InitializeAsync();

        Assert.True(_auth.IsInitialized);
        Assert.Equal("octocat", _auth.Login);
    }

    [Fact]
    public async Task Initialize_WithRevokedToken_ClearsToken()
    {
        _store.Token = "revoked";

        await _auth.InitializeAsync();

        Assert.False(_auth.IsAuthenticated);
        Assert.Null(_store.Token);
    }

    [Fact]
    public async Task Initialize_WhenOffline_KeepsToken()
    {
        _store.Token = "good";
        _gitHub.Users.Clear();
        _gitHub.LoginError = new HttpRequestException("offline");

        await _auth.InitializeAsync();

        Assert.True(_auth.IsInitialized);
        Assert.False(_auth.IsAuthenticated);
        Assert.Equal("good", _store.Token);
    }

    [Fact]
    public async Task Initialize_WhenOfflineAfterEarlierLogin_ContinuesWithStoredLogin()
    {
        await _auth.LoginAsync("good", remember: true);
        var restarted = new AuthState(_store, _gitHub);
        _gitHub.Users.Clear();
        _gitHub.LoginError = new HttpRequestException("offline");

        await restarted.InitializeAsync();

        Assert.Equal("octocat", restarted.Login);
        Assert.False(restarted.SessionExpired);
    }

    [Fact]
    public async Task Initialize_WithRevokedToken_MarksSessionExpired()
    {
        _store.Token = "revoked";

        await _auth.InitializeAsync();

        Assert.True(_auth.SessionExpired);
    }

    [Fact]
    public async Task Unauthorized_WhileWorking_SignsOutButKeepsReturnUrl()
    {
        await _auth.LoginAsync("good", remember: false);
        _auth.ReturnUrl = "repo/octo/demo";

        _gitHub.RaiseUnauthorized();

        Assert.False(_auth.IsAuthenticated);
        Assert.True(_auth.SessionExpired);
        Assert.Null(_store.Token);
        Assert.Equal("repo/octo/demo", _auth.ReturnUrl);
    }

    [Fact]
    public async Task Unauthorized_DuringLogin_IsIgnored()
    {
        await Assert.ThrowsAsync<AuthorizationException>(() => _auth.LoginAsync("bad", remember: false));

        _gitHub.RaiseUnauthorized();

        Assert.False(_auth.SessionExpired);
    }

    [Fact]
    public async Task Login_AfterExpiry_ClearsExpiredFlag()
    {
        await _auth.LoginAsync("good", remember: false);
        _gitHub.RaiseUnauthorized();

        await _auth.LoginAsync("good", remember: false);

        Assert.False(_auth.SessionExpired);
        Assert.Equal("octocat", _store.Login);
    }

    [Fact]
    public async Task Login_WithValidToken_StoresTrimmedTokenAndRaisesChanged()
    {
        var changed = false;
        _auth.Changed += () => changed = true;

        await _auth.LoginAsync("  good ", remember: true);

        Assert.Equal("octocat", _auth.Login);
        Assert.Equal("good", _store.Token);
        Assert.True(_store.Persistent);
        Assert.True(changed);
    }

    [Fact]
    public async Task Login_WithInvalidToken_ThrowsAndClearsToken()
    {
        await Assert.ThrowsAsync<AuthorizationException>(() => _auth.LoginAsync("bad", remember: false));

        Assert.False(_auth.IsAuthenticated);
        Assert.Null(_store.Token);
    }

    [Fact]
    public async Task Logout_ClearsLoginAndToken()
    {
        await _auth.LoginAsync("good", remember: false);

        await _auth.LogoutAsync();

        Assert.False(_auth.IsAuthenticated);
        Assert.Null(_store.Token);
    }
}
