using System.Text.RegularExpressions;

namespace GhostLetters.Infrastructure.Speech;

/// <summary>
/// Живая речь вместо шаблона: реплика бота (уже с названиями карт) пересказывается вслух разными словами.
/// Каждое типовое предложение — несколько вариантов, плюс манера бота (вступления) и род по голосу
/// («проверяла», «согласна»). Незнакомые предложения остаются как есть. Смысл и карты не меняются.
/// </summary>
public static partial class SpokenStyle
{
    /// <summary>Манера речи: постоянная для бота, выбирается по его id.</summary>
    public enum Manner { Calm, Lively, Formal }

    public static Manner MannerOf(Guid botId) => (Manner)(botId.ToByteArray()[1] % 3);

    private static readonly IReadOnlyDictionary<Manner, string[]> Openers = new Dictionary<Manner, string[]>
    {
        [Manner.Calm] = ["Так.", "Ну что ж.", "Смотрите.", "Значит, так.", "Хм."],
        [Manner.Lively] = ["Слушайте!", "О, смотрите.", "Так-так.", "Ну вот.", "Интересно."],
        [Manner.Formal] = ["Итак.", "Позвольте.", "Изложу коротко.", "Что ж, коллеги.", "Разберёмся."],
    };

    private sealed record Rule(Regex Pattern, string[] Variants);

