using GhostLetters.Infrastructure.Auth;
using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Lobbies;

namespace GhostLetters.Api.Endpoints;

public static class LobbyEndpoints
{
    public static RouteGroupBuilder MapLobbies(this RouteGroupBuilder api)
    {
        var lobbies = api.MapGroup("/lobbies").WithTags("Lobbies").RequireAuthorization();
        var presets = api.MapGroup("/me/settings-presets").WithTags("Lobbies").RequireAuthorization();
        presets.MapGet("", (HttpContext http, SettingsPresetService service, CancellationToken ct) =>
            service.ListAsync(http.User.UserId(), ct));
        presets.MapPut("/{id:guid}", (Guid id, SaveSettingsPresetRequest request, HttpContext http, SettingsPresetService service, CancellationToken ct) =>
            service.SaveAsync(http.User.UserId(), id, request, ct));
        presets.MapDelete("/{id:guid}", async (Guid id, HttpContext http, SettingsPresetService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(http.User.UserId(), id, ct);
            return Results.NoContent();
        });

        lobbies.MapGet("/watchable", (HttpContext http, LobbyService service, CancellationToken ct) =>
            service.WatchableAsync(http.User.UserId(), ct)).WithName("WatchableGames");

        lobbies.MapPost("", (CreateLobbyRequest request, HttpContext http, LobbyService service, CancellationToken ct) =>
            service.CreateAsync(http.User.UserId(), request, ct)).WithName("CreateLobby");

        lobbies.MapGet("/{code}", (string code, LobbyService service, CancellationToken ct) =>
            service.GetByCodeAsync(code, ct)).WithName("GetLobbyByCode");

        lobbies.MapPost("/{code}/join", (string code, JoinLobbyRequest request, HttpContext http, LobbyService service, CancellationToken ct) =>
            service.JoinAsync(code, http.User.UserId(), request, ct)).WithName("JoinLobby");

        lobbies.MapPost("/{id:guid}/leave", async (Guid id, HttpContext http, LobbyService service, CancellationToken ct) =>
        {
            await service.LeaveAsync(id, http.User.UserId(), ct);
            return Results.NoContent();
        }).WithName("LeaveLobby");

        lobbies.MapPost("/{id:guid}/ready", (Guid id, ReadyRequest request, HttpContext http, LobbyService service, CancellationToken ct) =>
            service.SetReadyAsync(id, http.User.UserId(), request.Ready, ct)).WithName("SetReady");

        lobbies.MapPut("/{id:guid}/settings", (Guid id, LobbySettings settings, HttpContext http, LobbyService service, CancellationToken ct) =>
            service.UpdateSettingsAsync(id, http.User.UserId(), settings, ct)).WithName("UpdateLobbySettings");

        lobbies.MapPost("/{id:guid}/kick", async (Guid id, KickRequest request, HttpContext http, LobbyService service, CancellationToken ct) =>
        {
            await service.KickAsync(id, http.User.UserId(), request.UserId, ct);
            return Results.NoContent();
        }).WithName("KickFromLobby");

        lobbies.MapPost("/{id:guid}/bots", (Guid id, Guid? botId, HttpContext http, LobbyService service, CancellationToken ct) =>
            service.AddBotAsync(id, http.User.UserId(), ct, botId)).WithName("AddBot");

        lobbies.MapPost("/{id:guid}/start", (Guid id, HttpContext http, LobbyService service, CancellationToken ct) =>
            service.StartAsync(id, http.User.UserId(), ct)).WithName("StartGame");

        return api;
    }

    public static RouteGroupBuilder MapGames(this RouteGroupBuilder api)
    {
        var games = api.MapGroup("/games/{id:guid}").WithTags("Games").RequireAuthorization();

        games.MapGet("/view", (Guid id, HttpContext http, GameService service, CancellationToken ct) =>
            service.SnapshotAsync(id, http.User.UserId(), ct)).WithName("GetGameView");

        // Закрыть без доигрывания: свою партию с ботами или (админ) любую.
        games.MapPost("/close", async (Guid id, HttpContext http, GameService service, CancellationToken ct) =>
            {
                await service.CloseAsync(id, http.User.UserId(), ct);
                return Results.NoContent();
            }).WithName("CloseGame");

        games.MapPost("/commands", (Guid id, CommandRequest request, HttpContext http, GameService service, CancellationToken ct) =>
            service.ExecuteAsync(id, http.User.UserId(), request, ct)).WithName("SendCommand");

        games.MapGet("/events", (Guid id, long? after, HttpContext http, GameService service, CancellationToken ct) =>
            service.GetEventsAsync(id, http.User.UserId(), after ?? 0, ct)).WithName("GetGameEvents");

        // Живой голосовой чат (LiveKit): доступ к комнате партии; говорить — только со словом.
        games.MapPost("/voice", (Guid id, HttpContext http, Infrastructure.Voice.VoiceService voice, CancellationToken ct) =>
            voice.AccessAsync(id, http.User.UserId(), ct)).WithName("JoinVoice");

        // Кусок сказанного (WAV 16 кГц) — распознать и положить в чат: так боты понимают людей.
        games.MapPost("/voice/transcript", async (Guid id, IFormFile file, HttpContext http, Infrastructure.Voice.VoiceService voice,
                CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return await voice.TranscribeAsync(id, http.User.UserId(), stream, file.Length, ct);
            })
            .DisableAntiforgery()
            .WithName("VoiceTranscript");

        // Ответ на переспрос бота: нажатая карта для непонятного названия.
        games.MapPost("/voice/alias", (Guid id, Infrastructure.Voice.AliasRequest request, HttpContext http,
                Infrastructure.Voice.VoiceService voice, CancellationToken ct) =>
            voice.AnswerAsync(id, http.User.UserId(), request, ct))
            .WithName("VoiceAlias");

        return api;
    }
}
