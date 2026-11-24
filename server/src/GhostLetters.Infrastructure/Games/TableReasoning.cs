using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Persistence.Entities;

namespace GhostLetters.Infrastructure.Games;

/// <summary>Same public card statements for humans, bots and sandbox. Never reads hidden truth.</summary>
public static class TableReasoning
{
    public const string Tentative = "думаю, эта (догадка)";
    // Posterior odds at least twice the initial odds of a card in this row.
    public static bool HasPrivateSupport(double probability, int columns) => probability * (columns - 1) >= 2 * (1 - probability);
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
            yield return new(author, cards[i], negative ? -1 : note.StartsWith(Tentative, StringComparison.Ordinal) ? .2 : 1,
                note.StartsWith("проверял", StringComparison.Ordinal) && !note.Contains("думаю", StringComparison.Ordinal), source);
        }
    }

    public static (string? Id, int Round, int Number) SupportingHint(PlayerView view, CardTags.BoardReading reading,
        BotPersonality personality, string target)
    {
        var focus = BotTuning.Current.FocusBase + BotTuning.Current.FocusStrict * personality.Strictness;
        var best = view.Hints.SelectMany(h => h.Cards.Select((id, i) =>
            (Id: id, h.Round, Number: i + 1, Weight: reading.Support(id, target, focus))))
            .OrderByDescending(h => h.Weight).FirstOrDefault();
        return best.Weight >= GhostCluePolicy.PlausibleMatch(personality.Risk) ? (best.Id, best.Round, best.Number) : default;
    }

    public static SendChatRequest Speech(PlayerView view, CardTags tags, BotMind? mind,
        (string Text, IReadOnlyList<string> Cards, IReadOnlyList<string> Notes) line)
    {
        if (line.Notes.Any(IsSource) || line.Cards.Count == 0 || line.Notes.FirstOrDefault() == Tentative)
            return new(ChatChannels.Public, line.Text, null, line.Cards, line.Notes);
        var target = line.Cards[0];
        var personality = mind?.Personality ?? BotPersonality.Default;
        var source = SupportingHint(view, tags.AtBoard(view.Board, personality), personality, target).Id;
        if (source is null || line.Cards.Contains(source)) return new(ChatChannels.Public, line.Text, null, line.Cards, line.Notes);
        // Only the first hypothesis was actually explained by this clue. Other
        // rows retain standalone attachments, without invented links to them.
        return new(ChatChannels.Public, line.Text, null, new[] { source }.Concat(line.Cards.Take(4)).ToList(),
            new[] { "улика" }.Concat(line.Cards.Take(4).Select((_, i) =>
                (i < line.Notes.Count ? line.Notes[i] : "думаю, эта") + (i == 0 ? ":0" : ""))).ToList());
    }
}
