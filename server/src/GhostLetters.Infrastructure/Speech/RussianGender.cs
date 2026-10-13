using System.Text.RegularExpressions;

namespace GhostLetters.Infrastructure.Speech;

/// <summary>
/// Род в репликах ботов. Бот говорит о себе в своём роде («я кидала»), к собеседнику обращается в его роде
/// («Аня, ты права», «дорогая моя»), о третьем — тоже в его роде («Аня ошиблась», «её версия»).
/// Кто подлежащее, решает ближайшее местоимение перед словом: «я» — говорящий, «ты» — собеседник,
/// «он/она/вы» и имя прямо перед словом — кто-то третий (такое слово при смене рода говорящего не трогаем).
/// </summary>
public static partial class RussianGender
{
    /// <summary>Слова, род которых виден по форме (мужская → женская).</summary>
    private static readonly (string Male, string Female)[] Pairs =
    [
        ("отправлял", "отправляла"), ("отправил", "отправила"), ("кидал", "кидала"), ("проверял", "проверяла"),
        ("думал", "думала"), ("целился", "целилась"), ("видел", "видела"), ("сказал", "сказала"), ("согласен", "согласна"),
        ("уверен", "уверена"), ("проверил", "проверила"), ("пересмотрел", "пересмотрела"), ("собрал", "собрала"),
        ("выбрал", "выбрала"), ("голосовал", "голосовала"), ("бы сказал", "бы сказала"), ("бы проверил", "бы проверила"), ("бы присмотрелся", "бы присмотрелась"),
        ("бы последил", "бы последила"), ("готов", "готова"), ("кинул", "кинула"), ("скинул", "скинула"), ("ставил", "ставила"),
        ("ошибся", "ошиблась"), ("понял", "поняла"), ("заметил", "заметила"), ("решил", "решила"),
    ];

    /// <summary>
    /// Ещё формы — только для собеседника и названного игрока: о себе бот их почти не говорит, а «Призрак был прав»
    /// при смене рода говорящего ломалось бы.
    /// </summary>
    private static readonly (string Male, string Female)[] OthersPairs =
    [
        ("был", "была"), ("прав", "права"), ("неправ", "неправа"), ("должен", "должна"), ("рад", "рада"),
        ("ошибался", "ошибалась"), ("говорил", "говорила"), ("предлагал", "предлагала"), ("назвал", "назвала"),
        ("промахнулся", "промахнулась"), ("поторопился", "поторопилась"), ("запутался", "запуталась"),
    ];

    /// <summary>Обращения, у которых есть род: «дорогой мой» — «дорогая моя».</summary>
    private static readonly (string Male, string Female)[] Vocatives =
    [
        ("дорогой мой", "дорогая моя"), ("мой дорогой", "моя дорогая"), ("мой милый", "моя милая"), ("дорогой", "дорогая"),
        ("голубчик", "голубушка"), ("сударь", "сударыня"), ("приятель", "подруга"), ("братец", "сестрица"),
        ("батенька", "матушка"), ("молодой человек", "барышня"), ("мон шер", "ма шер"), ("mon cher", "ma chère"),
        ("друг мой", "подруга моя"), ("мой друг", "моя подруга"),
    ];

    /// <summary>О третьем лице в репликах ботов: «его версия» — «её версия», «против него» — «против неё».</summary>
    private static readonly (string Male, string Female)[] ThirdPerson =
    [
        ("его версия", "её версия"), ("его версию", "её версию"), ("его голоса", "её голоса"), ("против него", "против неё"),
        ("за ним", "за ней"), ("к нему", "к ней"), ("у него", "у неё"), ("от него", "от неё"),
    ];

    [GeneratedRegex(@"(?<![а-яёa-z])(я|мне|меня|мной|ты|тебя|тебе|тобой|он|она|вы|мы|они|кто)(?![а-яёa-z])", RegexOptions.IgnoreCase)]
    private static partial Regex Pronoun();

    [GeneratedRegex(@"([\p{L}{}_]+)(?:\s+(?:бы|же|ведь|не|уже|тоже|точно|явно|ещё|еще|так|зря|опять|снова))*\s+$")]
    private static partial Regex Adjacent();

    [GeneratedRegex(@"[^.!?\n]+[.!?\n]*")]
    private static partial Regex Sentences();

    /// <summary>Глагол прошедшего времени сразу после «ты» (через «же», «ведь», «не»…): «ты говорил», «ты ведь ошибся».</summary>
    [GeneratedRegex(@"(?<![а-яё])(ты(?:\s+(?:же|ведь|тоже|не|уже|бы|явно|точно|сам|сама|правда|опять|снова|зря|так))*\s+)([а-яё]{3,}?)(л|ла|лся|лась)(?![а-яё])", RegexOptions.IgnoreCase)]
    private static partial Regex YouPast();

    /// <summary>Род говорящего: женский бот говорит «проверяла», «согласна»; «ты прав», «Призрак открыл», «Аня права» не трогаем.</summary>
    public static string Speaker(string text, bool female) => Convert(text, female, Subject.Speaker, Pairs);

    /// <summary>Обращение к собеседнику в его роде: «ты прав» — «ты права», «дорогой» — «дорогая».</summary>
    public static string Addressee(string text, bool female)
    {
        text = Convert(text, female, Subject.Addressee, [.. Pairs, .. OthersPairs]);
        text = YouPast().Replace(text, m =>
        {
            var stem = m.Groups[2].Value;
            var end = m.Groups[3].Value.ToLowerInvariant();
            var reflexive = end.EndsWith("сь", StringComparison.Ordinal) || end.EndsWith("ся", StringComparison.Ordinal);
            // «ошибся»: основа без «л» — «ошибла» нет, только «ошиблась»; такие слова — в списке пар.
            var word = female ? (reflexive ? "лась" : "ла") : (reflexive ? "лся" : "л");
            return m.Groups[1].Value + stem + Case(m.Groups[3].Value, word);
        });
        foreach (var (male, fem) in Vocatives.OrderByDescending(v => v.Male.Length))
            text = Swap(text, female ? male : fem, female ? fem : male);
        return text;
    }

