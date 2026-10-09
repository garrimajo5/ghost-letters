using System.Net;
using GhostLetters.Infrastructure.Games;
using GhostLetters.Domain.Roles;

namespace GhostLetters.Api.Tests;

[Collection(DbCollection.Name)]
public sealed class SettingsPresetTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly DbApiFactory _factory = new(postgres);
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Theory]
    [InlineData(3, 5, 5, 0, 0, 0, 0, 2)]
    [InlineData(4, 5, 4, 1, 0, 0, 0, 2)]
    [InlineData(5, 6, 4, 1, 0, 0, 0, 3)]
    [InlineData(6, 5, 4, 1, 1, 0, 0, 3)]
    [InlineData(7, 5, 4, 1, 1, 1, 0, 3)]
    [InlineData(8, 5, 3, 1, 1, 1, 0, 4)]
    [InlineData(9, 6, 3, 1, 1, 1, 0, 5)]
    [InlineData(10, 5, 3, 1, 2, 1, 1, 4)]
    [InlineData(11, 6, 3, 1, 2, 1, 1, 5)]
    public void Ozon_MatchesEveryRow(int n, int columns, int rounds, int killers, int accomplices, int witnesses, int experts, int detectives)
    {
        var settings = new LobbySettings { RulesPreset = "ozon" }.ResolveForPlayers(n);
        settings.Columns.Should().Be(columns);
        settings.Rounds.Should().Be(rounds);
        settings.UseSecretRow.Should().BeTrue();
        settings.Roles.RandomKillerOmission.Should().Be(n == 4);
        var roles = RoleTable.Compose(n, settings.Roles);
        roles.Count(r => r == Role.Ghost).Should().Be(1);
        roles.Count(r => r == Role.Killer).Should().Be(killers);
        roles.Count(r => r == Role.Accomplice).Should().Be(accomplices);
        roles.Count(r => r == Role.Witness).Should().Be(witnesses);
        roles.Count(r => r == Role.Expert).Should().Be(experts);
        roles.Count(r => r == Role.Detective).Should().Be(detectives);
    }

    [Fact]
    public async Task PersonalPresets_PersistAndOnlyOwnerCanChangeThem()
    {
        var owner = await TestPlayer.LoginAsync(_factory, "Автор");
        var other = await TestPlayer.LoginAsync(_factory, "Другой");
        var id = Guid.NewGuid();
        var root = "/api/v1/me/settings-presets";
        var settings = new LobbySettings { Columns = 6, Rounds = 3, GhostUserId = owner.Id };
        var saved = await owner.PutAsync($"{root}/{id}", new { name = "Вечер", settings });
        saved.GetProperty("settings").GetProperty("ghostUserId").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        (await owner.GetAsync(root))[0].Str("name").Should().Be("Вечер");
        (await other.GetAsync(root)).GetArrayLength().Should().Be(0);
        await other.PutAsync($"{root}/{id}", new { name = "Чужой", settings }, HttpStatusCode.NotFound);
        (await other.Client.DeleteAsync($"{root}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await owner.PutAsync($"{root}/{id}", new { name = "Новый вечер", settings = settings with { Columns = 7 } });
        var listed = await owner.GetAsync(root);
        listed.GetArrayLength().Should().Be(1);
        listed[0].GetProperty("settings").GetProperty("columns").GetInt32().Should().Be(7);
        await owner.PutAsync($"{root}/{Guid.NewGuid()}", new { name = " ", settings }, HttpStatusCode.BadRequest);
        (await owner.Client.DeleteAsync($"{root}/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.GetAsync(root)).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Ozon_AdaptsToJoinsAndLeaves_AndStartUsesFinalComposition()
    {
        var host = await TestPlayer.LoginAsync(_factory, "Хост");
        var lobby = await host.PostAsync("/api/v1/lobbies", new { settings = new LobbySettings { RulesPreset = "ozon" } });
        var code = lobby.Str("code");
        var id = lobby.Id("id");
        var players = new List<TestPlayer>();
        for (var i = 0; i < 5; i++)
        {
            var player = await TestPlayer.LoginAsync(_factory, $"Игрок {i}");
            players.Add(player);
            lobby = await player.PostAsync($"/api/v1/lobbies/{code}/join", new { });
            await player.PostAsync($"/api/v1/lobbies/{id}/ready", new { ready = true });
            if (i == 3) lobby.GetProperty("settings").GetProperty("columns").GetInt32().Should().Be(6);
        }
        lobby.GetProperty("settings").GetProperty("roles").GetProperty("extraAccomplices").GetInt32().Should().Be(1);
        await players.Last().PostAsync($"/api/v1/lobbies/{id}/leave", null, HttpStatusCode.NoContent);
        lobby = await host.GetAsync($"/api/v1/lobbies/{code}");
        lobby.GetProperty("settings").GetProperty("columns").GetInt32().Should().Be(6);
        var gameId = (await host.PostAsync($"/api/v1/lobbies/{id}/start", null)).Id("gameId");
        var view = await host.ViewAsync(gameId);
        view.GetProperty("totalRounds").GetInt32().Should().Be(4);
        view.GetProperty("board")[0].GetProperty("cards").GetArrayLength().Should().Be(6);
        view.GetProperty("players").GetArrayLength().Should().Be(5);
    }
}
