using System.Net;
using System.Net.Http.Json;
using GhostLetters.Infrastructure.Auth;
using Microsoft.AspNetCore.Hosting;

namespace GhostLetters.Api.Tests;

[Collection(DbCollection.Name)]
public sealed class RateLimitTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly DbApiFactory _factory = new(postgres, b => b.UseSetting("RateLimit:AuthPerMinute", "3"));

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Auth_TooManyRequestsFromOneAddress_Get429()
    {
        var client = _factory.CreateClient();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            var r = await client.PostAsJsonAsync("/api/v1/auth/guest", new GuestLoginRequest($"device-{Guid.NewGuid():N}", "Игрок", null));
            codes.Add(r.StatusCode);
        }

        codes.Take(3).Should().OnlyContain(c => c == HttpStatusCode.OK);
        codes.Skip(3).Should().OnlyContain(c => c == HttpStatusCode.TooManyRequests);
        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK, "лимит действует только на вход");
    }
}
