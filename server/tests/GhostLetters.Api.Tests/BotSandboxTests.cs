using System.Net;
using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Games;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace GhostLetters.Api.Tests;

public sealed class BotSandboxRunnerTests(ITestOutputHelper output)
{
    private static (List<string> Deck, CardTags Tags) Cards()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "client", "assets", "cards"))) dir = dir.Parent;
        var path = Path.Combine(dir!.FullName, "client", "assets", "cards");
        var tags = CardTags.Parse(File.ReadAllText(Path.Combine(path, "tags.json"))).WithDetails(File.ReadAllText(Path.Combine(path, "details.json")));
        return (Enumerable.Range(1, 758).Select(i => $"orig_{i:0000}").ToList(), tags);
    }

    [Theory]
    [InlineData("full-game")]
    [InlineData("clear-hints")]
    [InlineData("witness-reveal")]
    public void RunsAreReproducible_AndCaptureOnlyRoleProjectionForDecisions(string scenario)
    {
        var (deck, tags) = Cards();
        var a = BotSandboxRunner.Run(scenario, 1000, deck, tags, default);
        var b = BotSandboxRunner.Run(scenario, 1000, deck, tags, default);
        output.WriteLine($"{scenario}: status={a.Status}, passed={a.Passed}, rows={a.CorrectRows}/{a.TotalRows}, winner={a.Winner}, frames={a.Frames.Count}, messages={a.Messages.Count}, ms={a.ElapsedMs}");
        a.Status.Should().Be("completed", a.Error);
        GameJson.Serialize(a with { ElapsedMs = 0 }).Should().Be(GameJson.Serialize(b with { ElapsedMs = 0 }));
        a.Frames.Count.Should().BeLessThanOrEqualTo(BotSandboxRunner.MaxSteps + 1);
        foreach (var frame in a.Frames.Where(f => f.Observation?.Me?.Role == Role.Detective))
        {
            frame.Observation!.Truth.Should().BeNull();
            frame.Observation.Players.Where(p => p.Id != frame.Actor && !p.IsGhost).Should().OnlyContain(p => p.KnownRole == null);
        }
        if (scenario == "clear-hints") { a.Passed.Should().BeTrue(); }
        if (scenario == "full-game") { a.Messages.Should().NotBeEmpty(); }
    }

    [Fact]
    public void MissingTagsInvalidateEasyTask_InsteadOfClaimingBotFailure()
    {
        var (deck, _) = Cards();
        var result = BotSandboxRunner.Run("clear-hints", 1000, deck, CardTags.Empty, default);
        result.Status.Should().Be("invalid-scenario"); result.Passed.Should().BeNull();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RevealedWitnessIsHunted_WithoutNamingTheKiller(int seed)
    {
        var (deck, tags) = Cards();
        var report = BotSandboxRunner.Run("witness-reveal", seed, deck, tags, default);
        report.Status.Should().Be("completed", report.Error);
        report.Messages.Single().Text.Should().Be("Я Свидетель.");
        report.Passed.Should().BeTrue("публичное раскрытие роли заметно без обвинения по имени");
    }
}

[Collection(DbCollection.Name)]
public sealed class BotSandboxApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly DbApiFactory _factory = new(postgres);
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task AdminCanRunAndReplay_OrdinaryPlayersCannot_NoLiveGameIsCreated()
    {
        var player = await TestPlayer.LoginAsync(_factory, "Админ");
        var url = "/api/v1/admin/sandbox";
        (await _factory.CreateClient().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await player.GetAsync(url, HttpStatusCode.Forbidden);
        await player.PostAsync(url, new SandboxRequest("witness-reveal", 42), HttpStatusCode.Forbidden);
        await player.GetAsync(url + $"/{Guid.NewGuid()}/steps/0", HttpStatusCode.Forbidden);
        _factory.Services.GetRequiredService<IConfiguration>()["Admin:UserIds"] = player.Id.ToString();
        var before = await _factory.WithDbAsync(async db => (await db.Games.CountAsync(), await db.Users.CountAsync(), await db.BotRelationships.CountAsync()));
        var result = await player.PostAsync(url, new SandboxRequest("witness-reveal", 42));
        var id = result.Id("id");
        try
        {
            (await player.GetAsync(url)).EnumerateArray().Should().Contain(r => r.Id("id") == id);
            var replay = await player.GetAsync(url + $"/{id}");
            replay.GetProperty("seats").GetArrayLength().Should().Be(7);
            var step = await player.GetAsync(url + $"/{id}/steps/0");
            step.GetProperty("truth").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
            step.GetProperty("view").GetProperty("truth").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
            var revealed = await player.GetAsync(url + $"/{id}/steps/0?reveal=true");
            revealed.GetProperty("truth").GetArrayLength().Should().Be(4);
            var witness = revealed.GetProperty("roles").EnumerateObject().Single(p => p.Value.GetString() == "Witness").Name;
            var pov = await player.GetAsync(url + $"/{id}/steps/0?viewer={witness}");
            pov.GetProperty("view").GetProperty("me").Str("role").Should().Be("Witness");
            await player.GetAsync(url + $"/{id}/steps/9999", HttpStatusCode.BadRequest);
            await player.GetAsync(url + $"/{id}/steps/0?viewer={Guid.NewGuid()}", HttpStatusCode.BadRequest);
            var after = await _factory.WithDbAsync(async db => (await db.Games.CountAsync(), await db.Users.CountAsync(), await db.BotRelationships.CountAsync()));
            after.Should().Be(before);
            _factory.Services.GetRequiredService<IConfiguration>()["Admin:UserIds"] = "";
            await player.GetAsync(url + $"/{id}", HttpStatusCode.Forbidden);
            (await player.Client.DeleteAsync(url + $"/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            _factory.Services.GetRequiredService<IConfiguration>()["Admin:UserIds"] = player.Id.ToString();
            (await player.Client.DeleteAsync(url + $"/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
        await player.GetAsync(url + $"/{id}", HttpStatusCode.NotFound);
    }
}
