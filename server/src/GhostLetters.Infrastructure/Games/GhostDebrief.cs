using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Bots;

namespace GhostLetters.Infrastructure.Games;

/// <summary>Retrospective card analysis. Never called with a live, unresolved game.</summary>
public static class GhostDebrief
{
    public const string Completed = "Разбор завершён.";
    public const string Prefix = "Разбор Призрака";
    public sealed record Speech(string Text, IReadOnlyList<string> Cards, IReadOnlyList<string> Notes);

    public static IReadOnlyList<Speech> Compose(GameState state, CardTags tags, BotMind mind)
    {
        if (state.Result is null || state.Truth is null) return [];
        var view = GameProjection.For(state, state.Ghost.Id);
        var p = mind.Personality;
        var truth = view.Board.Select((r, i) => r.Cards[state.Truth[i]]).ToList();
        var letters = state.Hints.Where(h => h.Round == 0).SelectMany(h => h.Cards)
            .Select(c => (Card: c, Round: 0, Revealed: true, From: (Guid?)null))
            .Concat(state.Letters.Select(l => (Card: l.CardId, l.Round, l.Revealed, From: (Guid?)l.From))).ToList();
        var result = new List<Speech>();
        foreach (var letter in letters)
        {
            var target = truth.OrderByDescending(c => tags.Similarity(letter.Card, c, p.Attention, p.Details, p.SecondaryMeanings)).First();
            var assessment = GhostCluePolicy.Assess(view, tags, mind, letter.Card);
            var reason = tags.Explain(letter.Card, target, p.Attention, p.Details, p.SecondaryMeanings);
            var verdict = letter.Revealed ? "Открыл это письмо" : "Оставил это письмо закрытым";
            var analysis = assessment.TrueMatch < .08 ? "Связь с истиной очень слабая." :
                assessment.FalseMatch >= assessment.TrueMatch ? "Неоднозначная карта: ложная улика выглядит не менее убедительно. Открывать её — риск." :
                "Истинная улика выделяется лучше ложных.";
            var feedback = letter.From is { } author && mind.Names.TryGetValue(author, out var name)
                ? assessment.TrueMatch > .18 && assessment.TrueMatch > assessment.FalseMatch
                    ? $" {name}, спасибо за понятную связь."
                    : p.Compromise < .4 ? $" {name}, такая карта могла запутать стол; лучше искать более точную связь." : " В следующий раз попробуем найти связь яснее."
                : "";
            var text = $"{Prefix} · раунд {letter.Round}. {verdict}. Разбираю по итогам: {analysis} Связь с истинной уликой: {reason}.{feedback}";
            result.Add(new(text.Length <= 1000 ? text : text[..1000], [letter.Card, target], [letter.Revealed ? "открыл" : "не открыл", "истинная улика"]));
        }
        if (result.Count > 0)
        {
            var last = result[^1];
            result[^1] = last with { Text = last.Text[..Math.Min(last.Text.Length, 970)] + "\n" + Completed };
        }
        return result;
    }
}
