using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;

namespace GhostLetters.Infrastructure.Games;

public static class BotLetterTactics
{
    public const string WaitLine = "Сначала послушаю, кто какое письмо отправлял. Своё пока не называю.";

    private static bool IsClaim(DiscussionLine message, int index) =>
        (message.Notes is { } notes && index < notes.Count && notes[index].StartsWith("кидал", StringComparison.OrdinalIgnoreCase)) ||
        (index == 0 && new[] { "Я отправлял вот эту", "Я кидал эту", "Кидал эту", "Моё письмо — вот это", "Это моё письмо" }
            .Any(prefix => message.Text.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));

    public static bool Pending(Guid bot, IReadOnlyList<DiscussionLine> messages)
    {
        var wait = messages.LastOrDefault(m => m.Author == bot && m.Text == WaitLine);
        return wait is not null && !messages.Any(m => m.Author == bot && m.At > wait.At &&
            m.Notes?.Any(n => n.StartsWith("кидал", StringComparison.Ordinal)) == true);
    }

    public static (string Text, IReadOnlyList<string> Cards, IReadOnlyList<string> Notes)? Compose(
        PlayerView view, BotMind mind, IReadOnlyList<DiscussionLine> messages, Random rng, DateTimeOffset? now = null)
    {
        if (view.Me is not { Role: not Role.Ghost } me) return null;
        var own = messages.Where(m => m.Author == me.Id).ToList();
        var letter = me.Letters.LastOrDefault(l => l.Round == view.Round);
        var claims = messages.SelectMany(m => m.Cards.Select((c, i) => (Message: m, Card: c, Index: i)))
            .Where(x => IsClaim(x.Message, x.Index)).ToList();
        var remaining = view.Players.Where(p => !p.IsGhost && p.Id != me.Id && !p.HasActed && !messages.Any(m => m.Author == p.Id)).ToList();
        var waited = own.FirstOrDefault(m => m.Text == WaitLine);
        if (waited is not null && !own.Any(m => m.At > waited.At && m.Notes?.Any(n => n.StartsWith("кидал", StringComparison.Ordinal)) == true) && letter is not null)
        {
            var thief = claims.LastOrDefault(c => c.Card == letter.CardId && c.Message.Author != me.Id);
            if (thief.Message is not null)
                return ($"{mind.Names.GetValueOrDefault(thief.Message.Author, "Игрок")}, ты назвал моё письмо своим. Я отправлял вот эту карту. Подозреваю, что {mind.Names.GetValueOrDefault(thief.Message.Author, "Игрок")} из чёрных.", [letter.CardId], ["кидал эту"]);
            if ((now ?? messages.LastOrDefault()?.At ?? waited.At) - waited.At >= TimeSpan.FromSeconds(45) || remaining.Count == 0 || messages.Count(m => m.At > waited.At && m.Author != me.Id) >= view.Players.Count - 2)
                return ("Проверил заявления: никто не назвал мою карту. Это письмо отправлял я.", [letter.CardId], ["кидал эту"]);
            return null;
        }
        if (own.Count > 0) return null;
        var p = mind.Personality;
        var cunning = (p.Memory + p.Compromise) / 2;
        var suspectedLast = remaining.LastOrDefault();
        var suspicious = suspectedLast is not null && mind.AccusationList.Any(a => a.Target == suspectedLast.Id && a.Strength > .3);
        if (view.Discussion == DiscussionMode.FreeChat && letter?.Revealed == true && p.Risk > .6 && cunning > .6 &&
            (suspicious || me.Role.IsKillerTeam()) && !claims.Any(c => c.Card == letter.CardId))
            return (WaitLine, [], []);
        var late = remaining.Count == 0 || remaining.All(p => p.KnownRole?.IsKillerTeam() == true);
        if (!me.Role.IsKillerTeam() || (!late && p.Risk < .8) || rng.NextDouble() >= (late ? .25 + .65 * p.Risk : .4 * p.Risk)) return null;
        var unclaimed = view.Hints.Where(h => h.Round == view.Round).SelectMany(h => h.Cards)
            .Where(c => c != letter?.CardId && !claims.Any(x => x.Card == c)).ToList();
        return unclaimed.Count == 0 ? null : ("Я отправлял вот эту — она открылась.", [unclaimed[rng.Next(unclaimed.Count)]], ["кидал эту"]);
    }
}
