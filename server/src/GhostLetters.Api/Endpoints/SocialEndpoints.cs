using GhostLetters.Infrastructure.Auth;
using GhostLetters.Infrastructure.Games;
using Microsoft.AspNetCore.Mvc;

namespace GhostLetters.Api.Endpoints;

/// <summary>Чат, голосовые, заметки, итоги, профиль и рейтинг.</summary>
public static class SocialEndpoints
{
    public static RouteGroupBuilder MapSocial(this RouteGroupBuilder api)
    {
        var game = api.MapGroup("/games/{id:guid}").WithTags("Games").RequireAuthorization();

        game.MapGet("/chat", (Guid id, string? channel, DateTimeOffset? before, int? limit, HttpContext http, ChatService chat,
                CancellationToken ct) => chat.HistoryAsync(id, http.User.UserId(), channel, before, limit ?? 50, ct))
            .WithName("GetChat");

        game.MapPost("/chat", (Guid id, SendChatRequest request, HttpContext http, ChatService chat, CancellationToken ct) =>
            chat.SendAsync(id, http.User.UserId(), request, ct)).WithName("SendChat");

        game.MapGet("/notes", (Guid id, HttpContext http, NotesService notes, CancellationToken ct) =>
            notes.GetNotesAsync(id, http.User.UserId(), ct)).WithName("GetNotes");

        game.MapPut("/notes/{userId:guid}", (Guid id, Guid userId, SaveNoteRequest request, HttpContext http, NotesService notes,
            CancellationToken ct) => notes.SaveNoteAsync(id, http.User.UserId(), userId, request, ct)).WithName("SaveNote");

        game.MapGet("/marks", (Guid id, HttpContext http, NotesService notes, CancellationToken ct) =>
            notes.GetMarksAsync(id, http.User.UserId(), ct)).WithName("GetMarks");

        game.MapPut("/marks", (Guid id, List<CardMarkDto> marks, HttpContext http, NotesService notes, CancellationToken ct) =>
            notes.SaveMarksAsync(id, http.User.UserId(), marks, ct)).WithName("SaveMarks");

        game.MapGet("/summary", (Guid id, HttpContext http, ProfileService profiles, CancellationToken ct) =>
            profiles.SummaryAsync(id, http.User.UserId(), ct)).WithName("GetGameSummary");

        var media = api.MapGroup("/media").WithTags("Media").RequireAuthorization();

        media.MapPost("", async (IFormFile file, [FromForm] int durationMs, HttpContext http, ChatService chat, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return await chat.UploadVoiceAsync(http.User.UserId(), stream, file.Length, file.ContentType, durationMs, ct);
            })
            .DisableAntiforgery()
            .WithName("UploadVoice");

        media.MapGet("/{mediaId:guid}", async (Guid mediaId, HttpContext http, ChatService chat, CancellationToken ct) =>
        {
            var (content, contentType) = await chat.OpenVoiceAsync(http.User.UserId(), mediaId, ct);
            return Results.Stream(content, contentType);
        }).WithName("GetVoice");

        api.MapGet("/me/games", (string? status, HttpContext http, ProfileService profiles, CancellationToken ct) =>
            profiles.MyGamesAsync(http.User.UserId(), status, ct)).WithTags("Profile").RequireAuthorization().WithName("GetMyGames");

        api.MapGet("/users/{userId:guid}/profile", (Guid userId, ProfileService profiles, CancellationToken ct) =>
            profiles.GetProfileAsync(userId, ct)).WithTags("Profile").RequireAuthorization().WithName("GetProfile");

        api.MapGet("/leaderboard", (int? limit, bool? bots, ProfileService profiles, CancellationToken ct) =>
            profiles.LeaderboardAsync(limit ?? 50, ct, bots ?? false)).WithTags("Profile").RequireAuthorization().WithName("GetLeaderboard");

        return api;
    }
}
