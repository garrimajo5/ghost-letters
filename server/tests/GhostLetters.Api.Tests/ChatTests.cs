using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

[Collection(DbCollection.Name)]
public sealed class ChatTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly DbApiFactory _factory = new(postgres);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Radio_OnlySpeakerWrites_GhostSilent_KillerChannelPrivate()
    {
        var game = await GameHarness.StartAsync(_factory, players: 7, new LobbySettings { Rounds = 1 });
        var driver = new GameDriver(game);
        var ghost = await game.WithRoleAsync("Ghost");
        var killer = await game.WithRoleAsync("Killer");
        var detective = await game.WithRoleAsync("Detective");

        await driver.RunUntilAsync(p => p == "Mailbox");
        // До обсуждения писать может любой, кроме Призрака.
        await detective.PostAsync(Chat(game), new { text = "Отправляю что-то про ключ" });
        (await ghost.PostAsync(Chat(game), new { text = "подсказка" }, HttpStatusCode.Forbidden)).Code().Should().Be("NOT_ALLOWED");

        await driver.RunUntilAsync(p => p == "Discussion");
        var view = await game.Host.ViewAsync(game.GameId);
        var speakerId = view.Id("currentSpeaker");
        var speaker = game.Players.Single(p => p.Id == speakerId);
        var silent = game.Players.First(p => p.Id != speakerId && p.Id != ghost.Id);
        var boardCard = view.GetProperty("board")[0].GetProperty("cards")[0].GetString()!;

        (await silent.PostAsync(Chat(game), new { text = "можно?" }, HttpStatusCode.Conflict)).Code().Should().Be("NOT_YOUR_TURN");
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        var said = await speaker.PostAsync(Chat(game), new { text = "Думаю, это нож", cardIds = new[] { boardCard } });
        said.GetProperty("cardIds")[0].GetString().Should().Be(boardCard);
        said.GetProperty("round").GetInt32().Should().Be(1);
        (await speaker.PostAsync(Chat(game), new { text = "x", cardIds = new[] { "orig_9999" } }, HttpStatusCode.BadRequest))
            .Code().Should().Be("VALIDATION");

        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        await killer.PostAsync(Chat(game), new { channel = "killer_team", text = "Валим на детектива" });
        (await detective.PostAsync(Chat(game), new { channel = "killer_team", text = "я тоже" }, HttpStatusCode.Forbidden))
            .Code().Should().Be("FORBIDDEN");

        var forDetective = await detective.GetAsync(Chat(game));
        forDetective.EnumerateArray().Select(m => m.Str("channel")).Should().OnlyContain(c => c == "public");
        forDetective.GetArrayLength().Should().Be(2);

        var forGhost = await ghost.GetAsync(Chat(game));
        forGhost.EnumerateArray().Select(m => m.Str("text")).Should().Contain("Валим на детектива");
        forGhost.EnumerateArray().Select(m => m.Str("text")).Should().Equal("Отправляю что-то про ключ", "Думаю, это нож", "Валим на детектива");
    }

    [Fact]
    public async Task Voice_UploadPostAndListen_OnlyForParticipants()
    {
        var game = await GameHarness.StartAsync(_factory, players: 4);
        var author = await game.WithRoleAsync("Detective");
        var listener = await game.WithRoleAsync("Killer");
        var stranger = await TestPlayer.LoginAsync(_factory, "Чужой");
        var bytes = Enumerable.Range(0, 4096).Select(i => (byte)i).ToArray();

        var media = await UploadAsync(author, bytes, "audio/aac", 3200, HttpStatusCode.OK);
        var mediaId = media.Id("mediaId");
        (await UploadAsync(author, bytes, "audio/aac", 61_000, HttpStatusCode.BadRequest)).Code().Should().Be("VALIDATION");
        (await UploadAsync(author, bytes, "image/png", 1000, HttpStatusCode.BadRequest)).Code().Should().Be("VALIDATION");

        (await listener.Client.GetAsync($"/api/v1/media/{mediaId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "пока голосовое не отправлено в чат, его слышит только автор");

        var message = await author.PostAsync(Chat(game), new { mediaId });
        message.Str("kind").Should().Be("voice");
        message.GetProperty("durationMs").GetInt32().Should().Be(3200);

        var heard = await listener.Client.GetAsync($"/api/v1/media/{mediaId}");
        heard.StatusCode.Should().Be(HttpStatusCode.OK);
        heard.Content.Headers.ContentType!.MediaType.Should().Be("audio/aac");
        (await heard.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);

        (await stranger.Client.GetAsync($"/api/v1/media/{mediaId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await listener.PostAsync(Chat(game), new { mediaId }, HttpStatusCode.BadRequest)).Code().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task TableScreen_ReadsPublicChat_CannotWrite()
    {
        var game = await GameHarness.StartAsync(_factory, players: 4);
        var table = await TestPlayer.LoginAsync(_factory, "Экран");
        await table.PostAsync($"/api/v1/lobbies/{game.Code}/join", new { mode = "table" });
        var killer = await game.WithRoleAsync("Killer");
        await (await game.WithRoleAsync("Detective")).PostAsync(Chat(game), new { text = "привет" });
        await killer.PostAsync(Chat(game), new { channel = "killer_team", text = "тсс" });

        var seen = await table.GetAsync(Chat(game));
        seen.EnumerateArray().Select(m => m.Str("text")).Should().NotContain("тсс");
        await table.PostAsync(Chat(game), new { text = "я экран" }, HttpStatusCode.Forbidden);
    }

    private static string Chat(GameHarness game) => $"/api/v1/games/{game.GameId}/chat";

    private static async Task<JsonElement> UploadAsync(TestPlayer player, byte[] bytes, string type, int durationMs, HttpStatusCode expected)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(file, "file", "voice.aac");
        form.Add(new StringContent(durationMs.ToString()), "durationMs");
        var response = await player.Client.PostAsync("/api/v1/media", form);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