    private static Rule R(string pattern, params string[] variants) =>
        new(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), variants);

    // Порядок важен: первое совпадение. {name} — группы шаблона; {why?} — «, общее — …», если группа есть.
    private static readonly Rule[] Rules =
    [
        R(@"^(?:Отправлял|Я отправлял|Кидал|Я кидал) вот (?:эту|это) — (?<card>.+?) — (?:исчезла|не открылась)$",
            "Моё письмо — {card}. Призрак его не открыл.",
            "Я отправлял {card}, и письмо исчезло.",
            "{card} — это было моё письмо. Не открылось.",
            "Кидал {card} — увы, письмо пропало.",
            "Моё письмо, {card}, Призрак оставил закрытым."),
        R(@"^(?:Моё письмо — вот это|Я отправлял вот эту|Это моё письмо) — (?<card>.+?)(?:,| —) (?:оно открылось|она открылась|открылось)$",
            "Моё письмо открылось — это {card}.",
            "{card} — моё письмо, и Призрак его открыл!",
            "Открылось моё письмо: {card}.",
            "Да, {card} — это я отправлял. Открылось."),
        R(@"^Отправил вот (?:эту|это) — (?<card>.+?), ждём Призрака$",
            "Я отправил {card}. Ждём, что скажет Призрак.",
            "Моё письмо — {card}. Посмотрим, откроется ли.",
            "{card} ушло Призраку, ждём."),
        R(@"^Проверял: (?<list>.+?) — (?<why>(?:по смыслу|по форме|по цвету|по деталям).+)$",
            "Этим письмом я проверял: {list}. Общее — {why}.",
            "Целился в {list}: {why}.",
            "Проверял {list} — {why}.",
            "Я думал про {list}, потому что {why}."),
        R(@"^Проверял: явной связи с полем нет$",
            "Явной связи с полем у моего письма не было.",
            "Честно, письмо ни к чему на поле не подходило.",
            "Связи с полем я тогда не видел."),
        R(@"^Раз исчезло — это ослабляет эти версии, но не исключает: другие улики могут перевесить$",
            "Раз исчезло, эти карты слабее, но не вычеркнуты.",
            "Это их ослабляет, но не хоронит — посмотрим на другие улики.",
            "Минус им, но не приговор."),
        R(@"^Раз Призрак его не открыл, эти карты под сомнением, но не вычеркнуты$",
            "Значит, эти карты под вопросом, но пока в игре.",
            "Под сомнением — но не вычёркиваю.",
            "Сомнительно, но списывать рано."),
        R(@"^Исчезло — значит, скорее не они\. Но одной проверки мало$",
            "Значит, скорее не они. Хотя одной проверки мало.",
            "Скорее всего, не они — но я бы ещё проверил.",
            "Похоже, мимо. Но одно письмо — ещё не доказательство."),
        R(@"^Открытие письма усиливает эти связи$",
            "Раз открылось — эти связи крепче.",
            "Это подтверждает мою догадку.",
            "Открылось — значит, я на верном пути."),
        R(@"^Пока жду результат проверки$",
            "Посмотрим, что покажет Призрак.",
            "Жду, откроется ли письмо.",
            "Пока ждём."),
        R(@"^Версия: (?<rest>.+)$",
            "Моя версия: {rest}",
            "{rest}",
            "Думаю так: {rest}",
            "Я бы сказал: {rest}"),
        R(@"^(?<hint>(?:подсказка \S+ раунда|зацепка)(?: — [^—]+?)?) указывает на (?<target>.+?): (?<why>.+)$",
            "{hint} — это, по-моему, {target}: {why}.",
            "{hint}? Я вижу тут {target}, {why}.",
            "{hint} ведёт к карте {target} — {why}.",
            "Смотрите: {hint} и {target} — {why}."),
        R(@"^Целиком думаю так: (?<list>.+)$",
            "А в целом у меня так: {list}.",
            "Если собрать всё вместе: {list}.",
            "Итого: {list}.",
            "Вся картина, по-моему: {list}."),
        R(@"^Подозреваю, что (?<who>.+?) из чёрных(?:: (?<why>.+))?$",
            "И ещё: {who}, по-моему, играет за чёрных{why?}.",
            "{who} мне не нравится — похоже, это кто-то из чёрных{why?}.",
            "Подозреваю: {who} — из чёрных{why?}.",
            "Я бы последил за игроком {who}{why?}."),
        R(@"^(?<who>[^,]{2,24}), согласен$",
            "{who}, согласен.",
            "{who}, поддерживаю.",
            "{who}, да, я тоже так думаю.",
            "Тут я с тобой, {who}."),
        R(@"^(?<who>[^,]{2,24}), тут не соглашусь$",
            "{who}, не соглашусь.",
            "{who}, а я вот думаю иначе.",
            "{who}, спорно.",
            "Нет, {who}, тут я с тобой не согласен."),
    ];

    [GeneratedRegex(@"(?<=[.!?…])\s+")]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"по (?:смыслу|форме|цвету|деталям): ")]
    private static partial Regex ReasonLabel();

    /// <summary>Пересказать реплику вслух. <paramref name="rng"/> — свой для каждой реплики, чтобы повторы звучали по-разному.</summary>
    /// <summary>Мысль типового предложения бота — для выученных у людей фраз (их места {card}, {player}).</summary>
    private static string? IntentOf(Rule rule)
    {
        var p = rule.Pattern.ToString();
        if (p.StartsWith("^(?:Отправлял", StringComparison.Ordinal)) return PhraseBook.Intents.ClaimLost;
        if (p.StartsWith("^(?:Моё письмо", StringComparison.Ordinal)) return PhraseBook.Intents.ClaimOpen;
        if (p.StartsWith("^Версия:", StringComparison.Ordinal)) return PhraseBook.Intents.Version;
        if (p.StartsWith("^Подозреваю", StringComparison.Ordinal)) return PhraseBook.Intents.Accuse;
        if (p.EndsWith(", согласен$", StringComparison.Ordinal)) return PhraseBook.Intents.Agree;
        if (p.EndsWith(", тут не соглашусь$", StringComparison.Ordinal)) return PhraseBook.Intents.Disagree;
        return null;
    }

    [GeneratedRegex(@"(?:мотив|место|способ|тайна) — (?<card>[^.,!?:;—]+)", RegexOptions.IgnoreCase)]
    private static partial Regex NamedCard();

    /// <summary>Доля реплик, которую бот говорит выученной у людей фразой (если есть подходящая).</summary>
    public const double LearnedShare = .45;

    /// <param name="learned">Фразы людей той же манеры: мысль, эмоция, шаблон.</param>
    /// <param name="emotions">Эмоции, которые подходят боту.</param>
    public static string Rephrase(string spoken, Random rng, Manner manner, bool male,
        IReadOnlyList<(string Intent, string Emotion, string Template)>? learned = null, IReadOnlyCollection<string>? emotions = null)
    {
        if (string.IsNullOrWhiteSpace(spoken)) return spoken;
        var sentences = SentenceBreak().Split(spoken.Trim()).Where(s => s.Length > 0).ToList();
        var result = new List<string>();
        foreach (var raw in sentences)
        {
            var sentence = raw.TrimEnd('.', ' ');
            var rule = Rules.FirstOrDefault(r => r.Pattern.IsMatch(sentence));
            if (rule is null)
            {
                result.Add(raw.Trim());
                continue;
            }

            var match = rule.Pattern.Match(sentence);
            if (learned is { Count: > 0 } && IntentOf(rule) is { } intent && rng.NextDouble() < LearnedShare)
            {
                var card = match.Groups["card"].Success ? match.Groups["card"].Value.Trim()
                    : match.Groups["rest"].Success && NamedCard().Match(match.Groups["rest"].Value) is { Success: true } named ? named.Groups["card"].Value.Trim()
                    : null;
                var who = match.Groups["who"].Success ? match.Groups["who"].Value.Trim() : null;
                // Письмо: подходит и фраза про исход («исчезло»), и нейтральная («я кидал …»).
                var options = learned.Where(l => (l.Intent == intent || (l.Intent == PhraseBook.Intents.Claim && intent is PhraseBook.Intents.ClaimLost or PhraseBook.Intents.ClaimOpen))
                    && (emotions is null || emotions.Contains(l.Emotion))).ToList();
                if (options.Count > 0)
                {
                    var said = PhraseBook.Fill(options[rng.Next(options.Count)].Template, card, who);
                    if (said.Length > 0)
                    {
                        result.Add(EndSentence(said));
                        continue;
                    }
                }
            }

            var variant = rule.Variants[rng.Next(rule.Variants.Length)];
            var text = Fill(variant, match);
            result.Add(EndSentence(Capitalize(text)));
        }

        // Вступление — не каждый раз: иначе оно само становится шаблоном.
        if (result.Count > 0 && rng.NextDouble() < .35)
        {
            var openers = Openers[manner];
            result.Insert(0, openers[rng.Next(openers.Length)]);
        }

        // Вслух приставку «Бот» у имён не произносят.
        var joined = Regex.Replace(string.Join(" ", result), @"(?<![а-яёА-ЯЁ])Бот (?=[А-ЯЁ])", "");
        return male ? joined : Feminine(joined);
    }

    private static string Fill(string variant, Match match)
    {
        var text = Regex.Replace(variant, @"\{(?<name>\w+)(?<opt>\?)?\}", m =>
        {
            var group = match.Groups[m.Groups["name"].Value];
            if (m.Groups["opt"].Success)
                return group.Success && group.Value.Length > 0 ? ": " + Reasons(group.Value) : "";
            var value = group.Success ? group.Value.Trim() : "";
            return m.Groups["name"].Value == "why" ? Reasons(value) : value;
        });
        return Regex.Replace(text, @"\s+", " ").Replace(" .", ".").Replace("..", ".").Trim();
    }

    /// <summary>«по смыслу: звук; по форме: острые выступы» → «звук и острые выступы» (не больше двух доводов).</summary>
    private static string Reasons(string why)
    {
        var parts = why.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => ReasonLabel().Replace(p, "").Trim()).Where(p => p.Length > 0).Distinct().Take(2).ToList();
        return parts.Count == 0 ? why : string.Join(" и ", parts);
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string EndSentence(string s) => s.Length == 0 || ".!?…".Contains(s[^1]) ? s : s + ".";

    private static readonly (string Male, string Female)[] Endings =
    [
        ("отправлял", "отправляла"), ("отправил", "отправила"), ("кидал", "кидала"), ("проверял", "проверяла"),
        ("думал", "думала"), ("целился", "целилась"), ("видел", "видела"), ("сказал", "сказала"), ("согласен", "согласна"),
        ("уверен", "уверена"), ("проверил", "проверила"), ("пересмотрел", "пересмотрела"), ("собрал", "собрала"),
        ("выбрал", "выбрала"), ("голосовал", "голосовала"), ("бы сказал", "бы сказала"), ("бы проверил", "бы проверила"), ("бы присмотрелся", "бы присмотрелась"),
        ("бы последил", "бы последила"), ("готов", "готова"),
    ];

    /// <summary>Род по голосу: женский голос говорит «проверяла», «согласна». «Призрак открыл/оставил» не трогаем.</summary>
    public static string Feminine(string text)
    {
        foreach (var (male, female) in Endings)
        {
            if (male == female) continue;
            text = Regex.Replace(text, $@"(?<![а-яё]){Regex.Escape(male)}(?![а-яё])", m =>
                char.IsUpper(m.Value[0]) ? char.ToUpperInvariant(female[0]) + female[1..] : female, RegexOptions.IgnoreCase);
        }

        return text;
    }
}
