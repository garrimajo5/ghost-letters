using GhostLetters.Application;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Speech;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Infrastructure.Voice;

/// <summary>Сказанное голосом: распознанный текст и то, что из него поняли (карты с подписями).</summary>
public sealed record TranscriptDto(string Text, IReadOnlyList<string> CardIds, IReadOnlyList<string> CardNotes, ChatMessageDto? Message);

/// <summary>Голосовой чат партии: доступ к комнате и «уши» ботов — расшифровка сказанного в чат.</summary>
public sealed class VoiceService(GhostLettersDbContext db, GameService games, VoiceRooms rooms, SpeechKit speech,
    ChatService chat, CardTagStore tagStore)
{
    /// <summary>Префикс расшифровки в чате: по нему клиент показывает «голосом».</summary>
    public const string TranscriptPrefix = "Голосом: ";

    public const long MaxTranscriptBytes = 1_100_000;

    public async Task<VoiceAccess> AccessAsync(Guid gameId, Guid userId, CancellationToken ct)
    {
        if (!rooms.Enabled) throw new AppException("VOICE_OFF", "Голосовой чат не настроен на сервере.", 503);
        await games.RequireViewerAsync(gameId, userId, ct);
        var game = await db.Games.AsNoTracking().FirstAsync(g => g.Id == gameId, ct);
        var name = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Nickname).FirstOrDefaultAsync(ct) ?? "?";
        return rooms.Access(GameStore.Read(game), userId, name);
    }

    /// <summary>
    /// Кусок речи игрока (WAV 16 кГц моно, до 30 с) → текст в общий чат от его имени, с картами, которые он назвал.
    /// Так боты «слышат» людей. Говорить можно было только со словом — значит, и расшифровку принимаем лишь у тех,
    /// кому сейчас (или только что, при передаче слова) можно говорить.
    /// </summary>
    public async Task<TranscriptDto> TranscribeAsync(Guid gameId, Guid userId, Stream audio, long length, CancellationToken ct)
    {
        if (length is <= 0 or > MaxTranscriptBytes) throw AppException.Validation("Запись — до 30 секунд.");
        var viewer = await games.RequireViewerAsync(gameId, userId, ct);
        if (viewer.IsObserver) throw AppException.Forbidden("Зритель не говорит.");
        var game = await db.Games.AsNoTracking().FirstAsync(g => g.Id == gameId, ct);
        var state = GameStore.Read(game);
        var player = state.Player(userId);
        if (player.Role == Role.Ghost && state.Result is null) throw AppException.Forbidden("Призрак до итогов молчит.");
        using var buffer = new MemoryStream();
        await audio.CopyToAsync(buffer, ct);
        var text = await speech.RecognizeAsync(buffer.ToArray(), 16000, ct);
        if (text is null) return new TranscriptDto("", [], [], null);

        var tags = await tagStore.LoadAsync(ct);
        var own = state.Letters.Where(l => l.From == userId).Select(l => l.CardId).ToList();
        var (cards, notes) = SpokenCards.Read(text, state.Board, state.Hints, own, tags);
        var line = TranscriptPrefix + text;
        if (line.Length > ChatService.MaxTextLength) line = line[..ChatService.MaxTextLength];
        var message = await chat.SendAsync(gameId, userId, new SendChatRequest(ChatChannels.Public, line, null, cards, notes), ct);
        return new TranscriptDto(text, cards, notes, message);
    }
}
