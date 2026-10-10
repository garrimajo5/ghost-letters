using System.Text.Json;
using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Infrastructure.Games;

/// <summary>
/// Переносит итоги партии в таблицы для профиля: голоса, статистику, рейтинг, лайки, ачивки.
/// Вызывается в той же транзакции, что и ход, поэтому итоги не теряются и не дублируются.
/// </summary>
public sealed class GameRecorder(GhostLettersDbContext db, GhostLetters.Infrastructure.Bots.BotRelationshipService relationships)
{
    public const int EloK = 32;

    public async Task RecordAsync(Game game, GameState state, bool hadResult, Phase phaseBefore, DateTimeOffset now, CancellationToken ct)
    {
        if (state.Result is null)
        {
            return;
        }

        if (!hadResult)
        {
            RecordVotes(game.Id, state, now);
            await RecordStatsAndRatingAsync(game.Id, state, now, ct);
        }

        await SyncLikesAsync(game.Id, state, ct);

        if (phaseBefore != Phase.Finished && state.Phase == Phase.Finished)
        {
            RecordAwards(game.Id, state, now);
            await relationships.RecordAsync(state, now, ct);
        }
    }

    /// <summary>Победа команды: детективы с Призраком против Убийцы с Сообщниками. Остальные роли рейтинг не меняют.</summary>
    public static (int Detectives, int Killers) EloDelta(double detectivesRating, double killersRating, WinningSide side)
    {
        var score = side switch
        {
            WinningSide.Detectives => 1.0,
            WinningSide.Killer => 0.0,
            _ => 0.5,
        };
        var expected = 1.0 / (1.0 + Math.Pow(10, (killersRating - detectivesRating) / 400.0));
        var delta = (int)Math.Round(EloK * (score - expected), MidpointRounding.AwayFromZero);
        return (delta, -delta);
    }

    private void RecordVotes(Guid gameId, GameState state, DateTimeOffset now)
    {
        db.Votes.AddRange(state.VoteRecords.Select(v => new Vote
        {
            Id = Guid.NewGuid(),
            GameId = gameId,
            Stage = v.Stage,
            StageKind = state.VoteStages[v.Stage].Kind == VoteStageKind.Row ? "row" : "killer",
            Attempt = v.Attempt,
            VoterId = v.Voter,
            Column = v.Column,
            SuspectId = v.Suspect,
            CreatedAt = now,
        }));
    }

    private async Task RecordStatsAndRatingAsync(Guid gameId, GameState state, DateTimeOffset now, CancellationToken ct)
    {
        var result = state.Result!;
        var ids = state.Players.Select(p => p.Id).ToList();
        await LockStatsAsync(ids, ct);
        var stats = await db.Stats.Where(s => ids.Contains(s.UserId)).ToDictionaryAsync(s => s.UserId, ct);
        foreach (var id in ids.Where(id => !stats.ContainsKey(id)))
        {
            stats[id] = new UserStats { UserId = id };
            db.Stats.Add(stats[id]);
        }

        foreach (var p in state.Players)
        {
            var s = stats[p.Id];
            s.Games++;
            if (result.Winners.Contains(p.Id))
            {
                s.Wins++;
                var roleWins = JsonSerializer.Deserialize<Dictionary<string, int>>(s.RoleWins) ?? new Dictionary<string, int>();
                roleWins[p.Role.ToString()] = roleWins.GetValueOrDefault(p.Role.ToString()) + 1;
                s.RoleWins = JsonSerializer.Serialize(roleWins);
            }
        }

        // Рейтинг меняется только в рейтинговой партии — для всех, включая ботов.
        if (!state.Settings.Ranked)
        {
            return;
        }

        // Кооператив (без Убийцы) рейтинг не меняет: соперника нет, сравнивать не с кем.
        var detectives = state.Players.Where(p => p.Role.IsDetectiveTeam()).ToList();
        var killers = state.Players.Where(p => p.Role.IsKillerTeam()).ToList();
        if (!state.HasKiller || detectives.Count == 0 || killers.Count == 0)
        {
            return;
        }

        var (dDelta, kDelta) = EloDelta(
            detectives.Average(p => stats[p.Id].Rating),
            killers.Average(p => stats[p.Id].Rating),
            result.Side);
        var changes = detectives.Select(p => (p, dDelta)).Concat(killers.Select(p => (p, kDelta)));
        foreach (var (p, delta) in changes)
        {
            var s = stats[p.Id];
            s.Rating += delta;
            db.RatingHistoryRecords.Add(new RatingHistory
            {
                Id = Guid.NewGuid(), UserId = p.Id, GameId = gameId, Delta = delta, RatingAfter = s.Rating, CreatedAt = now,
            });
        }
    }

    /// <summary>
    /// Один игрок (чаще всего бот из кабинета) может закончить несколько партий одновременно. Строку user_stats
    /// читаем и меняем под блокировкой транзакции по игроку, иначе одна партия затрёт счёт другой.
    /// Блокируем в одном порядке (по id), чтобы две партии не ждали друг друга по кругу.
    /// </summary>
    private async Task LockStatsAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Статистику игроков пишут только внутри транзакции хода.");
        }

        foreach (var id in userIds.Distinct().Order())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({id.ToString()}, 3))", ct);
        }
    }

    /// <summary>Лайки можно ставить и снимать до конца и после — таблица повторяет состояние партии.</summary>
    private async Task SyncLikesAsync(Guid gameId, GameState state, CancellationToken ct)
    {
        var existing = await db.Likes.Where(l => l.GameId == gameId).ToListAsync(ct);
        var wanted = state.Likes.Select(l => (l.From, l.To)).ToHashSet();
        var removed = existing.Where(l => !wanted.Contains((l.FromUserId, l.ToUserId))).ToList();
        var added = wanted.Where(w => !existing.Any(l => l.FromUserId == w.From && l.ToUserId == w.To)).ToList();
        if (removed.Count == 0 && added.Count == 0)
        {
            return;
        }

        db.Likes.RemoveRange(removed);
        db.Likes.AddRange(added.Select(a => new Persistence.Entities.Like { GameId = gameId, FromUserId = a.From, ToUserId = a.To }));

        var changed = removed.Select(r => r.ToUserId).Concat(added.Select(a => a.To)).Distinct().ToList();
        await LockStatsAsync(changed, ct);
        var stats = await db.Stats.Where(s => changed.Contains(s.UserId)).ToListAsync(ct);
        foreach (var s in stats)
        {
            s.LikesReceived += added.Count(a => a.To == s.UserId) - removed.Count(r => r.ToUserId == s.UserId);
        }
    }

    private void RecordAwards(Guid gameId, GameState state, DateTimeOffset now)
    {
        for (var i = 0; i < state.Awards.Count; i++)
        {
            var entry = state.Awards[i];
            var rows = entry.NominatedBy.Select(n => new AwardNomination
            {
                Id = Guid.NewGuid(), GameId = gameId, NominatorId = n, NomineeId = entry.Nominee, NominationCode = entry.Code,
            }).ToList();
            db.AwardNominations.AddRange(rows);
            db.AwardVotes.AddRange(entry.Voters.Select(v => new Persistence.Entities.AwardVote
            {
                GameId = gameId, VoterId = v, AwardNominationId = rows[0].Id,
            }));

            if (state.AwardWinners.Contains(i))
            {
                db.UserAchievements.Add(new UserAchievement
                {
                    Id = Guid.NewGuid(),
                    UserId = entry.Nominee,
                    NominationCode = entry.Code,
                    GameId = gameId,
                    Votes = entry.Voters.Count,
                    AwardedAt = now,
                });
            }
        }
    }
}
