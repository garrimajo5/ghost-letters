using System.Collections.Concurrent;
using GhostLetters.Application;
using GhostLetters.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Infrastructure.Games;

/// <summary>Эмодзи-реакция за столом: не хранится, просто летит всем участникам партии.</summary>
public sealed record ReactionDto(Guid GameId, Guid UserId, string Emoji, DateTimeOffset At);

/// <summary>
/// Реакции за столом. Набор эмодзи фиксирован (не превращается в обходной чат), частота ограничена:
/// не больше <see cref="Burst"/> за <see cref="Window"/> на игрока в партии.
/// </summary>
public sealed class ReactionService(GhostLettersDbContext db, IRealtimeNotifier notifier, ReactionLimiter limiter, TimeProvider time)
{
    public static readonly string[] Allowed = ["👍", "👎", "😂", "😮", "🤔", "👻", "🔥", "❤️", "😱", "🙈"];

    public async Task<ReactionDto> SendAsync(Guid gameId, Guid userId, string? emoji, CancellationToken ct)
    {
        if (emoji is null || !Allowed.Contains(emoji)) throw AppException.Validation("Такой реакции нет.");
        var players = await db.GamePlayers.AsNoTracking().Where(p => p.GameId == gameId).Select(p => p.UserId).ToListAsync(ct);
        if (players.Count == 0) throw AppException.NotFound("Партия не найдена.");
        if (!players.Contains(userId)) throw AppException.Forbidden("Реагировать могут только игроки партии.");
        var now = time.GetUtcNow();
        if (!limiter.TryAcquire(gameId, userId, now)) throw AppException.Conflict("RATE_LIMIT", "Не так часто — подождите пару секунд.");
        var reaction = new ReactionDto(gameId, userId, emoji, now);
        await notifier.ReactionAsync(reaction, players, ct);
        return reaction;
    }
}

/// <summary>Скользящее окно в памяти процесса: реакции не хранятся, их лимит — тоже.</summary>
public sealed class ReactionLimiter
{
    public const int Burst = 6;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<(Guid, Guid), Queue<DateTimeOffset>> _recent = new();

    public bool TryAcquire(Guid gameId, Guid userId, DateTimeOffset now)
    {
        var queue = _recent.GetOrAdd((gameId, userId), _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() >= Window) queue.Dequeue();
            if (queue.Count >= Burst) return false;
            queue.Enqueue(now);
        }

        // Старые партии не копятся: пустые очереди убираем.
        if (_recent.Count > 10_000)
        {
            foreach (var (key, q) in _recent)
            {
                lock (q)
                {
                    if (q.Count == 0 || now - q.Peek() >= Window) _recent.TryRemove(key, out _);
                }
            }
        }

        return true;
    }
}
