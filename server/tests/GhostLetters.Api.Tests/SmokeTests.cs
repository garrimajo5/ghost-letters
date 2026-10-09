using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GhostLetters.Api.Tests;

public class SmokeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task Version_ReturnsApiV1()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v1/version");

        body.GetProperty("api").GetString().Should().Be("v1");
    }

    [Fact]
    public async Task PreviewRoles_SevenPlayers_ReturnsRulebookComposition()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v1/rules/roles?players=7");

        body.GetProperty("cooperative").GetBoolean().Should().BeFalse();
        body.GetProperty("rounds").GetInt32().Should().Be(4);
        body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should()
            .BeEquivalentTo(new[] { "Ghost", "Killer", "Accomplice", "Witness", "Detective", "Detective", "Detective" });
    }

    [Fact]
    public async Task PreviewRoles_SixPlayersWithAccomplice()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v1/rules/roles?players=6&accomplices=1");

        body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should()
            .BeEquivalentTo(new[] { "Ghost", "Killer", "Accomplice", "Detective", "Detective", "Detective" });
        (await _client.GetAsync("/api/v1/rules/roles?players=5&accomplices=1")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PreviewRoles_InvalidImitator_ReturnsProblem()
    {
        var response = await _client.GetAsync("/api/v1/rules/roles?players=4&imitator=ReplaceDetective");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }
}
