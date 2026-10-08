using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GhostLetters.Infrastructure.Auth;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

/// <summary>Своя аватарка: загрузка, показ всем без входа, удаление; принимаются только картинки.</summary>
[Collection(DbCollection.Name)]
public sealed class AvatarTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    private readonly DbApiFactory _factory = new(postgres);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Upload_ShownToEveryone_InLobbyAndProfile_ThenRemoved()
    {
        var me = await TestPlayer.LoginAsync(_factory, "Ирен");
        var user = await UploadAsync(me, Png, "image/png", HttpStatusCode.OK);
        var avatarId = user.GetProperty("avatarId").GetGuid();

        // Картинку отдаём без входа и с долгим кэшем: новый файл — новый id.
        var anonymous = _factory.CreateClient();
        var image = await anonymous.GetAsync($"/api/v1/avatars/{avatarId}");
        image.StatusCode.Should().Be(HttpStatusCode.OK);
        image.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await image.Content.ReadAsByteArrayAsync()).Should().Equal(Png);
        image.Headers.CacheControl!.MaxAge.Should().BeGreaterThan(TimeSpan.FromDays(30));

        var lobby = await me.PostAsync("/api/v1/lobbies", new { settings = new LobbySettings() });
        lobby.GetProperty("members")[0].GetProperty("avatarId").GetGuid().Should().Be(avatarId);
        (await me.GetAsync("/api/v1/me")).GetProperty("avatarId").GetGuid().Should().Be(avatarId);

        var removed = await me.Client.DeleteAsync("/api/v1/me/avatar");
        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await me.GetAsync("/api/v1/me")).GetProperty("avatarId").ValueKind.Should().Be(JsonValueKind.Null);
        (await anonymous.GetAsync($"/api/v1/avatars/{avatarId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Upload_NotAnImage_Rejected()
    {
        var me = await TestPlayer.LoginAsync(_factory, "Ирен");
        var error = await UploadAsync(me, "<html>not an image</html>"u8.ToArray(), "image/png", HttpStatusCode.BadRequest);
        error.GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public void ImageType_ByContent()
    {
        UserService.ImageType(Png).Should().Be("image/png");
        UserService.ImageType([0xFF, 0xD8, 0xFF, 0xE0]).Should().Be("image/jpeg");
        UserService.ImageType("RIFF\0\0\0\0WEBPVP8 "u8).Should().Be("image/webp");
        UserService.ImageType("GIF89a"u8).Should().BeNull();
    }

    private static async Task<JsonElement> UploadAsync(TestPlayer player, byte[] bytes, string type, HttpStatusCode expected)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(file, "file", "avatar.png");
        var response = await player.Client.PostAsync("/api/v1/me/avatar", form);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
