using System.Text.RegularExpressions;
using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Infrastructure.Speech;

/// <summary>
/// Что человек сказал голосом — в понятном ботам виде: какие карты назвал (по номеру в ряду, «мотив три», или по
/// названию и синонимам из разметки), отвергал ли их («не клетка», «точно не ухо») и называл ли своё письмо
/// («кидал кошку»). Получаются те же карты с подписями, что у показа на столе: «думаю, эта», «исключаю», «кидал эту».
/// </summary>
public static partial class SpokenCards
{
    private static readonly Dictionary<string, int> Numbers = new()
    {
        ["один"] = 1, ["одна"] = 1, ["первая"] = 1, ["первый"] = 1, ["первую"] = 1, ["раз"] = 1,
        ["два"] = 2, ["две"] = 2, ["вторая"] = 2, ["второй"] = 2, ["вторую"] = 2,
        ["три"] = 3, ["третья"] = 3, ["третий"] = 3, ["третью"] = 3,
        ["четыре"] = 4, ["четвёртая"] = 4, ["четвертая"] = 4, ["четвёртый"] = 4, ["четвертый"] = 4, ["четвёртую"] = 4, ["четвертую"] = 4,
        ["пять"] = 5, ["пятая"] = 5, ["пятый"] = 5, ["пятую"] = 5,
        ["шесть"] = 6, ["шестая"] = 6, ["шестой"] = 6, ["шестую"] = 6,
        ["семь"] = 7, ["седьмая"] = 7, ["седьмой"] = 7, ["седьмую"] = 7,
    };

    [GeneratedRegex(@"[а-яё]+|\d+")]
    private static partial Regex Words();

    private static readonly string[] Negations = ["не", "нет", "исключаю", "исключаем", "исключить", "мимо", "отпадает", "вычеркиваю", "вычёркиваю"];
    private static readonly string[] Claims = ["кидал", "кидала", "отправлял", "отправляла", "отправил", "отправила", "скинул", "скинула", "письмо"];

    private static string Stem(string word) => word.Length > 4 ? word[..^2] : word.Length >= 3 ? word[..3] : word;

    private static readonly IReadOnlyDictionary<string, Category> Categories = new Dictionary<string, Category>
    {
        ["мотив"] = Category.Motive, ["место"] = Category.Place, ["способ"] = Category.Method, ["тайна"] = Category.Secret,
    };

    public static (List<string> Cards, List<string> Notes) Read(string text, IReadOnlyList<BoardRow> board,
        IReadOnlyList<HintGroup> hints, IReadOnlyList<string> ownLetters, CardTags tags)
    {
        var words = Words().Matches(text.ToLowerInvariant().Replace('ё', 'е')).Select(m => m.Value).ToList();
        var found = new List<(int At, string Card, bool Own)>();

        // «мотив три», «место — вторая», «способ 4»: категория (в любом падеже) и номер следом.
        for (var i = 0; i + 1 < words.Count; i++)
        {
            var category = Categories.FirstOrDefault(c => words[i].StartsWith(Stem(c.Key), StringComparison.Ordinal)).Key;
            if (category is null) continue;
            var number = int.TryParse(words[i + 1], out var n) ? n : Numbers.GetValueOrDefault(words[i + 1]);
            var row = board.FirstOrDefault(r => r.Category == Categories[category]);
            if (row is null || number < 1 || number > row.Cards.Count) continue;
            found.Add((i, row.Cards[number - 1], false));
        }

        // По названиям: карты поля, подсказки и свои письма. Все слова названия — подряд в речи (по основам).
        var candidates = board.SelectMany(r => r.Cards).Select(c => (Card: c, Own: false))
            .Concat(hints.SelectMany(h => h.Cards).Select(c => (Card: c, Own: false)))
            .Concat(ownLetters.Select(c => (Card: c, Own: true)));
        foreach (var (card, own) in candidates)
        {
            foreach (var name in tags.NamesOf(card))
            {
                var parts = Words().Matches(name.ToLowerInvariant().Replace('ё', 'е')).Select(m => Stem(m.Value)).Where(p => p.Length >= 3).ToList();
                if (parts.Count == 0) continue;
                for (var i = 0; i + parts.Count <= words.Count; i++)
                {
                    if (!parts.Select((p, k) => words[i + k].StartsWith(p, StringComparison.Ordinal)).All(x => x)) continue;
                    found.Add((i, card, own));
                    break;
                }
            }
        }

        var cards = new List<string>();
        var notes = new List<string>();
        foreach (var (at, card, own) in found.OrderBy(f => f.At))
        {
            if (cards.Contains(card)) continue;
            var before = words.Skip(Math.Max(0, at - 3)).Take(Math.Min(3, at)).ToList();
            var note = own && before.Any(w => Claims.Contains(w)) ? "кидал эту"
                : before.Any(w => Negations.Contains(w)) ? "исключаю"
                : own ? null : "думаю, эта";
            if (note is null) continue;
            // Своё письмо «кидал» — только из своих писем; карта поля не может быть «кидал эту».
            if (note == "кидал эту" && !ownLetters.Contains(card)) note = "думаю, эта";
            cards.Add(card);
            notes.Add(note);
            if (cards.Count >= ChatService.MaxCards) break;
        }

        return (cards, notes);
    }
}
