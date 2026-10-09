using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GhostLetters.Api.Tests;

/// <summary>Кабинет ботов: только админ создаёт и настраивает характеры; хост выбирает бота в лобби.</summary>
[Collection(DbCollection.Name)]
public sealed class BotAdminTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly DbApiFactory _factory = new(postgres);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private void MakeAdmin(TestPlayer player) =>
        _factory.Services.GetRequiredService<IConfiguration>()["Admin:UserIds"] = player.Id.ToString();

    private static object Spectra(double risk = 0.5) => new
    {
        meaning = 0.6, shape = 0.2, color = 0.2, negative = 0.7, memory = 0.4, risk, compromise = 0.3, variability = 0.1,
    };

    [Fact]
    public async Task Mind_RemembersOnlyFinishedSharedGames_AndReadsOnlyLatestPublicOpinions()
    {
        var admin = await TestPlayer.LoginAsync(_factory, "Лида");
        MakeAdmin(admin);
        var spectra = BotSpectra.From(new BotPersonality { Memory = 1, Variability = 0 });
        var bot = await admin.PostAsync("/api/v1/admin/bots", new { nickname = "Память", spectra });
        var botId = bot.Id("id");
        var state = GameEngine.Create(Guid.NewGuid(), [botId, admin.Id], new GameSettings(),
            Enumerable.Range(1, 300).Select(i => $"c{i:000}").ToList(), 7);
        var card = state.Board[0].Cards[0];
        var now = DateTimeOffset.UtcNow;
        await _factory.WithDbAsync(async db =>
        {
            // Three finished shared games, one unfinished, one without the bot, and the current game.
            for (var i = 0; i < 6; i++)
            {
                var id = i == 5 ? state.Id : Guid.NewGuid();
                db.Games.Add(new Game { Id = id, StartedAt = now.AddDays(-i),
                    FinishedAt = i is 3 or 5 ? null : now.AddDays(-i),
                    Status = i is 3 or 5 ? GameStatuses.Active : GameStatuses.Finished });
                if (i != 4) db.GamePlayers.Add(new GamePlayer { GameId = id, UserId = botId, Seat = 0, Role = "Detective" });
                db.GamePlayers.Add(new GamePlayer { GameId = id, UserId = admin.Id, Seat = 1, Role = i == 0 ? "Killer" : "Detective" });
            }
            for (var i = 0; i < 3; i++)
                db.ChatMessages.Add(new ChatMessage { Id = Guid.NewGuid(), GameId = state.Id, AuthorId = admin.Id,
                    Channel = i == 2 ? ChatChannels.KillerTeam : ChatChannels.Public, Round = 1,
                    CreatedAt = now.AddSeconds(i), CardIds = [card], CardNotes = [i == 1 ? "не эта" : "думаю, эта"] });
            return await db.SaveChangesAsync();
        });
        var mind = (await _factory.WithServiceAsync<BotService, BotMind?>(s => s.MindAsync(state, botId, CancellationToken.None)))!;
        mind.History[admin.Id].Games.Should().Be(3);
        mind.Opinions.Should().ContainSingle().Which.Strength.Should().Be(-1, "повтор заменён, приватный чат недоступен");
        await admin.PutAsync($"/api/v1/admin/bots/{botId}", new { nickname = "Память", spectra = spectra with { Memory = 0, Variability = 1 } });
        var blank = (await _factory.WithServiceAsync<BotService, BotMind?>(s => s.MindAsync(state, botId, CancellationToken.None)))!;
        blank.History.Should().BeEmpty();
        blank.Opinions.Should().ContainSingle("события текущей партии помним даже при памяти 0");
    }

    [Fact]
    public async Task NotAdmin_CannotOpenCabinet()
    {
        var player = await TestPlayer.LoginAsync(_factory, "Игрок");

        (await player.GetAsync("/api/v1/admin/me")).GetProperty("isAdmin").GetBoolean().Should().BeFalse();
        (await player.GetAsync("/api/v1/admin/bots", HttpStatusCode.Forbidden)).Code().Should().Be("FORBIDDEN");
        (await player.PostAsync("/api/v1/admin/bots", new { nickname = "Хакер", spectra = Spectra() }, HttpStatusCode.Forbidden))
            .Code().Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task Admin_CreatesTunesAndDisablesBots_HostPicksThemInLobby()
    {
        var admin = await TestPlayer.LoginAsync(_factory, "Админ");
        MakeAdmin(admin);
        (await admin.GetAsync("/api/v1/admin/me")).GetProperty("isAdmin").GetBoolean().Should().BeTrue();

        var created = await admin.PostAsync("/api/v1/admin/bots",
            new { nickname = "Шерлок", avatarColor = "#3D6A99", about = "Холодная логика", spectra = Spectra(risk: 0.1) });
        created.Str("nickname").Should().Be("Бот Шерлок", "боты всегда подписаны «Бот»");
        var id = created.Id("id");
        created.GetProperty("spectra").GetProperty("risk").GetDouble().Should().Be(0.1);

        var updated = await admin.PutAsync($"/api/v1/admin/bots/{id}",
            new { nickname = "Бот Шерлок", about = "Теперь рискует", spectra = Spectra(risk: 0.9), enabled = true });
        updated.GetProperty("spectra").GetProperty("risk").GetDouble().Should().Be(0.9);
        updated.Str("about").Should().Be("Теперь рискует");

        var presets = await admin.PostAsync("/api/v1/admin/bots/presets", null);
        presets.EnumerateArray().Select(b => b.Str("nickname")).Should().Contain(new[] { "Бот Пуаро", "Бот Марпл", "Бот Шерлок" });
        var again = await admin.PostAsync("/api/v1/admin/bots/presets", null);
        again.GetArrayLength().Should().Be(presets.GetArrayLength(), "готовые характеры не дублируются");

        // Хост выбирает конкретного бота; второй раз того же — нельзя.
        var host = await TestPlayer.LoginAsync(_factory, "Хост");
        var lobby = await host.PostAsync("/api/v1/lobbies", new { settings = new LobbySettings() });
        var lobbyId = lobby.Id("id");
        var publicList = await host.GetAsync("/api/v1/bots");
        publicList.EnumerateArray().Should().Contain(b => b.Id("id") == id && b.Str("about") == "Теперь рискует");

        var withBot = await host.PostAsync($"/api/v1/lobbies/{lobbyId}/bots?botId={id}", null);
        withBot.GetProperty("members").EnumerateArray().Should().Contain(m => m.Id("userId") == id);
        (await host.PostAsync($"/api/v1/lobbies/{lobbyId}/bots?botId={id}", null, HttpStatusCode.Conflict)).Code().Should().Be("CONFLICT");

        // Случайный бот — из кабинета, а не безымянный.
        var random = await host.PostAsync($"/api/v1/lobbies/{lobbyId}/bots", null);
        var names = presets.EnumerateArray().Select(b => b.Str("nickname")).ToHashSet();
        random.GetProperty("members").EnumerateArray().Where(m => m.GetProperty("isBot").GetBoolean())
            .Should().OnlyContain(m => names.Contains(m.Str("nickname")));

        // Выключенного бота в лобби не предлагают.
        await admin.PutAsync($"/api/v1/admin/bots/{id}",
            new { nickname = "Шерлок", about = "На отдыхе", spectra = Spectra(), enabled = false });
        (await host.GetAsync("/api/v1/bots")).EnumerateArray().Should().NotContain(b => b.Id("id") == id);
    }

    [Fact]
    public async Task Mind_LoadsCharacterOnlyForCabinetBots()
    {
        var admin = await TestPlayer.LoginAsync(_factory, "Админ");
        MakeAdmin(admin);
        var bot = await admin.PostAsync("/api/v1/admin/bots", new { nickname = "Мегрэ", spectra = Spectra(risk: 0.8) });
        var botId = bot.Id("id");
        var ids = new List<Guid> { botId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var deck = Enumerable.Range(1, 300).Select(i => $"c{i:000}").ToList();
        var state = GameEngine.Create(Guid.NewGuid(), ids, new GameSettings(), deck, 7);

        var mind = await _factory.WithServiceAsync<BotService, BotMind?>(s => s.MindAsync(state, botId, CancellationToken.None));
        var none = await _factory.WithServiceAsync<BotService, BotMind?>(s => s.MindAsync(state, ids[1], CancellationToken.None));

        mind.Should().NotBeNull();
        mind!.Personality.Risk.Should().BeApproximately(0.8, 0.04, "изменчивость 0.1 сдвигает спектр не больше чем на 0.035");
        none.Should().BeNull("бот без характера играет классически");
    }

    [Fact]
    public async Task Strictness_OldClientDefaultsToMiddle_AndUpdatesArePersisted()
    {
        var admin = await TestPlayer.LoginAsync(_factory, "Админ");
        MakeAdmin(admin);
        // The old request has no strictness field.
        var created = await admin.PostAsync("/api/v1/admin/bots", new { nickname = "Холмс", spectra = Spectra() });
        created.GetProperty("spectra").GetProperty("strictness").GetDouble().Should().Be(0.5);
        created.GetProperty("spectra").GetProperty("details").GetDouble().Should().Be(0.25);
        var id = created.Id("id");
        var changed = BotSpectra.From(new BotPersonality { Strictness = 0.95, Details = 0.9, Memory = 1 });
        await admin.PutAsync($"/api/v1/admin/bots/{id}", new { nickname = "Холмс", spectra = changed, enabled = true });

        // Read through a fresh request, so this verifies the saved database value.
        var savedList = await admin.GetAsync("/api/v1/admin/bots");
        savedList.EnumerateArray().Single(b => b.Id("id") == id)
            .GetProperty("spectra").GetProperty("strictness").GetDouble().Should().Be(0.95);
        var spectra = savedList.EnumerateArray().Single(b => b.Id("id") == id).GetProperty("spectra");
        spectra.GetProperty("details").GetDouble().Should().Be(0.9);
        spectra.GetProperty("memory").GetDouble().Should().Be(1);
    }

    [Fact]
    public async Task Admin_SetsAndRemovesBotAvatar_OthersCannot()
    {
        var admin = await TestPlayer.LoginAsync(_factory, "Админ");
        MakeAdmin(admin);
        var player = await TestPlayer.LoginAsync(_factory, "Игрок");
        var bot = await admin.PostAsync("/api/v1/admin/bots", new { nickname = "Пуаро", spectra = Spectra() });
        var botId = bot.Id("id");
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

        var withPhoto = await UploadAsync(admin, $"/api/v1/admin/bots/{botId}/avatar", png, HttpStatusCode.OK);
        var avatarId = withPhoto.GetProperty("avatarId").GetGuid();
        withPhoto.Str("nickname").Should().Be("Бот Пуаро");
        (await player.GetAsync("/api/v1/bots")).EnumerateArray().Single(b => b.Id("id") == botId)
            .GetProperty("avatarId").GetGuid().Should().Be(avatarId, "хост видит фото бота в лобби");
        (await player.Client.GetAsync($"/api/v1/avatars/{avatarId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Не админ не может, и админ не может так поменять фото живому игроку.
        (await UploadAsync(player, $"/api/v1/admin/bots/{botId}/avatar", png, HttpStatusCode.Forbidden)).Code().Should().Be("FORBIDDEN");
        (await UploadAsync(admin, $"/api/v1/admin/bots/{player.Id}/avatar", png, HttpStatusCode.NotFound)).Code().Should().Be("NOT_FOUND");
        (await UploadAsync(admin, $"/api/v1/admin/bots/{botId}/avatar", "<html>"u8.ToArray(), HttpStatusCode.BadRequest)).Code().Should().Be("VALIDATION");

        var removed = await admin.Client.DeleteAsync($"/api/v1/admin/bots/{botId}/avatar");
        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/api/v1/admin/bots")).EnumerateArray().Single(b => b.Id("id") == botId)
            .GetProperty("avatarId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static async Task<JsonElement> UploadAsync(TestPlayer player, string url, byte[] bytes, HttpStatusCode expected)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "avatar.png");
        var response = await player.Client.PostAsync(url, form);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
