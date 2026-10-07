using GhostLetters.Infrastructure.Games;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Api.Tests;

/// <summary>Лобби с N игроками, все готовы, партия запущена через API.</summary>
public sealed class GameHarness
{
    private GameHarness(DbApiFactory factory, List<TestPlayer> players, Guid lobbyId, string code, Guid gameId)
    {
        Factory = factory;
        Players = players;
        LobbyId = lobbyId;
        Code = code;
        GameId = gameId;
    }

    public DbApiFactory Factory { get; }

    public List<TestPlayer> Players { get; }

    public TestPlayer Host => Players[0];

    public Guid LobbyId { get; }

    public string Code { get; }

    public Guid GameId { get; }

    public static async Task<GameHarness> StartAsync(DbApiFactory factory, int players, object? settings = null)
    {
        var list = new List<TestPlayer>();
        for (var i = 0; i < players; i++)
        {
            list.Add(await TestPlayer.LoginAsync(factory, $"Игрок {i + 1}"));
        }

        var lobby = await list[0].PostAsync("/api/v1/lobbies", new { title = "Тест", settings });
        var code = lobby.Str("code");
        var lobbyId = lobby.Id("id");
        foreach (var p in list.Skip(1))
        {
            await p.PostAsync($"/api/v1/lobbies/{code}/join", new { mode = "player" });
            await p.PostAsync($"/api/v1/lobbies/{lobbyId}/ready", new { ready = true });
        }

        var gameId = (await list[0].PostAsync($"/api/v1/lobbies/{lobbyId}/start", null)).Id("gameId");
        return new GameHarness(factory, list, lobbyId, code, gameId);
    }

    /// <summary>Роль игрока — из его собственной проекции.</summary>
    public async Task<string> RoleOfAsync(TestPlayer player) =>
        (await player.ViewAsync(GameId)).GetProperty("me").Str("role");

    public async Task<TestPlayer> WithRoleAsync(string role)
    {
        foreach (var p in Players)
        {
            if (await RoleOfAsync(p) == role)
            {
                return p;
            }
        }

        throw new InvalidOperationException($"Нет роли {role}.");
    }

    /// <summary>Сдвинуть часы за дедлайн и прогнать службу таймеров.</summary>
    public async Task<int> ExpireAsync(TimeSpan? step = null)
    {
        Factory.Time.Advance(step ?? TimeSpan.FromHours(2));
        return await Factory.WithServiceAsync<GameService, int>(s => s.TimeoutDueAsync(CancellationToken.None));
    }

    public Task<(string Phase, int Version, string Status)> StateAsync() =>
        Factory.WithDbAsync(db =>
        {
            var g = db.Games.AsNoTracking().Single(x => x.Id == GameId);
            return Task.FromResult((g.Phase, g.Version, g.Status));
        });
}
