using GhostLetters.Application;
using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Infrastructure.Games;

public sealed record ChatMessageDto(
    Guid Id,
    Guid GameId,
    int Round,
    string Channel,
    Guid? AuthorId,
    string Kind,
    string? Text,
    Guid? MediaId,
    int? DurationMs,
    IReadOnlyList<string> CardIds,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string>? CardNotes = null);

/// <param name="CardNotes">Необязательные подписи под картами, по порядку CardIds.</param>
public sealed record SendChatRequest(string? Channel, string? Text, Guid? MediaId, IReadOnlyList<string>? CardIds,
    IReadOnlyList<string>? CardNotes = null);

public sealed record MediaDto(Guid MediaId, int DurationMs, string ContentType);

/// <summary>
/// Чат партии и голосовые. Общий канал — для обсуждения; канал команды Убийцы — только Убийце и Сообщникам
/// (Призрак его читает, как и общий). Призрак до итогов не пишет; в режиме рации пишет говорящий
/// и тот, кому он дал слово.
/// </summary>
public sealed class ChatService(
    GhostLettersDbContext db,
    GameService games,
    IMediaStorage storage,
    MediaLimits mediaLimits,
    IRealtimeNotifier notifier,
    TimeProvider time)
{
    public const int MaxTextLength = 1000;
    public const int MaxCards = 5;
    public const int MaxNoteLength = 30;
    public const int MaxVoiceMs = 60_000;
    public const long MaxVoiceBytes = 2 * 1024 * 1024;

    public static readonly string[] VoiceTypes =
        ["audio/aac", "audio/mp4", "audio/m4a", "audio/x-m4a", "audio/mpeg", "audio/ogg", "audio/webm", "audio/wav"];

    public async Task<ChatMessageDto> SendAsync(Guid gameId, Guid userId, SendChatRequest request, CancellationToken ct)
    {
        var channel = request.Channel ?? ChatChannels.Public;
        if (channel is not (ChatChannels.Public or ChatChannels.KillerTeam))
        {
            throw AppException.Validation("Канал — public или killer_team.");
        }

        var viewer = await games.RequireViewerAsync(gameId, userId, ct);
        if (viewer.IsObserver)
        {
            throw AppException.Forbidden("Зритель не пишет в чат.");
        }

        var state = await LoadStateAsync(gameId, ct);
        var author = state.Player(userId);
        RequireCanWrite(state, author, channel);

        var text = request.Text?.Trim();
        if (text is { Length: > MaxTextLength })
        {
            throw AppException.Validation($"Сообщение — до {MaxTextLength} символов.");
        }

        Media? media = null;
        await using var mediaTransaction = request.MediaId is not null ? await db.Database.BeginTransactionAsync(ct) : null;
        if (request.MediaId is { } mediaId)
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74293821)", ct);
            media = await db.MediaFiles.FirstOrDefaultAsync(m => m.Id == mediaId && m.OwnerId == userId && m.ContentType.StartsWith("audio/"), ct)
                    ?? throw AppException.Validation("Голосовое не найдено.");
        }

        if (string.IsNullOrEmpty(text) && media is null)
        {
            throw AppException.Validation("Пустое сообщение.");
        }

        var cards = (request.CardIds ?? []).Distinct().ToList();
        var notes = (request.CardNotes ?? []).Select(n => (n ?? "").Trim()).ToList();
        if (notes.Count > cards.Count || notes.Any(n => n.Length > MaxNoteLength))
        {
            throw AppException.Validation($"Подписи — по одной на карту, до {MaxNoteLength} символов.");
        }

        // Свои письма тоже можно показать: «отправлял вот эту».
        var known = state.Board.SelectMany(r => r.Cards).Concat(state.Hints.SelectMany(h => h.Cards)).Concat(author.Hand)
            .Concat(state.Letters.Where(l => l.From == author.Id).Select(l => l.CardId))
            .ToHashSet();
        if (cards.Count <= MaxCards && cards.Any(c => !known.Contains(c)))
        {
            // Повторять можно показанные карты, а не скрытые чужие письма.
            // В общий канал не переносим карты, известные только из командного.
            var shown = await db.ChatMessages.AsNoTracking()
                .Where(m => m.GameId == gameId && (m.Channel == ChatChannels.Public ||
                    (channel == ChatChannels.KillerTeam && m.Channel == ChatChannels.KillerTeam)))
                .Select(m => m.CardIds).ToListAsync(ct);
            known.UnionWith(shown.SelectMany(ids => ids));
        }
        if (cards.Count > MaxCards || cards.Any(c => !known.Contains(c)))
        {
            throw AppException.Validation($"Можно упомянуть до {MaxCards} карт с поля, подсказок, своей руки, своих писем или доступного чата.");
        }

        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            GameId = gameId,
            Round = state.Round,
            Channel = channel,
            AuthorId = userId,
            Kind = media is null ? ChatKinds.Text : ChatKinds.Voice,
            Text = string.IsNullOrEmpty(text) ? null : text,
            MediaId = media?.Id,
            CardIds = cards,
            CardNotes = notes,
            CreatedAt = time.GetUtcNow(),
        };
        db.ChatMessages.Add(message);
        await db.SaveChangesAsync(ct);
        if (mediaTransaction is not null) await mediaTransaction.CommitAsync(ct);

        var dto = Dto(message, media?.DurationMs);
        var recipients = Recipients(state, channel);
        await notifier.ChatAsync(dto, recipients, toTable: channel == ChatChannels.Public, ct);
        return dto;
    }

    /// <summary>История чата, новые снизу. Канал команды Убийцы виден только его участникам и Призраку.</summary>
    public async Task<IReadOnlyList<ChatMessageDto>> HistoryAsync(Guid gameId, Guid userId, string? channel,
        DateTimeOffset? before, int limit, CancellationToken ct)
    {
        var viewer = await games.RequireViewerAsync(gameId, userId, ct);
        var state = await LoadStateAsync(gameId, ct);
        var channels = viewer.IsObserver ? new List<string> { ChatChannels.Public } : VisibleChannels(state, state.Player(userId));
        if (channel is not null)
        {
            channels = channels.Where(c => c == channel).ToList();
        }

        var rows = await (
                from m in db.ChatMessages.AsNoTracking()
                join media in db.MediaFiles.AsNoTracking() on m.MediaId equals media.Id into mm
                from media in mm.DefaultIfEmpty()
                where m.GameId == gameId && channels.Contains(m.Channel) && (before == null || m.CreatedAt < before)
                orderby m.CreatedAt descending, m.Id descending
                select new { m, Duration = media == null ? (int?)null : media.DurationMs })
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);
        return rows.Select(x => Dto(x.m, x.Duration)).Reverse().ToList();
    }

    public async Task<MediaDto> UploadVoiceAsync(Guid userId, Stream content, long length, string? contentType, int durationMs,
        CancellationToken ct)
    {
        var type = (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        if (!VoiceTypes.Contains(type))
        {
            throw AppException.Validation("Неподдерживаемый формат голосового.");
        }

        if (length is <= 0 or > MaxVoiceBytes)
        {
            throw AppException.Validation($"Голосовое — до {MaxVoiceBytes / 1024 / 1024} МБ.");
        }

        if (durationMs is <= 0 or > MaxVoiceMs)
        {
            throw AppException.Validation($"Голосовое — до {MaxVoiceMs / 1000} секунд.");
        }

        using var buffer = await MediaLimits.ReadAsync(content, length, MaxVoiceBytes, ct);
        if (!MediaLimits.IsVoice(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), type))
            throw AppException.Validation("Содержимое не соответствует формату голосового.");

        var media = new Media
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            ContentType = type,
            DurationMs = durationMs,
            SizeBytes = length,
            CreatedAt = time.GetUtcNow(),
        };
        media.StorageKey = media.Id.ToString("N");
        await mediaLimits.SaveAsync(media, buffer, null, ct);
        return new MediaDto(media.Id, media.DurationMs, media.ContentType);
    }

    /// <summary>Голосовое доступно автору и тем, кто видит сообщение с ним.</summary>
    public async Task<(Stream Content, string ContentType)> OpenVoiceAsync(Guid userId, Guid mediaId, CancellationToken ct)
    {
        var media = await db.MediaFiles.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mediaId, ct)
                    ?? throw AppException.NotFound("Голосовое не найдено.");
        if (media.OwnerId != userId)
        {
            var messages = await db.ChatMessages.AsNoTracking()
                .Where(m => m.MediaId == mediaId).Select(m => new { m.GameId, m.Channel }).ToListAsync(ct);
            var allowed = false;
            foreach (var m in messages)
            {
                var visible = await HistoryChannelsAsync(m.GameId, userId, ct);
                allowed |= visible.Contains(m.Channel);
            }

            if (!allowed)
            {
                throw AppException.Forbidden("Нет доступа к этому голосовому.");
            }
        }

        var stream = storage.Open(media.StorageKey) ?? throw AppException.NotFound("Файл голосового потерян.");
        return (stream, media.ContentType);
    }

    public static IReadOnlyList<Guid> Recipients(GameState state, string channel) =>
        state.Players.Where(p => VisibleChannels(state, p).Contains(channel)).Select(p => p.Id).ToList();

    public static List<string> VisibleChannels(GameState state, PlayerState player) =>
        player.Role.IsKillerTeam() || player.Role == Role.Ghost || state.Result is not null
            ? [ChatChannels.Public, ChatChannels.KillerTeam]
            : [ChatChannels.Public];

    private static void RequireCanWrite(GameState state, PlayerState author, string channel)
    {
        if (channel == ChatChannels.KillerTeam)
        {
            if (!author.Role.IsKillerTeam())
            {
                throw AppException.Forbidden("Это канал команды Убийцы.");
            }

            return;
        }

        if (state.Result is not null)
        {
            return;
        }

        if (author.Role == Role.Ghost)
        {
            throw new AppException(GameRuleException.Codes.NotAllowed, "Призрак общается только подсказками.", 403);
        }

        if (state.Phase == Phase.Discussion && state.EffectiveDiscussion == DiscussionMode.Radio &&
            state.CurrentSpeaker != author.Id && state.FloorGrantedTo != author.Id)
        {
            throw new AppException(GameRuleException.Codes.NotYourTurn, "Сейчас говорит другой игрок.", 409);
        }
    }

    private async Task<List<string>> HistoryChannelsAsync(Guid gameId, Guid userId, CancellationToken ct)
    {
        try
        {
            var viewer = await games.RequireViewerAsync(gameId, userId, ct);
            var state = await LoadStateAsync(gameId, ct);
            return viewer.IsObserver ? [ChatChannels.Public] : VisibleChannels(state, state.Player(userId));
        }
        catch (AppException)
        {
            return [];
        }
    }

    private async Task<GameState> LoadStateAsync(Guid gameId, CancellationToken ct)
    {
        var game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId, ct)
                   ?? throw AppException.NotFound("Партия не найдена.");
        return GameStore.Read(game);
    }

    private static ChatMessageDto Dto(ChatMessage m, int? durationMs) => new(
        m.Id, m.GameId, m.Round, m.Channel, m.AuthorId, m.Kind, m.Text, m.MediaId, durationMs, m.CardIds, m.CreatedAt,
        m.CardNotes);
}
