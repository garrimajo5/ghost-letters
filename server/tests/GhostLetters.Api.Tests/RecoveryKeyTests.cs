using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GhostLetters.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Api.Tests;

[Collection(DbCollection.Name)]
public sealed class RecoveryKeyTests : IAsyncLifetime
{
    private readonly DbApiFactory _factory;
    private readonly HttpClient _client;
    public RecoveryKeyTests(PostgresFixture postgres) { _factory = new(postgres); _client = _factory.CreateClient(); }
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _factory.DisposeAsync();
    private static string Login() => "key_" + Guid.NewGuid().ToString("N")[..20];
    private static RecoverySecret Word(string text = "Тихий дождь") => new("word", text);

    private async Task<AuthResponse> Register(string login, RecoverySecret key)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/guest", new GuestLoginRequest(
            "device-" + Guid.NewGuid(), "Игрок", null, Recovery: new(login, key)));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }
    private Task<HttpResponseMessage> Enter(string login, RecoverySecret key) =>
        _client.PostAsJsonAsync("/api/v1/auth/key-login", new KeyLoginRequest(login, key));
    private async Task<HttpResponseMessage> Change(AuthResponse auth, RecoveryKeyRequest key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/me/recovery") { Content = JsonContent.Create(key) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task WordLoginAfterLogout_UsesSameAccount_StoresOnlySaltedHash()
    {
        var login = Login();
        var owner = await Register(login, Word());
        await _client.PostAsJsonAsync("/api/v1/auth/logout", new RefreshRequest(owner.RefreshToken));
        var response = await Enter(login.ToUpperInvariant(), Word());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AuthResponse>())!.User.Id.Should().Be(owner.User.Id);
        var first = await _factory.WithDbAsync(db => db.RecoveryCredentials.SingleAsync(x => x.UserId == owner.User.Id));
        var secondOwner = await Register(Login(), Word());
        var second = await _factory.WithDbAsync(db => db.RecoveryCredentials.SingleAsync(x => x.UserId == secondOwner.User.Id));
        first.KeyHash.Should().NotContain("Тихий").And.NotBe(second.KeyHash);
        first.Salt.Should().NotBe(second.Salt);
        first.Iterations.Should().BeGreaterThanOrEqualTo(600_000);
    }

    [Fact]
    public async Task WrongKeysLockAccountAcrossRequests_ThenExpire()
    {
        var login = Login();
        await Register(login, Word());
        for (var i = 0; i < 5; i++) (await Enter(login, Word("ошибка"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Enter(login, Word())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.Time.Advance(TimeSpan.FromMinutes(16));
        (await Enter(login, Word())).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Enter(Login(), Word())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangingKeyRequiresOldKey_RevokesRefresh_AndCardOrderMatters()
    {
        var login = Login();
        var owner = await Register(login, Word());
        var cards = await _factory.WithDbAsync(db => db.Cards.OrderBy(c => c.ImageKey).Take(3).Select(c => c.ImageKey).ToArrayAsync());
        var cardKey = new RecoverySecret("cards", Cards: cards);
        (await Change(owner, new(login, cardKey))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Change(owner, new(login, cardKey, Word("неправильно")))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var changed = await Change(owner, new(login, cardKey, Word()));
        changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        var revoked = await _factory.WithDbAsync(db => db.RefreshTokens.SingleAsync(x => x.TokenHash == AuthService.Hash(owner.RefreshToken)));
        revoked.RevokedAt.Should().NotBeNull();
        (await Enter(login, Word())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Enter(login, new("cards", Cards: cards.Reverse().ToArray()))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Enter(login, cardKey)).StatusCode.Should().Be(HttpStatusCode.OK);
        // Gameplay deactivation must not lock the owner out of their account.
        await _factory.WithDbAsync(db => db.Cards.Where(c => cards.Contains(c.ImageKey)).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false)));
        (await Enter(login, cardKey)).StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.WithDbAsync(db => db.Cards.Where(c => cards.Contains(c.ImageKey)).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, true)));
    }

    [Fact]
    public async Task DuplicateLoginCannotOverwriteOwner_OrLeavePartialGuest()
    {
        var login = Login();
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => _client.PostAsJsonAsync("/api/v1/auth/guest",
            new GuestLoginRequest("device-" + Guid.NewGuid(), "Конкурент", null, Recovery: new(login, Word())))));
        responses.Count(x => x.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(x => x.StatusCode == HttpStatusCode.BadRequest).Should().Be(1);
        (await _factory.WithDbAsync(db => db.RecoveryCredentials.CountAsync(x => x.Login == login))).Should().Be(1);
        (await Enter(login, Word())).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InvalidCardsAndUnauthenticatedEditsRejected()
    {
        var key = new RecoveryKeyRequest(Login(), new("cards", Cards: ["invented1", "invented2", "invented3"]));
        (await _client.PutAsJsonAsync("/api/v1/me/recovery", key)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var response = await _client.PostAsJsonAsync("/api/v1/auth/guest", new GuestLoginRequest("device-" + Guid.NewGuid(), "Игрок", null, Recovery: key));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Enter(key.Login, new("cards", Cards: ["same", "same", "same"]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
