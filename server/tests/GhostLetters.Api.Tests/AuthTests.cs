using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GhostLetters.Infrastructure.Auth;
using GhostLetters.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Api.Tests;

[Collection(DbCollection.Name)]
public sealed class AuthTests : IAsyncLifetime
{
    private readonly DbApiFactory _factory;
    private readonly HttpClient _client;

    public AuthTests(PostgresFixture postgres)
    {
        _factory = new DbApiFactory(postgres);
        _client = _factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Migrations_CreateSchema_AndSeedCatalog()
    {
        var sets = await _factory.WithDbAsync(db => db.CardSets.OrderBy(s => s.Code).Select(s => s.Code).ToListAsync());
        var nominations = await _factory.WithDbAsync(db => db.Nominations.Select(n => n.Code).ToListAsync());

        sets.Should().Equal("mailbox", "mirror", "original", "ritual");
        nominations.Should().Contain("steel_balls");
    }

    [Fact]
    public async Task Guest_CreatesUser_AndSameDeviceReturnsSameUser()
    {
        var device = NewDevice();

        var first = await LoginAsync(device, "Шерлок", "#3fb68b");
        var second = await LoginAsync(device, "Другой ник", null);

        first.User.Nickname.Should().Be("Шерлок");
        first.User.AvatarColor.Should().Be("#3FB68B");
        second.User.Id.Should().Be(first.User.Id);
        second.User.Nickname.Should().Be("Шерлок", "ник меняется только через профиль");
        second.RefreshToken.Should().NotBe(first.RefreshToken);
        first.AccessTokenExpiresAt.Should().BeCloseTo(_factory.Time.GetUtcNow().AddMinutes(15), TimeSpan.FromSeconds(5));

        var stats = await _factory.WithDbAsync(db => db.Stats.CountAsync(s => s.UserId == first.User.Id));
        stats.Should().Be(1);
    }

    [Fact]
    public async Task Guest_WithoutColor_GetsColorFromPalette()
    {
        var login = await LoginAsync(NewDevice(), "Ватсон", null);

        ProfileRules.Palette.Should().Contain(login.User.AvatarColor);
    }

    [Theory]
    [InlineData("x", "#112233")]
    [InlineData("слишком-длинный-ник-для-игры", "#112233")]
    [InlineData("Нормальный", "red")]
    public async Task Guest_InvalidProfile_ReturnsValidationProblem(string nickname, string color)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/guest",
            new GuestLoginRequest(NewDevice(), nickname, color));

