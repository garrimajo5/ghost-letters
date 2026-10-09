using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GhostLetters.Infrastructure.Auth;
using GhostLetters.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR;
using GhostLetters.Infrastructure.Games;
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace GhostLetters.Api.Tests;

// Regression coverage for the security audit.
[Collection(DbCollection.Name)]
public sealed class SecurityAuditTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly DbApiFactory _factory = new(postgres);
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task UntrustedForwardedHeadersCannotBypassAuthLimit()
    {
        await using var factory = new DbApiFactory(postgres, b => b.UseSetting("RateLimit:AuthPerMinute", "2"));
        using var one = factory.CreateClient();
        using var two = factory.CreateClient();
        one.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2.10");
        two.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2.20");
        // Both requests reach the app through the same transport, like the production reverse proxy.
        (await one.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest("invalid-a"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await one.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest("invalid-b"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await two.PostAsJsonAsync("/api/v1/auth/guest", new GuestLoginRequest("audit-" + Guid.NewGuid(), "Аудит", null)))
            .StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task PublicHandsDealtEventDoesNotIdentifyHiddenKiller()
    {
        var game = await GameHarness.StartAsync(_factory, 5);
        await new GameDriver(game).RunUntilAsync(p => p == "Night");
        var killer = await game.WithRoleAsync("Killer");
        var detective = await game.WithRoleAsync("Detective");
        var view = await killer.ViewAsync(game.GameId);
        await killer.CommandAsync(game.GameId, "ChooseTruth", new { columns = Enumerable.Repeat(0, view.GetProperty("board").GetArrayLength()).ToArray() });
        var publicView = await detective.ViewAsync(game.GameId);
        publicView.GetProperty("players").EnumerateArray().Single(p => p.Id("id") == killer.Id)
            .GetProperty("knownRole").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        var events = await detective.GetAsync($"/api/v1/games/{game.GameId}/events");
        events.EnumerateArray().Single(e => e.Str("type") == "HandsDealt").GetProperty("actor").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        // Historic records retain the private command actor but have no public-actor payload.
        await _factory.WithDbAsync(db => db.GameEvents.Where(e => e.GameId == game.GameId && e.Type == "HandsDealt")
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Payload, "{\"detail\":null}")));
        events = await detective.GetAsync($"/api/v1/games/{game.GameId}/events");
        events.EnumerateArray().Where(e => e.Str("type") is "HandsDealt" or "PhaseChanged")
            .Should().OnlyContain(e => e.GetProperty("actor").ValueKind == System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task StaleRefreshReaderCannotIssueAnotherReplacement()
    {
        using var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/guest", new GuestLoginRequest("audit-" + Guid.NewGuid(), "Аудит", null));
        var login = (await loginResponse.Content.ReadFromJsonAsync<AuthResponse>())!;
        await using var scopeA = _factory.Services.CreateAsyncScope();
        await using var scopeB = _factory.Services.CreateAsyncScope();
        var hash = AuthService.Hash(login.RefreshToken);
        // Two requests have read the same live token before either saves; emulate that interleaving deterministically.
        await scopeA.ServiceProvider.GetRequiredService<GhostLettersDbContext>().RefreshTokens.SingleAsync(t => t.TokenHash == hash);
        await scopeB.ServiceProvider.GetRequiredService<GhostLettersDbContext>().RefreshTokens.SingleAsync(t => t.TokenHash == hash);
        var a = await scopeA.ServiceProvider.GetRequiredService<AuthService>().RefreshAsync(login.RefreshToken, default);
        Func<Task> second = () => scopeB.ServiceProvider.GetRequiredService<AuthService>().RefreshAsync(login.RefreshToken, default);
        await second.Should().ThrowAsync<GhostLetters.Application.AppException>();
        (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(a.RefreshToken))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    }

    [Fact]
    public async Task DeviceIdentifierCannotRestoreRevokedSession()
    {
        using var client = _factory.CreateClient();
        var request = new GuestLoginRequest("audit-" + Guid.NewGuid(), "Аудит", null);
        var response = await client.PostAsJsonAsync("/api/v1/auth/guest", request);
        var login = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        await client.PostAsJsonAsync("/api/v1/auth/logout", new RefreshRequest(login.RefreshToken));
        (await client.PostAsJsonAsync("/api/v1/auth/guest", request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    }

    [Fact]
    public async Task UploadRejectsNonAudioPayloads()
    {
        var player = await TestPlayer.LoginAsync(_factory, "Аудит медиа");
        for (var i = 0; i < 3; i++)
        {
            using var data = new MultipartFormDataContent();
            var file = new ByteArrayContent("this is not an audio file"u8.ToArray());
            file.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
            data.Add(file, "file", "sample.mp3");
            data.Add(new StringContent("1"), "durationMs");
            (await player.Client.PostAsync("/api/v1/media", data)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        (await _factory.WithDbAsync(db => db.MediaFiles.CountAsync(m => m.OwnerId == player.Id))).Should().Be(0);
    }

    [Fact]
    public async Task TrustedProxyUsesRealClient_AndIgnoresSpoofedPrefix()
    {
        await using var factory = new DbApiFactory(postgres, b =>
        {
            b.UseSetting("RateLimit:AuthPerMinute", "2");
            b.UseSetting("Proxy:KnownProxies", "127.0.0.1");
            b.ConfigureServices(s => s.AddSingleton<IStartupFilter, ProxyTransport>());
        });
        using var client = factory.CreateClient();
        async Task<HttpStatusCode> Send(string chain)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
                { Content = JsonContent.Create(new RefreshRequest("invalid")) };
            request.Headers.Add("X-Forwarded-For", chain);
            return (await client.SendAsync(request)).StatusCode;
        }
        (await Send("192.0.2.10")).Should().Be(HttpStatusCode.Unauthorized);
        (await Send("192.0.2.10")).Should().Be(HttpStatusCode.Unauthorized);
        (await Send("203.0.113.55, 192.0.2.10")).Should().Be(HttpStatusCode.TooManyRequests);
        (await Send("192.0.2.20")).Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed class ProxyTransport : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, continuation) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
                return continuation(context);
            });
            next(app);
        };
    }

    [Fact]
    public async Task ParallelUploadsCannotOverrunPendingQuota_AndOrphansExpire()
    {
        var player = await TestPlayer.LoginAsync(_factory, "Лимит файлов");
        var bytes = new byte[100]; bytes[0] = 0xff; bytes[1] = 0xf1;
        async Task<HttpStatusCode> Upload()
        {
            using var data = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("audio/aac");
            data.Add(file, "file", "voice.aac"); data.Add(new StringContent("1000"), "durationMs");
            return (await player.Client.PostAsync("/api/v1/media", data)).StatusCode;
        }
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Upload()));
        outcomes.Count(s => s == HttpStatusCode.OK).Should().Be(5);
        outcomes.Count(s => s == HttpStatusCode.TooManyRequests).Should().Be(3);
        var keys = await _factory.WithDbAsync(db => db.MediaFiles.Where(m => m.OwnerId == player.Id).Select(m => m.StorageKey).ToListAsync());
        _factory.Time.Advance(TimeSpan.FromHours(25));
        await _factory.WithServiceAsync<MediaLimits, int>(m => m.CleanupAsync(default));
        (await _factory.WithDbAsync(db => db.MediaFiles.CountAsync(m => m.OwnerId == player.Id))).Should().Be(0);
        var storage = _factory.Services.GetRequiredService<IMediaStorage>();
        foreach (var key in keys) storage.Open(key).Should().BeNull();
    }

    [Fact]
    public async Task BoundedReadDoesNotTrustDeclaredLength()
    {
        using var oversized = new MemoryStream(new byte[1024]);
        Func<Task> read = () => MediaLimits.ReadAsync(oversized, 8, 100, default);
        await read.Should().ThrowAsync<GhostLetters.Application.AppException>();
    }

    [Fact]
    public async Task HubBudgetIsSharedAcrossConnections()
    {
        await using var factory = new DbApiFactory(postgres, b => b.UseSetting("RateLimit:HubCallsPerMinute", "2"));
        var player = await TestPlayer.LoginAsync(factory, "Лимит хаба");
        await using var a = Connect(factory, player.Token);
        await using var b = Connect(factory, player.Token);
        await a.StartAsync(); await b.StartAsync();
        await a.InvokeAsync("UnsubscribeLobby", Guid.NewGuid());
        await b.InvokeAsync("UnsubscribeLobby", Guid.NewGuid());
        Func<Task> excess = () => a.InvokeAsync("UnsubscribeLobby", Guid.NewGuid());
        (await excess.Should().ThrowAsync<HubException>()).Which.Message.Should().Contain("RATE_LIMIT");
    }

    [Fact]
    public async Task HubClosesWhenAuthenticationExpires()
    {
        var player = await TestPlayer.LoginAsync(_factory, "Короткая сессия");
        var normal = new JsonWebToken(player.Token);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = normal.Issuer, Audience = normal.Audiences.Single(),
            Subject = new ClaimsIdentity([new Claim("sub", player.Id.ToString())]),
            IssuedAt = DateTime.UtcNow, NotBefore = DateTime.UtcNow.AddSeconds(-1), Expires = DateTime.UtcNow.AddSeconds(3),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.TestSigningKey)), SecurityAlgorithms.HmacSha256),
        });
        await using var connection = Connect(_factory, token);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };
        await connection.StartAsync();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        connection.State.Should().Be(HubConnectionState.Disconnected);
    }

    private static HubConnection Connect(DbApiFactory factory, string token) => new HubConnectionBuilder()
        .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/play"), o =>
        {
            o.Transports = HttpTransportType.LongPolling;
            o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            o.AccessTokenProvider = () => Task.FromResult<string?>(token);
        }).Build();
}
