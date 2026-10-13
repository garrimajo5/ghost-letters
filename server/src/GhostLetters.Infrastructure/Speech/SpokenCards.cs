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
        ["первое"] = 1, ["второе"] = 2, ["третье"] = 3, ["четвертое"] = 4, ["пятое"] = 5, ["шестое"] = 6, ["седьмое"] = 7,
    };

    [GeneratedRegex(@"[а-яё]+|\d+")]
    private static partial Regex Words();

    [GeneratedRegex(@"(ый|ий|ой|ая|яя|ое|ее|ые|ие|ую|юю)$")]
    private static partial Regex Adjective();

    private static readonly string[] Negations = ["не", "нет", "исключаю", "исключаем", "исключить", "мимо", "отпадает", "вычеркиваю", "вычёркиваю"];
    private static readonly string[] Claims = ["кидал", "кидала", "отправлял", "отправляла", "отправил", "отправила", "скинул", "скинула", "письмо"];

    private static string Stem(string word) => word.Length > 4 ? word[..^2] : word.Length >= 3 ? word[..3] : word;

    private static readonly IReadOnlyDictionary<string, Category> Categories = new Dictionary<string, Category>
    {
        ["мотив"] = Category.Motive, ["место"] = Category.Place, ["способ"] = Category.Method, ["тайна"] = Category.Secret,
    };

    /// <summary>Сказанное, но не узнанное название карты: что сказали, в каком ряду (если назвали ряд) и с каким смыслом.</summary>
    public sealed record Unknown(string Phrase, Category? Row, string Note);

    public sealed record Result(List<string> Cards, List<string> Notes, Unknown? Unclear)
    {
        public void Deconstruct(out List<string> cards, out List<string> notes) => (cards, notes) = (Cards, Notes);
    }

    private static readonly HashSet<string> Stop =
    [
        "это", "эту", "этот", "эта", "эти", "вот", "тот", "та", "ту", "те", "карта", "карту", "карты", "картой", "ряд", "ряду",
        "мне", "кажется", "думаю", "наверное", "точно", "всё", "все", "тут", "там", "она", "оно", "он", "они", "ну", "так", "как",
        "что", "который", "которая", "которую", "номер", "первая", "вторая", "его", "её", "ее", "них", "моя", "мою", "мой", "своё",
        "свою", "мое", "моё", "письмо", "письмом", "подсказка", "подсказку", "зацепка", "зацепку", "сюда", "туда", "просто", "очень",
        "тоже", "уже", "ещё", "еще", "был", "была", "было", "будет", "мотив", "место", "способ", "тайна", "ставлю", "голосую",
        "кидал", "кидала", "отправлял", "отправляла", "отправил", "отправила", "согласен", "согласна", "нет", "да", "или",
    ];

    /// <param name="aliases">Как этот игрок называл карты раньше: фраза (нижний регистр) → карта.</param>
    public static Result Read(string text, IReadOnlyList<BoardRow> board,
        IReadOnlyList<HintGroup> hints, IReadOnlyList<string> ownLetters, CardTags tags,
        IReadOnlyDictionary<string, string>? aliases = null)
    {
        var words = Words().Matches(text.ToLowerInvariant().Replace('ё', 'е')).Select(m => m.Value).ToList();
        var found = new List<(int At, int Length, string Card, bool Own)>();
        var categoryAt = new Dictionary<int, Category>();

        // «мотив три», «место — вторая», «способ 4»: категория (в любом падеже) и номер следом.
        for (var i = 0; i < words.Count; i++)
        {
            var category = Categories.FirstOrDefault(c => words[i].StartsWith(Stem(c.Key), StringComparison.Ordinal)).Key;
            if (category is null) continue;
            categoryAt[i] = Categories[category];
            if (i + 1 >= words.Count) continue;
            var number = int.TryParse(words[i + 1], out var n) ? n : Numbers.GetValueOrDefault(words[i + 1]);
            var row = board.FirstOrDefault(r => r.Category == Categories[category]);
            if (row is null || number < 1 || number > row.Cards.Count) continue;
            found.Add((i, 2, row.Cards[number - 1], false));
        }

        // По названиям: карты поля, подсказки и свои письма. Все слова названия — подряд в речи (по основам).
        var candidates = board.SelectMany(r => r.Cards).Select(c => (Card: c, Own: false))
            .Concat(hints.SelectMany(h => h.Cards).Select(c => (Card: c, Own: false)))
            .Concat(ownLetters.Select(c => (Card: c, Own: true))).ToList();
        var known = candidates.Select(c => c.Card).ToHashSet();
        var names = candidates.SelectMany(c => tags.NamesOf(c.Card).Select(name => (c.Card, c.Own, Name: name)));
        if (aliases is not null)
            names = names.Concat(aliases.Where(a => known.Contains(a.Value))
                .Select(a => (Card: a.Value, Own: ownLetters.Contains(a.Value), Name: a.Key)));
        foreach (var (card, own, name) in names.ToList())
        {
            var parts = Words().Matches(name.ToLowerInvariant().Replace('ё', 'е')).Select(m => Stem(m.Value)).Where(p => p.Length >= 3).ToList();
            if (parts.Count == 0) continue;
            for (var i = 0; i + parts.Count <= words.Count; i++)
            {
                if (!parts.Select((p, k) => words[i + k].StartsWith(p, StringComparison.Ordinal)).All(x => x)) continue;
                found.Add((i, parts.Count, card, own));
                break;
            }
        }

        string NoteAt(int at, bool own)
        {
            var before = words.Skip(Math.Max(0, at - 3)).Take(Math.Min(3, at)).ToList();
            return own && before.Any(w => Claims.Contains(w)) ? "кидал эту"
                : before.Any(w => Negations.Contains(w)) ? "исключаю"
                : "думаю, эта";
        }

        var cards = new List<string>();
        var notes = new List<string>();
        foreach (var (at, _, card, own) in found.OrderBy(f => f.At))
        {
            if (cards.Contains(card)) continue;
            var note = NoteAt(at, own);
            // Своё письмо, названое без «кидал», — не версия о поле: пропускаем.
            if (own && note != "кидал эту" && !board.Any(r => r.Cards.Contains(card))) continue;
            if (note == "кидал эту" && !ownLetters.Contains(card)) note = "думаю, эта";
            cards.Add(card);
            notes.Add(note);
            if (cards.Count >= ChatService.MaxCards) break;
        }

        return new Result(cards, notes, FindUnknown(words, found.Select(f => (f.At, f.Length)).ToList(), categoryAt, NoteAt));
    }

    /// <summary>
    /// Название карты, которое не узнали: после ряда («мотив — грабли»), «ставлю на …», «голосую за …», «кидал …»,
    /// «точно не …». Одно-два слова, не служебные и не узнанные карты.
    /// </summary>
    private static Unknown? FindUnknown(List<string> words, List<(int At, int Length)> found,
        Dictionary<int, Category> categoryAt, Func<int, bool, string> noteAt)
    {
        bool Covered(int i) => found.Any(f => i >= f.At && i < f.At + f.Length);
        bool Content(int i) => i < words.Count && words[i].Length >= 3 && !Stop.Contains(words[i]) && !Covered(i) && !int.TryParse(words[i], out _) &&
                               !Numbers.ContainsKey(words[i]);
        for (var i = 0; i < words.Count; i++)
        {
            int start;
            Category? row = null;
            if (categoryAt.TryGetValue(i, out var category) && !Covered(i)) { start = i + 1; row = category; }
            else if ((words[i] is "на" && i > 0 && words[i - 1].StartsWith("став", StringComparison.Ordinal)) ||
                     (words[i] is "за" && i > 0 && words[i - 1].StartsWith("голос", StringComparison.Ordinal)) ||
                     Claims.Contains(words[i]) && words[i] != "письмо" ||
                     (words[i] is "не" && i > 0 && words[i - 1] == "точно")) start = i + 1;
            else continue;
            while (start < words.Count && Stop.Contains(words[start]) && words[start] is "это" or "эту" or "карту" or "вот") start++;
            if (!Content(start)) continue;
            // Два слова — только «прилагательное + существительное»: «ржавые грабли», а не «грабли потому».
            var adjective = Adjective().IsMatch(words[start]);
            var phrase = adjective && Content(start + 1) ? words[start] + " " + words[start + 1] : words[start];
            var claim = Claims.Contains(words[i]);
            var note = claim ? "кидал эту" : noteAt(start, false);
            return new Unknown(phrase, row, note);
        }

        return null;
    }
}
