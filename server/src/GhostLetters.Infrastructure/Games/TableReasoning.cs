using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Persistence.Entities;

namespace GhostLetters.Infrastructure.Games;

/// <summary>Same public card statements for humans, bots and sandbox. Never reads hidden truth.</summary>
public static class TableReasoning
{
    public static bool IsSource(string note) => note == "улика" || note.StartsWith("кидал", StringComparison.Ordinal);

    public static IEnumerable<ChatOpinion> Read(Guid author, IReadOnlyList<string> cards, IReadOnlyList<string> notes,
        IReadOnlySet<string> board)
    {
        for (var i = 0; i < cards.Count; i++)
        {
            var note = i < notes.Count ? notes[i] : "";
            if (!board.Contains(cards[i]) || IsSource(note)) continue;
            var negative = note.StartsWith("исключ", StringComparison.OrdinalIgnoreCase) || note.StartsWith("не эта", StringComparison.OrdinalIgnoreCase);
            var source = int.TryParse(note.Split(':').Last(), out var index) && index >= 0 && index < cards.Count &&
                index < notes.Count && IsSource(notes[index]) ? cards[index] : null;
            yield return new(author, cards[i], negative ? -1 : 1,
                note.StartsWith("проверял", StringComparison.Ordinal) && !note.Contains("думаю", StringComparison.Ordinal), source);
        }
    }

    public static SendChatRequest Speech(PlayerView view, CardTags tags, BotMind? mind,
        (string Text, IReadOnlyList<string> Cards, IReadOnlyList<string> Notes) line)
    {
        if (line.Notes.Any(IsSource) || line.Cards.Count == 0)
            return new(ChatChannels.Public, line.Text, null, line.Cards, line.Notes);
        var target = line.Cards[0];
        var p = mind?.Personality ?? BotPersonality.Default;
        var source = view.Hints.SelectMany(h => h.Cards).Distinct().Where(c => !line.Cards.Contains(c))
            .Select(c => (Card: c, Score: tags.Similarity(c, target, p.Attention, p.Details, p.SecondaryMeanings)))
            .Where(x => x.Score > .01).OrderByDescending(x => x.Score).FirstOrDefault();
        if (source.Card is null) return new(ChatChannels.Public, line.Text, null, line.Cards, line.Notes);
        // Only the first hypothesis was actually explained by this clue. Other
        // rows retain standalone attachments, without invented links to them.
        return new(ChatChannels.Public, line.Text, null, new[] { source.Card }.Concat(line.Cards.Take(4)).ToList(),
            new[] { "улика" }.Concat(line.Cards.Take(4).Select((_, i) =>
                (i < line.Notes.Count ? line.Notes[i] : "думаю, эта") + (i == 0 ? ":0" : ""))).ToList());
    }
}