    /// <summary>О названном игроке в его роде: «Аня ошибся» — «Аня ошиблась», «его версия» — «её версия».</summary>
    public static string Named(string text, string name, bool female)
    {
        if (name.Length < 2) return text;
        foreach (var (male, fem) in Pairs.Concat(OthersPairs).OrderByDescending(p => p.Male.Length))
        {
            var from = female ? male : fem;
            var to = female ? fem : male;
            text = Regex.Replace(text, $@"(?<![\p{{L}}]){Regex.Escape(name)}((?:\s+(?:же|ведь|тоже|не|уже|явно|точно|опять|снова|зря|так))*\s+){Regex.Escape(from)}(?![а-яё])",
                m => m.Value[..^from.Length] + Case(m.Value[^from.Length..], to), RegexOptions.IgnoreCase);
        }
        if (Regex.IsMatch(text, $@"(?<![\p{{L}}]){Regex.Escape(name)}(?![\p{{L}}])", RegexOptions.IgnoreCase))
            foreach (var (male, fem) in ThirdPerson)
                text = Swap(text, female ? male : fem, female ? fem : male);
        return text;
    }

    /// <summary>В фразе есть слова, род которых зависит от собеседника («ты прав», «дорогой мой») — без знания рода её не сказать.</summary>
    public static bool DependsOnAddressee(string text) =>
        Addressee(text, true) != text || Addressee(text, false) != text;

    /// <summary>
    /// Реплика бота целиком: обращения («Аня, ты права») и упоминания игроков («её версия») — в роде каждого.
    /// Обращение действует и на следующие предложения той же реплики, пока бот не обратится к другому.
    /// </summary>
    /// <param name="genders">Имя игрока (как в чате, без «Бот ») → женский ли род; кого нет — род неизвестен, не трогаем.</param>
    public static string People(string text, IReadOnlyDictionary<string, bool>? genders)
    {
        if (genders is null || genders.Count == 0 || string.IsNullOrEmpty(text)) return text;
        string? addressed = null;
        return Sentences().Replace(text, m =>
        {
            var sentence = m.Value;
            foreach (var (name, _) in genders.OrderByDescending(g => g.Key.Length))
            {
                var escaped = Regex.Escape(name);
                if (Regex.IsMatch(sentence, $@"(?:^\s*|[,—]\s*){escaped}\s*,|,\s*{escaped}\s*[.!?…]*\s*$", RegexOptions.IgnoreCase))
                {
                    addressed = name;
                    break;
                }
            }
            if (addressed is not null && genders.TryGetValue(addressed, out var female)) sentence = Addressee(sentence, female);
            foreach (var (name, isFemale) in genders)
                if (name != addressed) sentence = Named(sentence, name, isFemale);
            return sentence;
        });
    }

    private enum Subject { Speaker, Addressee }

    private static string Convert(string text, bool female, Subject subject, IEnumerable<(string Male, string Female)> pairs)
    {
        // Длинные пары раньше коротких: «бы сказал» раньше «сказал», «неправ» раньше «прав».
        foreach (var (male, fem) in pairs.OrderByDescending(p => p.Male.Length))
        {
            var from = female ? male : fem;
            var to = female ? fem : male;
            if (from == to) continue;
            text = Regex.Replace(text, $@"(?<![а-яё]){Regex.Escape(from)}(?![а-яё])", m =>
                Whose(text, m.Index) == subject ? Case(m.Value, to) : m.Value, RegexOptions.IgnoreCase);
        }
        return text;
    }

    /// <summary>Кто подлежащее у слова: ближайшее местоимение в том же предложении; имя прямо перед словом — третий.</summary>
    private static Subject? Whose(string text, int at)
    {
        var cut = at == 0 ? -1 : text.LastIndexOfAny(['.', '!', '?', '\n'], at - 1);
        var before = text[(cut + 1)..at];
        // «Аня права», «Призрак уверен», «{who} прав»: имя прямо перед формой (через пробел) — подлежащее, это не говорящий.
        // Частицы между ними не в счёт: «Холмс бы сказал», «Аня ведь права».
        var adjacent = Adjacent().Match(before);
        if (adjacent.Success)
        {
            var word = adjacent.Groups[1].Value;
            if ((char.IsUpper(word[0]) && !Pronoun().IsMatch(word)) || word is "{who}" or "{player}") return null;
        }
        var pronoun = Pronoun().Matches(before).LastOrDefault()?.Value.ToLowerInvariant();
        return pronoun switch
        {
            null or "я" or "мне" or "меня" or "мной" => Subject.Speaker,
            "ты" or "тебя" or "тебе" or "тобой" => Subject.Addressee,
            _ => null,
        };
    }

    private static string Swap(string text, string from, string to) =>
        Regex.Replace(text, $@"(?<![\p{{L}}]){Regex.Escape(from)}(?![\p{{L}}])", m => Case(m.Value, to), RegexOptions.IgnoreCase);

    private static string Case(string sample, string word) =>
        sample.Length > 0 && char.IsUpper(sample[0]) ? char.ToUpperInvariant(word[0]) + word[1..] : word;
}
