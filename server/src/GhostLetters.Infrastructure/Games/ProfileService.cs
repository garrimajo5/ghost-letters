using System.Text.Json;
using GhostLetters.Application;
using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Auth;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Infrastructure.Games;

public sealed record StatsDto(int Games, int Wins, int Rating, int LikesReceived, IReadOnlyDictionary<string, int> RoleWins);

public sealed record AchievementDto(string Code, string Title, int Count);

public sealed record ProfileDto(UserDto User, StatsDto Stats, IReadOnlyList<AchievementDto> Achievements, bool IsBot = false);

public sealed record LeaderboardRow(UserDto User, int Rating, int Games, int Wins, bool IsBot = false);

public sealed record MyGameDto(
    Guid GameId,
    Guid? LobbyId,
    string Status,
    string Phase,
    int Round,
    bool YourTurn,
    DateTimeOffset? Deadline,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int TotalRounds = 0,
    int Players = 0,
    string? Title = null,
    string? Role = null,
    bool? Won = null);

public sealed record SummaryPlayerDto(Guid Id, string Nickname, string AvatarColor, int Seat, string Role, bool Won);

public sealed record SummaryDto(
    Guid GameId,
    IReadOnlyList<SummaryPlayerDto> Players,
    IReadOnlyList<BoardRowView> Board,
    IReadOnlyList<int> Truth,
    IReadOnlyList<HintGroupView> Hints,
    IReadOnlyList<LetterRecord> Letters,
    FinaleView Finale);

/// <summary>Профиль, рейтинг, мои партии и итоги партии.</summary>
public sealed class ProfileService(GhostLettersDbContext db, GameService games)
{
    public async Task<ProfileDto> GetProfileAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw AppException.NotFound("Игрок не найден.");
        var stats = await db.Stats.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct) ?? new UserStats { UserId = userId };
        var titles = await db.Nominations.AsNoTracking().ToDictionaryAsync(n => n.Code, n => n.Title, ct);
        var achievements = await db.UserAchievements.AsNoTracking()
            .Where(a => a.UserId == userId)
            .GroupBy(a => a.NominationCode)
            .Select(g => new { Code = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return new ProfileDto(
            UserDto.From(user),
            new StatsDto(stats.Games, stats.Wins, stats.Rating, stats.LikesReceived,
                JsonSerializer.Deserialize<Dictionary<string, int>>(stats.RoleWins) ?? new Dictionary<string, int>()),
            achievements
                .OrderByDescending(a => a.Count).ThenBy(a => a.Code)
                .Select(a => new AchievementDto(a.Code, titles.GetValueOrDefault(a.Code) ?? a.Code, a.Count))
                .ToList(), user.IsBot);
    }

    /// <summary>Таблица лидеров; боты — по желанию (галочка «показать ботов»).</summary>
    public async Task<IReadOnlyList<LeaderboardRow>> LeaderboardAsync(int limit, CancellationToken ct, bool bots = false)
    {
        var rows = await (
                from s in db.Stats.AsNoTracking()
                join u in db.Users.AsNoTracking() on s.UserId equals u.Id
                // Старые боты и кабинет могут содержать одноимённые аккаунты.
                // Показываем профиль кабинета, иначе самого раннего бота (как при
                // добавлении в лобби). Статистика разных аккаунтов не складывается.
                where s.Games > 0 && (!u.IsBot || (bots && u.Id == db.Users
                    .Where(b => b.IsBot && b.Nickname == u.Nickname)
                    .OrderByDescending(b => db.BotProfiles.Any(p => p.UserId == b.Id))
                    .ThenBy(b => b.CreatedAt).ThenBy(b => b.Id)
                    .Select(b => b.Id).First()))
                orderby s.Rating descending, s.Wins descending, u.Nickname
                select new { u, s.Rating, s.Games, s.Wins })
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct);
        return rows.Select(r => new LeaderboardRow(UserDto.From(r.u), r.Rating, r.Games, r.Wins, r.u.IsBot)).ToList();
    }

    /// <summary>Мои партии; «ваш ход» — есть доступные команды (кроме лайков).</summary>
    public async Task<IReadOnlyList<MyGameDto>> MyGamesAsync(Guid userId, string? status, CancellationToken ct)
    {
        var query =
            from p in db.GamePlayers.AsNoTracking()
            join g in db.Games.AsNoTracking() on p.GameId equals g.Id
            where p.UserId == userId
            select g;
        query = status switch
        {
            null or "" => query,
            "active" => query.Where(g => g.Status == GameStatuses.Active),
            "finished" => query.Where(g => g.Status == GameStatuses.Finished),
            _ => throw AppException.Validation("status — active или finished."),
        };

        var list = await query.OrderByDescending(g => g.StartedAt).Take(50).ToListAsync(ct);
        var lobbyIds = list.Where(g => g.LobbyId != null).Select(g => g.LobbyId!.Value).Distinct().ToList();
        var titles = await db.Lobbies.AsNoTracking().Where(l => lobbyIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Title, ct);
        return list.Select(g =>
        {
            var state = GameStore.Read(g);
            var me = state.Player(userId);
            var allowed = GameProjection.AllowedCommands(state, me);
            return new MyGameDto(g.Id, g.LobbyId, g.Status, g.Phase, state.Round,
                allowed.Any(c => c != nameof(Domain.Game.Like)), g.PhaseDeadline, g.StartedAt, g.FinishedAt,
                state.TotalRounds, state.Players.Count,
                g.LobbyId is { } lobbyId ? titles.GetValueOrDefault(lobbyId) : null,
                me.Role.ToString(),
                state.Result is { } result ? (bool?)result.Winners.Contains(userId) : null);
        }).ToList();
    }

    /// <summary>Итоги: роли, истина, все письма по раундам, голоса, охота, награды. Только после результата.</summary>
    public async Task<SummaryDto> SummaryAsync(Guid gameId, Guid userId, CancellationToken ct)
    {
        await games.RequireViewerAsync(gameId, userId, ct);
        var game = await db.Games.AsNoTracking().FirstAsync(g => g.Id == gameId, ct);
        var state = GameStore.Read(game);
        if (state.Result is null)
        {
            throw AppException.Conflict(AppException.Codes.GameInProgress, "Итоги будут после финала.");
        }

        var ids = state.Players.Select(p => p.Id).ToList();
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var view = GameProjection.For(state, null);
        return new SummaryDto(
            gameId,
            state.Players.OrderBy(p => p.Seat).Select(p => new SummaryPlayerDto(
                p.Id, users[p.Id].Nickname, users[p.Id].AvatarColor, p.Seat, p.Role.ToString(), state.Result.Winners.Contains(p.Id)))
                .ToList(),
            view.Board,
            state.Truth!,
            view.Hints,
            state.Letters,
            view.Finale!);
    }
}