        await ShouldBeProblem(response, HttpStatusCode.BadRequest, "VALIDATION");
    }

    [Fact]
    public async Task Guest_ShortDeviceId_ReturnsValidationProblem()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/guest", new GuestLoginRequest("abc", "Игрок", null));

        await ShouldBeProblem(response, HttpStatusCode.BadRequest, "VALIDATION");
    }

    [Fact]
    public async Task Me_RequiresToken()
    {
        var response = await _client.GetAsync("/api/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_ReturnsAndUpdatesProfile()
    {
        var login = await LoginAsync(NewDevice(), "Мисс Марпл", "#112233");
        Authorize(login.AccessToken);

        var me = await _client.GetFromJsonAsync<UserDto>("/api/v1/me");
        me!.Id.Should().Be(login.User.Id);

        var patch = await _client.PatchAsJsonAsync("/api/v1/me", new UpdateProfileRequest("  Пуаро  ", "#aabbcc"));
        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await patch.Content.ReadFromJsonAsync<UserDto>();
        updated!.Nickname.Should().Be("Пуаро");
        updated.AvatarColor.Should().Be("#AABBCC");

        var bad = await _client.PatchAsJsonAsync("/api/v1/me", new UpdateProfileRequest(null, "#zzz"));
        await ShouldBeProblem(bad, HttpStatusCode.BadRequest, "VALIDATION");
    }

    [Fact]
    public async Task Me_WithForeignSignature_IsUnauthorized()
    {
        var login = await LoginAsync(NewDevice(), "Игрок", null);
        var parts = login.AccessToken.Split('.');
        Authorize($"{parts[0]}.{parts[1]}.{new string('A', parts[2].Length)}");

        var response = await _client.GetAsync("/api/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_RotatesTokens()
    {
        var login = await LoginAsync(NewDevice(), "Игрок", null);

        var refreshed = await RefreshAsync(login.RefreshToken);

        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await refreshed.Content.ReadFromJsonAsync<AuthResponse>())!;
        body.RefreshToken.Should().NotBe(login.RefreshToken);
        body.User.Id.Should().Be(login.User.Id);

        Authorize(body.AccessToken);
        (await _client.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_ReusedToken_RevokesAllSessions()
    {
        var login = await LoginAsync(NewDevice(), "Игрок", null);
        var rotated = (await (await RefreshAsync(login.RefreshToken)).Content.ReadFromJsonAsync<AuthResponse>())!;

        var reuse = await RefreshAsync(login.RefreshToken);
        await ShouldBeProblem(reuse, HttpStatusCode.Unauthorized, "UNAUTHORIZED");

        var afterTheft = await RefreshAsync(rotated.RefreshToken);
        afterTheft.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_Expired_IsUnauthorized()
    {
        var login = await LoginAsync(NewDevice(), "Игрок", null);

        _factory.Time.Advance(TimeSpan.FromDays(61));
        var response = await RefreshAsync(login.RefreshToken);

        await ShouldBeProblem(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    [Fact]
    public async Task Refresh_UnknownToken_IsUnauthorized()
    {
        var response = await RefreshAsync("no-such-token");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken_AndStoresOnlyHash()
    {
        var login = await LoginAsync(NewDevice(), "Игрок", null);

        var logout = await _client.PostAsJsonAsync("/api/v1/auth/logout", new RefreshRequest(login.RefreshToken));
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var hash = AuthService.Hash(login.RefreshToken);
        var stored = await _factory.WithDbAsync(db =>
            db.RefreshTokens.Where(t => t.UserId == login.User.Id).Select(t => t.TokenHash).ToListAsync());
        stored.Should().Contain(hash).And.NotContain(login.RefreshToken);
    }

    [Fact]
    public async Task LinkCode_SecondDevice_LogsIntoSameAccount_Once()
    {
        var phone = await LoginAsync(NewDevice(), "Шерлок", null);
        var code = await CreateLinkCodeAsync(phone.AccessToken);
        code.Code.Should().HaveLength(8);
        code.ExpiresAt.Should().BeCloseTo(_factory.Time.GetUtcNow().AddMinutes(10), TimeSpan.FromSeconds(5));

        // Вводят как удобно: строчными, с дефисом.
        var tabletDevice = NewDevice();
        var typed = code.Code[..4].ToLowerInvariant() + "-" + code.Code[4..];
        var response = await _client.PostAsJsonAsync("/api/v1/auth/link", new LinkLoginRequest(tabletDevice, typed));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tablet = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        tablet.User.Id.Should().Be(phone.User.Id);

        // Дальше планшет входит как обычный гость — и это тот же игрок.
        (await LoginAsync(tabletDevice, "Неважно", null)).User.Id.Should().Be(phone.User.Id);

        // Код одноразовый.
        var again = await _client.PostAsJsonAsync("/api/v1/auth/link", new LinkLoginRequest(NewDevice(), code.Code));
        await ShouldBeProblem(again, HttpStatusCode.BadRequest, "VALIDATION");
    }

    [Fact]
    public async Task LinkCode_MovesDeviceFromOtherGuest_AndExpires()
    {
        var owner = await LoginAsync(NewDevice(), "Холмс", null);
        var otherDevice = NewDevice();
        var other = await LoginAsync(otherDevice, "Случайный", null);

        var code = await CreateLinkCodeAsync(owner.AccessToken);
        var linked = await _client.PostAsJsonAsync("/api/v1/auth/link", new LinkLoginRequest(otherDevice, code.Code));
        linked.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(otherDevice, "Случайный", null)).User.Id.Should().Be(owner.User.Id).And.NotBe(other.User.Id);

        var stale = await CreateLinkCodeAsync(owner.AccessToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(11));
        var late = await _client.PostAsJsonAsync("/api/v1/auth/link", new LinkLoginRequest(NewDevice(), stale.Code));
        await ShouldBeProblem(late, HttpStatusCode.BadRequest, "VALIDATION");
    }

    [Fact]
    public async Task LinkCode_RequiresSignIn()
    {
        var response = await _client.PostAsync("/api/v1/auth/link-code", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<LinkCodeResponse> CreateLinkCodeAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/link-code");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<LinkCodeResponse>())!;
    }

    private static string NewDevice() => $"device-{Guid.NewGuid():N}";

    private async Task<AuthResponse> LoginAsync(string device, string nickname, string? color)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/guest", new GuestLoginRequest(device, nickname, color));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private Task<HttpResponseMessage> RefreshAsync(string token) =>
        _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(token));

    private void Authorize(string token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task ShouldBeProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be(code);
    }
}
