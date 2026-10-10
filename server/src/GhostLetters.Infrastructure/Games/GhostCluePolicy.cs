using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;

namespace GhostLetters.Infrastructure.Games;

public static class GhostCluePolicy
{
    public sealed record Assessment(double TrueMatch, double FalseMatch, double Risk, bool Reveal)
    {
        public string Reason => TrueMatch < .08 ? "связь с истиной слишком слабая" :
            FalseMatch >= TrueMatch ? "может сильнее увести к ложной карте" : "истинная карта читается яснее ложных";
    }

    public static Assessment Assess(PlayerView view, CardTags tags, BotMind mind, string letter)
    {
        if (view.Me?.Role != Role.Ghost || view.Truth is not { } truth) return new(0, 0, 0, false);
        var p = mind.Personality;
        var trueCards = view.Board.Select((r, i) => r.Cards[truth[i]]).ToHashSet();
        double Match(string card) => tags.Similarity(letter, card, p.Attention, p.Details, p.SecondaryMeanings);
        var yes = trueCards.Select(Match).DefaultIfEmpty().Max();
        var no = view.Board.SelectMany(r => r.Cards).Where(c => !trueCards.Contains(c)).Select(Match).DefaultIfEmpty().Max();
        // Only expressed public opinions, never somebody else's private plan.
        var publicOpinions = mind.Opinions.Where(o => !o.IsCheck && o.Strength > 0 &&
            view.Players.Any(player => player.Id == o.Author && !player.IsGhost &&
                !(player.KnownRole?.IsKillerTeam() ?? false))).ToList();
        var missing = trueCards.Count(c => !publicOpinions.Any(o => o.CardId == c)) / (double)Math.Max(1, trueCards.Count);
        var urgency = publicOpinions.Count == 0 ? 0 : missing * (view.TotalRounds == 1 && view.Round == 1 ? 1 : Math.Clamp((view.Round - 1.0) / Math.Max(1, view.TotalRounds - 1), 0, 1));
        var risk = Math.Clamp(p.Risk + (1 - p.Risk) * urgency * .8, 0, 1);
        var ranked = view.Board.SelectMany(r => r.Cards).OrderByDescending(Match).ToList();
        var breadth = Math.Max(1, (int)Math.Round(mind.TableBreadth));
        var readable = ranked.Take(breadth).Any(trueCards.Contains) ? yes : yes * .4;
        var reveal = readable >= .18 - .10 * risk && yes - no >= .06 - .60 * risk;
        return new(yes, no, risk, reveal);
    }

    public static IReadOnlyList<string> Choose(PlayerView view, CardTags tags, BotMind mind, IEnumerable<string> cards) =>
        cards.Select(c => (Card: c, Value: Assess(view, tags, mind, c)))
            .Where(x => x.Value.Reveal)
            .OrderByDescending(x => x.Value.TrueMatch - x.Value.FalseMatch * (1 - x.Value.Risk))
            .Select(x => x.Card).ToList();
}
