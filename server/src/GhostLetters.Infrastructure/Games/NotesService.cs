using System.Text.Json;
using GhostLetters.Application;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Infrastructure.Games;

public sealed record NoteDto(Guid TargetUserId, int Suspicion, string Body, JsonElement Entries, DateTimeOffset UpdatedAt);

public sealed record SaveNoteRequest(int Suspicion, string? Body, JsonElement? Entries);

public sealed record CardMarkDto(string CardId, int Crosses, int Checks, bool Believed);

/// <summary>Личные заметки об игроках и пометки на картах. Видит только автор.</summary>
public sealed class NotesService(GhostLettersDbContext db, GameService games, TimeProvider time)
{
    public const int MaxBody = 2000;
    public const int MaxEntriesBytes = 20_000;
    public const int MaxMarks = 60;
    public const int MaxCounter = 20;

    public async Task<IReadOnlyList<NoteDto>> GetNotesAsync(Guid gameId, Guid userId, CancellationToken ct)
    {
        await RequirePlayerAsync(gameId, userId, ct);
        var notes = await db.PlayerNotes.AsNoTracking()
            .Where(n => n.GameId == gameId && n.OwnerId == userId).ToListAsync(ct);
        return notes.Select(Dto).ToList();
    }

    public async Task<NoteDto> SaveNoteAsync(Guid gameId, Guid userId, Guid targetId, SaveNoteRequest request, CancellationToken ct)
    {
        await RequirePlayerAsync(gameId, userId, ct);
        if (targetId == userId || !await db.GamePlayers.AnyAsync(p => p.GameId == gameId && p.UserId == targetId, ct))
        {
            throw AppException.Validation("Заметку можно оставить о другом игроке этой партии.");
        }

        if (request.Suspicion is < -2 or > 2)
        {
            throw AppException.Validation("Подозрение — от -2 до 2.");
        }

        var body = request.Body ?? string.Empty;
        if (body.Length > MaxBody)
        {
            throw AppException.Validation($"Заметка — до {MaxBody} символов.");
        }

        var entries = request.Entries is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } e ? e.GetRawText() : "[]";
        if (request.Entries is { } arr && arr.ValueKind is not (JsonValueKind.Array or JsonValueKind.Undefined or JsonValueKind.Null))
        {
            throw AppException.Validation("entries — массив.");
        }

        if (entries.Length > MaxEntriesBytes)
        {
            throw AppException.Validation("Слишком много записей в заметке.");
        }

        var note = await db.PlayerNotes.FirstOrDefaultAsync(
            n => n.GameId == gameId && n.OwnerId == userId && n.TargetUserId == targetId, ct);
        if (note is null)
        {
            note = new PlayerNote { GameId = gameId, OwnerId = userId, TargetUserId = targetId };
            db.PlayerNotes.Add(note);
        }

        note.Suspicion = request.Suspicion;
        note.Body = body;
        note.Entries = entries;
        note.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Dto(note);
    }

    public async Task<IReadOnlyList<CardMarkDto>> GetMarksAsync(Guid gameId, Guid userId, CancellationToken ct)
    {
        await RequirePlayerAsync(gameId, userId, ct);
        return await db.CardMarks.AsNoTracking()
            .Where(m => m.GameId == gameId && m.OwnerId == userId)
            .OrderBy(m => m.CardId)
            .Select(m => new CardMarkDto(m.CardId, m.Crosses, m.Checks, m.Believed))
            .ToListAsync(ct);
    }

    /// <summary>Пометки целиком: чего нет в списке — удаляется. Пометить можно только карты поля.</summary>
    public async Task<IReadOnlyList<CardMarkDto>> SaveMarksAsync(Guid gameId, Guid userId, IReadOnlyList<CardMarkDto> marks,
        CancellationToken ct)
    {
        await RequirePlayerAsync(gameId, userId, ct);
        if (marks.Count > MaxMarks || marks.Select(m => m.CardId).Distinct().Count() != marks.Count)
        {
            throw AppException.Validation("Пометки — по одной на карту.");
        }

        var game = await db.Games.AsNoTracking().FirstAsync(g => g.Id == gameId, ct);
        var board = GameStore.Read(game).Board.SelectMany(r => r.Cards).ToHashSet();
        if (marks.Any(m => !board.Contains(m.CardId) || m.Crosses is < 0 or > MaxCounter || m.Checks is < 0 or > MaxCounter))
        {
            throw AppException.Validation($"Пометки — только на картах поля, счётчики от 0 до {MaxCounter}.");
        }

        var existing = await db.CardMarks.Where(m => m.GameId == gameId && m.OwnerId == userId).ToListAsync(ct);
        db.CardMarks.RemoveRange(existing.Where(e => marks.All(m => m.CardId != e.CardId)));
        foreach (var mark in marks)
        {
            var row = existing.FirstOrDefault(e => e.CardId == mark.CardId);
            if (row is null)
            {
                row = new CardMark { GameId = gameId, OwnerId = userId, CardId = mark.CardId };
                db.CardMarks.Add(row);
            }

            row.Crosses = mark.Crosses;
            row.Checks = mark.Checks;
            row.Believed = mark.Believed;
        }

        await db.SaveChangesAsync(ct);
        return await GetMarksAsync(gameId, userId, ct);
    }

    private async Task RequirePlayerAsync(Guid gameId, Guid userId, CancellationToken ct)
    {
        if ((await games.RequireViewerAsync(gameId, userId, ct)).IsTable)
        {
            throw AppException.Forbidden("Заметки ведут только игроки.");
        }
    }

    private static NoteDto Dto(PlayerNote n) =>
        new(n.TargetUserId, n.Suspicion, n.Body, JsonDocument.Parse(n.Entries).RootElement.Clone(), n.UpdatedAt);
}
