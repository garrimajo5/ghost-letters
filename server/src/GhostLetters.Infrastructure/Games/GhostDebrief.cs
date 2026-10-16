using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Bots;

namespace GhostLetters.Infrastructure.Games;

/// <summary>
/// Разбор Призрака после итогов — как рассказал бы живой Призрак: по раунду на реплику, коротко,
/// кого открыл и почему, и пару самых заметных закрытых писем. Раньше — реплика на каждое письмо (до 30 подряд).
/// Never called with a live, unresolved game.
/// </summary>
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
            .Concat(state.Letters.Select(l => (Card: l.CardId, l.Round, Revealed: l.Revealed == true, From: (Guid?)l.From))).ToList();
        string Name(Guid? id)
        {
            if (id == state.Ghost.Id) return "моё";
            if (id is not { } who || !mind.Names.TryGetValue(who, out var name)) return "письмо";
            return BotNames.Bare(name);
        }

        string Short(string reason)
        {
            if (reason.Contains("явной связи", StringComparison.Ordinal)) return "";
            var first = reason.Split(';')[0].Trim();
            var at = first.IndexOf(": ", StringComparison.Ordinal);
            return at > 0 ? first[(at + 2)..] : first;
        }

        var result = new List<Speech>();
        foreach (var round in letters.GroupBy(l => l.Round).OrderBy(g => g.Key))
        {
            var rated = round.Select(l =>
            {
                var target = truth.OrderByDescending(c => tags.Similarity(l.Card, c, p.Attention, p.Details, p.SecondaryMeanings)).First();
                return (Letter: l, Target: target, Value: GhostCluePolicy.Assess(view, tags, mind, l.Card),
                    Reason: Short(tags.Explain(l.Card, target, p.Attention, p.Details, p.SecondaryMeanings)));
            }).ToList();
            var parts = new List<string>();
            var cards = new List<string>();
            var notes = new List<string>();
            if (round.Key == 0)
            {
                var clue = rated[0];
                parts.Add(clue.Reason.Length > 0 ? $"Зацепку дал такую: {clue.Reason} — это про истину." : "Зацепку дал вот эту.");
                cards.Add(clue.Letter.Card);
                notes.Add("открыл");
            }
            else
            {
                var opened = rated.Where(x => x.Letter.Revealed).ToList();
                var closed = rated.Where(x => !x.Letter.Revealed).OrderByDescending(x => x.Value.TrueMatch).ToList();
                if (opened.Count > 0)
                {
                    var good = opened.Where(x => x.Value.TrueMatch > .18 && x.Value.TrueMatch > x.Value.FalseMatch).Select(x => Name(x.Letter.From)).Distinct().ToList();
                    var first = opened[0];
                    var who = opened.Select(x => Name(x.Letter.From)).Distinct().ToList();
                    parts.Add($"Открыл {(who.Count == 1 ? "письмо" : "письма")}: {string.Join(", ", who)}" +
                        (first.Reason.Length > 0 ? $" — {first.Reason}." : "."));
                    if (good.Count > 0) parts.Add($"{string.Join(", ", good)}, спасибо за понятную связь.");
                    foreach (var x in opened.Take(3)) { cards.Add(x.Letter.Card); notes.Add("открыл"); }
                }
                else parts.Add("Ничего не открыл.");

                var told = closed.Take(cards.Count >= 3 ? 1 : 2).ToList();
                foreach (var x in told)
                {
                    var why = x.Value.TrueMatch < .08 ? "связь с истиной слабая"
                        : x.Value.FalseMatch >= x.Value.TrueMatch ? "похоже и на ложную карту, открывать — риск"
                        : p.Compromise < .4 ? "можно было открыть, но я перестраховался" : "почти подошло";
                    var whose = Name(x.Letter.From);
                    parts.Add((whose == "моё" ? "Своё письмо оставил закрытым" : $"{whose} — оставил закрытым") +
                        $": {why}" + (x.Reason.Length > 0 && x.Value.TrueMatch >= .08 ? $" ({x.Reason})." : "."));
                }

                // Карты всех закрытых писем — на виду (до пяти карт в реплике), даже если о них не сказано отдельно.
                foreach (var x in told.Concat(closed.Except(told)))
                {
                    if (cards.Count >= 5) break;
                    cards.Add(x.Letter.Card);
                    notes.Add("не открыл");
                }
            }

            var text = $"{Prefix} · раунд {round.Key}. {string.Join(" ", parts)}";
            result.Add(new(text.Length <= 1000 ? text : text[..1000], cards.Take(5).ToList(), notes.Take(5).ToList()));
        }

        if (result.Count > 0)
        {
            var last = result[^1];
            result[^1] = last with { Text = last.Text[..Math.Min(last.Text.Length, 970)] + "\n" + Completed };
        }
        return result;
    }
}
