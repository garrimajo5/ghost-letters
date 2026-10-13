using System.Text.RegularExpressions;

namespace GhostLetters.Infrastructure.Speech;

/// <summary>
/// Реплика бота в чате — как написал бы человек. Бот рассуждает шаблоном («Проверял: место 1 — по смыслу: золото»,
/// «Версия: По-моему, Место: 4. Кто против?»), а в чат уходит живой текст: короче, разными словами, в манере бота,
/// иногда — фразой, выученной у людей. Ссылки на карты («место 4», «Подсказка р.1 №2», «Зацепка», «вот эту») остаются —
/// клиент показывает их картинками; карты и подписи к ним (что думает бот) не меняются.
/// Слова-подозрения («подозреваю», «из чёрных», «Убийца») остаются: по ним боты читают обвинения.
/// </summary>
public static partial class ChatStyle
{
    private const string Ref = @"(?:мотив|место|способ|тайн[ау])(?:\s*[,—:]\s*(?:карт[ау]\s*)?|\s+)\d+";

    [GeneratedRegex(Ref, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CardRef();

    [GeneratedRegex(@"(?<cat>мотив|место|способ|тайн[ау])(?:\s*[,—:]\s*(?:карт[ау]\s*)?|\s+)(?<n>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RefParts();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[А-ЯЁA-Z«])")]
    private static partial Regex SentenceBreak();

    /// <summary>Доля реплик, которые бот говорит выученной у людей фразой (если подходит).</summary>
    public const double LearnedShare = .35;

    private sealed record Ctx(Random Rng, SpokenStyle.Manner Manner, double Risk,
        IReadOnlyList<(string Intent, string Emotion, string Template)> Learned, IReadOnlyCollection<string>? Emotions,
        IReadOnlyCollection<string> Earlier)
    {
        /// <summary>Это обвинение бот уже говорил раньше — человек не повторяет его в каждой реплике.</summary>
        public bool Repeats(string who)
        {
            var bare = who.StartsWith("Бот ", StringComparison.Ordinal) ? who[4..] : who;
            return Earlier.Any(e => e.Contains(bare, StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(e, "подозр|ч[её]рн|убийц|присмотр", RegexOptions.IgnoreCase)) && Rng.NextDouble() < .8;
        }

        /// <summary>Сколько доводов «подсказка → карта» уже сказано в реплике.</summary>
        public int Links { get; set; }

        public string Pick(params string[] options) => options[Rng.Next(options.Length)];

        public string? LearnedFor(string intent, string? card, string? player)
        {
            if (Learned.Count == 0 || Rng.NextDouble() >= LearnedShare) return null;
            var options = Learned.Where(l => l.Intent == intent && (Emotions is null || Emotions.Contains(l.Emotion))).ToList();
            if (options.Count == 0) return null;
            var said = SpokenStyle.Masculine(PhraseBook.Fill(options[Rng.Next(options.Count)].Template, card, player));
            return said.Length == 0 ? null : said;
        }
    }

    /// <param name="learned">Фразы людей той же манеры (мысль, эмоция, шаблон с {card}/{player}).</param>
    /// <param name="emotions">Эмоции, которые подходят боту.</param>
    /// <param name="earlier">Прежние реплики бота в этой партии: уже сказанные обвинения он не повторяет.</param>
    public static string Humanize(string? text, Random rng, SpokenStyle.Manner manner, bool male, double risk = .5,
        IReadOnlyList<(string Intent, string Emotion, string Template)>? learned = null, IReadOnlyCollection<string>? emotions = null,
        IReadOnlyCollection<string>? earlier = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? "";
        var ctx = new Ctx(rng, manner, risk, learned ?? [], emotions, earlier ?? []);
        var result = "";
        var afterList = false;
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        // Люди не всегда начинают с письма: иногда сначала версия, потом — что кидал.
        var claim = lines.FindIndex(l => l.StartsWith("Отправлял вот", StringComparison.Ordinal) || l.StartsWith("Моё письмо — вот это", StringComparison.Ordinal));
        var version = lines.FindIndex(l => l.Contains("Версия: ", StringComparison.Ordinal));
        var reordered = claim == 0 && version > 0 && rng.NextDouble() < .25;
        if (reordered)
        {
            var moved = lines[version];
            lines.RemoveAt(version);
            lines.Insert(0, moved);
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var said = Regex.Replace(Line(lines[i], ctx), @"[ \t]+", " ").Replace(" .", ".").Replace("..", ".").Trim();
            if (said.Length == 0) continue;
            if (reordered && i == 1 && !said.StartsWith("Моё", StringComparison.Ordinal)) said = "А " + char.ToLowerInvariant(said[0]) + said[1..];
            // Список по рядам («• мотив 2 — …») остаётся списком, остальное — одним абзацем, как пишут люди.
            var item = said.StartsWith('•');
            result += result.Length == 0 ? said : item || afterList ? "\n" + said : " " + said;
            afterList = item;
        }

        // Предложение после точки — с большой буквы («… тайна 2. Связь …»).
        result = Regex.Replace(result, @"(?<=[.!?] )[а-яё]", m => m.Value.ToUpperInvariant());
        if (result.Length == 0) return text;
        // В чате люди пишут «Ватсон», а не «Бот Ватсон».
        result = Regex.Replace(result, @"(?<![а-яёА-ЯЁ])Бот (?=[А-ЯЁ])", "");
        result = char.ToUpperInvariant(result[0]) + result[1..];
        return male ? result : SpokenStyle.Feminine(result);
    }

    private static string Line(string line, Ctx c)
    {
        line = Common(line, c);
        if (line.StartsWith("• ", StringComparison.Ordinal)) return line;
        // Версия с откликом на чужое мнение, подозрением и оговоркой в одной строке — разбираем по частям.
        if (line.StartsWith("Версия: ", StringComparison.Ordinal) || Regex.IsMatch(line, @"^[^,.]{2,24}, (?:согласен|тут не соглашусь)\. Версия: "))
            return Version(line, c);

        var sentences = SentenceBreak().Split(line);
        return string.Join(" ", sentences.Select(s => Sentence(s.Trim(), c)).Where(s => s.Length > 0));
    }

    private static string Sentence(string s, Ctx c)
    {
        var bare = s.TrimEnd('.', ' ');
        Match m;
        // Своё письмо: «вот эту» — клиент покажет карту письма.
        if (bare == "Отправлял вот эту — исчезла")
            return c.LearnedFor(PhraseBook.Intents.ClaimLost, "вот это", null) ?? c.Manner switch
            {
                SpokenStyle.Manner.Lively => c.Pick("Моё вот это — пропало!", "Эх, кидал вот эту — не открыли.", "Я кидал вот эту, и она исчезла."),
                SpokenStyle.Manner.Formal => c.Pick("Моё письмо — вот это; Призрак его не открыл.", "Я отправлял вот эту, письмо исчезло."),
                _ => c.Pick("Кидал вот эту — исчезла.", "Моё письмо — вот это, не открылось.", "Я отправлял вот эту, она пропала."),
            };
        if (bare == "Моё письмо — вот это, оно открылось")
            return c.LearnedFor(PhraseBook.Intents.ClaimOpen, "вот это", null) ?? c.Manner switch
            {
                SpokenStyle.Manner.Lively => c.Pick("Моё вот это — открылось!", "Кидал вот это — и его открыли!"),
                SpokenStyle.Manner.Formal => c.Pick("Моё письмо — вот это; Призрак его открыл.", "Я отправлял вот это, письмо открыто."),
                _ => c.Pick("Моё письмо — вот это, открылось.", "Кидал вот это — открыли.", "Вот это моё, Призрак его открыл."),
            };
        if (bare == "Отправил вот эту, ждём Призрака")
            return c.Pick("Кинул вот эту, посмотрим.", "Моё — вот это. Ждём Призрака.", "Отправил вот эту, ждём.");
        if ((m = Regex.Match(bare, $@"^Проверял: (?<list>.+?) — (?<why>(?:по смыслу|по форме|по цвету|по деталям).+)$")).Success)
        {
            var list = Acc(m.Groups["list"].Value);
            var why = Reason(m.Groups["why"].Value, c);
            return c.Manner switch
            {
                SpokenStyle.Manner.Formal => c.Pick($"Письмом проверял {list}: {why}.", $"Целился в {list} — {why}."),
                SpokenStyle.Manner.Lively => c.Pick($"Целился в {list} — {why}!", $"Думал про {list}: {why}.", $"Хотел проверить {list} — {why}."),
                _ => c.Pick($"Проверял {list} — {why}.", $"Целился в {list}: {why}.", $"Думал про {list}, {why}."),
            };
        }
        if (bare == "Проверял: явной связи с полем нет")
            return c.Pick("Кидал почти наугад, честно.", "С полем оно особо не связано.", "Связи с полем у него почти нет.");
        if (bare is "Раз исчезло — это ослабляет эти версии, но не исключает: другие улики могут перевесить"
            or "Раз Призрак его не открыл, эти карты под сомнением, но не вычеркнуты"
            or "Исчезло — значит, скорее не они" or "Но одной проверки мало")
        {
            if (bare == "Но одной проверки мало") return "";
            return c.Manner switch
            {
                SpokenStyle.Manner.Lively => c.Pick("Значит, скорее мимо. Но это не точно!", "Минус этим картам, хотя одной проверки мало."),
                SpokenStyle.Manner.Formal => c.Pick("Это ослабляет проверенное, но не исключает.", "Под сомнением, но не вычеркнуто."),
                _ => c.Pick("Значит, скорее мимо, но одной проверки мало.", "Под вопросом, но вычёркивать рано.", "Слабее, но не приговор."),
            };
        }
        if (bare == "Письмо исчезло, но я не считаю это исключением карт")
            return c.Pick("Но я из этого выводов не делаю.", "Исчезло — ну и ладно, это ещё ничего не значит.");
        if (bare is "Письмо исчезло, но явных связей с картами поля я не вижу" or "Письмо открыто, но явных связей с картами поля я не вижу")
            return "";
        if (bare == "Открытие письма усиливает эти связи")
            return c.Manner == SpokenStyle.Manner.Lively ? c.Pick("Тепло!", "Значит, я на верном пути!") : c.Pick("Значит, я на верном пути.", "Это в плюс этим картам.");
        if (bare == "Пока жду результат проверки") return "";
        if ((m = Regex.Match(bare, @"^(?<hint>Подсказка р\.\d+ №\d+|Зацепка) → (?<to>" + Ref + @"): (?<why>.+)$", RegexOptions.IgnoreCase)).Success)
        {
            var why = Reason(m.Groups["why"].Value, c);
            // Второй довод подряд человек обычно не пишет.
            if (c.Links++ > 0 && c.Rng.NextDouble() < .7) return "";
            var hint = m.Groups["hint"].Value;
            var to = m.Groups["to"].Value;
            return c.Pick($"{hint} намекает на {Acc(to)}: {why}.",
                $"{hint} и {to} — {why}.",
                $"Смотрите: {(hint == "Зацепка" ? "зацепка" : hint)} — {why}, как и {to}.");
        }
        if ((m = Regex.Match(bare, @"^Целиком думаю так: (?<list>.+)$")).Success)
            return c.Rng.NextDouble() < .4 ? "" : c.Pick($"В целом у меня так: {m.Groups["list"].Value}.", $"Итого: {m.Groups["list"].Value}.", $"Целиком: {m.Groups["list"].Value}.");
        if ((m = Regex.Match(bare, @"^(?:Подозреваю, что (?<who>.+?) из чёрных|Думаю, Убийца — (?<who>.+?)|Мне кажется, Убийца — (?<who>.+?)|Я бы присмотрелся к (?<who>.+?)|Подозреваю (?<who>.+?): уже бывал Убийцей)(?:: .+)?$")).Success
            && c.Repeats(m.Groups["who"].Value))
            return "";
        if ((m = Regex.Match(bare, @"^Подозреваю, что (?<who>.+?) из чёрных(?:: (?<why>.+))?$")).Success)
        {
            var who = m.Groups["who"].Value;
            var why = m.Groups["why"].Success ? Behaviour(m.Groups["why"].Value) : null;
            return c.LearnedFor(PhraseBook.Intents.Accuse, null, who) is { } learned && Regex.IsMatch(learned, "подозр|ч[её]рн|убийц|присмотр", RegexOptions.IgnoreCase)
                ? learned
                : c.Pick($"Подозреваю, что {who} из чёрных{(why is null ? "" : ": " + why)}.",
                    $"{who}, по-моему, из чёрных{(why is null ? "" : " — " + why)}.",
                    $"Присмотритесь к игроку {who}: похоже, из чёрных{(why is null ? "" : " — " + why)}.");
        }
        if ((m = Regex.Match(bare, @"^Думаю, Убийца — (?<who>.+?): (?<why>.+)$")).Success)
            return c.Pick($"Думаю, Убийца — {m.Groups["who"].Value}: {Behaviour(m.Groups["why"].Value)}.",
                $"Ставлю на то, что Убийца — {m.Groups["who"].Value}. {Capital(Behaviour(m.Groups["why"].Value))}.");
        if ((m = Regex.Match(bare, @"^Подозреваю (?<who>.+?): уже бывал Убийцей$")).Success)
            return c.Pick($"{m.Groups["who"].Value} уже бывал Убийцей — подозреваю.", $"Подозреваю {m.Groups["who"].Value}: уже бывал Убийцей.");
        return s;
    }

    /// <summary>Обороты, которые бот повторяет в каждой строке, — разными словами.</summary>
    private static string Common(string line, Ctx c)
    {
        line = line.Replace("связь с открытыми уликами слабая, пока предположение", c.Pick("улик под неё мало, пока чутьё", "пока скорее наугад", "прямых улик нет, догадка"));
        line = Regex.Replace(line, @"(?<hint>Подсказка р\.\d+ №\d+|Зацепка): (?<why>по (?:смыслу|форме|цвету|деталям): [^.;]+(?:; [^.]+)?)",
            m => $"{m.Groups["hint"].Value} — {Reason(m.Groups["why"].Value, c)}");
        line = Regex.Replace(line, @"Пересмотрел выбор: (?<from>" + Ref + @") → (?<to>" + Ref + @")\. ",
            m => c.Pick($"Передумал: не {m.Groups["from"].Value}, а {m.Groups["to"].Value}. ", $"Пересмотрел: {m.Groups["to"].Value} вместо {m.Groups["from"].Value}. "), RegexOptions.IgnoreCase);
        line = line.Replace("Учёл и свои исчезнувшие письма: их вес зависит от того, насколько я доверяю такой проверке.",
            c.Pick("Свои исчезнувшие письма тоже учёл.", "Пропавшие письма учёл — в меру."));
        line = line.Replace("Исчезновение своих писем не считаю доводом против карт: смотрю на другие улики.",
            c.Pick("Свои пропавшие письма в расчёт не беру.", "Что мои письма исчезли — для меня не довод."));
        line = line.Replace(" Остальные ряды моей версии — на прикреплённых картах.", " " + c.Pick("Остальное — на картах.", "Остальные ряды — на картах ниже."));
        // Отклик союзнику: «твоя версия «…» сходится с подсказками; пока считаю тебя мирным».
        line = Regex.Replace(line, @"^(?<who>[^,]{2,24}), твоя версия «(?<card>[^»]+)» сходится с подсказками; пока считаю тебя мирным\. ",
            m => c.Pick($"{m.Groups["who"].Value}, с «{m.Groups["card"].Value}» согласен — сходится. ", $"{m.Groups["who"].Value}, «{m.Groups["card"].Value}» — да, похоже. Тебе пока верю. ",
                $"{m.Groups["who"].Value}, поддержу «{m.Groups["card"].Value}». "));
        line = line.Replace(" Какие ещё улики подтверждают нашу версию?", " " + c.Pick("Что ещё за неё?", "Какие ещё улики за неё?"));
        line = line.Replace(" По игрокам пока недостаточно оснований для уверенного обвинения.", " " + c.Pick("Кого подозревать — пока не знаю.", "По игрокам пока молчу.", "Подозреваемых пока не назову."));
        if (line.StartsWith("По игрокам пока недостаточно оснований для уверенного обвинения", StringComparison.Ordinal))
            line = c.Pick("Кого подозревать — пока не знаю.", "По игрокам пока молчу.");
        return line;
    }

    /// <summary>«Аня, согласен. Версия: По-моему, Место: 4. Кто против? Подозреваю, что …» — по-человечески.</summary>
    private static string Version(string line, Ctx c)
    {
        var rest = line;
        string? who = null;
        var agree = false;
        var lead = Regex.Match(rest, @"^(?<who>[^,.]{2,24}), (?<how>согласен|тут не соглашусь)\. ");
        if (lead.Success)
        {
            who = lead.Groups["who"].Value;
            agree = lead.Groups["how"].Value == "согласен";
            rest = rest[lead.Length..];
        }

        rest = rest["Версия: ".Length..];
        // Хвост: оговорка о слабой связи и обвинения — отдельными предложениями после версии.
        var tail = new List<string>();
        const string weak = " Связь с открытыми уликами слабая — это только предположение.";
        var weakLink = rest.Contains(weak, StringComparison.Ordinal);
        rest = rest.Replace(weak, "");
        var accusation = Regex.Match(rest, @"\s(?<acc>(?:Подозреваю|Думаю, Убийца|Мне кажется, Убийца|Я бы присмотрелся).*)$");
        if (accusation.Success)
        {
            tail.AddRange(SentenceBreak().Split(accusation.Groups["acc"].Value.Trim()).Select(s => Sentence(s.Trim(), c)));
            rest = rest[..accusation.Index];
        }

        var doubt = rest.StartsWith("Не уверен, но ", StringComparison.Ordinal);
        if (doubt) rest = rest["Не уверен, но ".Length..];
        var sure = rest.Contains(" — уверен", StringComparison.Ordinal);
        rest = rest.Replace(" — уверен", "");
        var refMatch = RefParts().Match(rest);
        var said = new List<string>();
        if (who is not null)
            said.Add(agree
                ? c.LearnedFor(PhraseBook.Intents.Agree, null, who) ?? c.Pick($"{who}, согласен.", $"{who}, поддержу.", $"{who}, да, я тоже так думаю.")
                : c.LearnedFor(PhraseBook.Intents.Disagree, null, who) ?? c.Pick($"{who}, тут не соглашусь.", $"{who}, а я не согласен.", $"{who}, спорно."));
        if (!refMatch.Success)
        {
            said.Add(rest.Trim());
        }
        else
        {
            var r = Nom(refMatch.Groups["cat"].Value) + " " + refMatch.Groups["n"].Value;
            var ra = Acc(r);
            var exclude = Regex.IsMatch(rest, @"не подходит|Исключаю|Не верю в", RegexOptions.IgnoreCase);
            var tentative = !exclude && Regex.IsMatch(rest, @"Пока не ясно|Есть идея", RegexOptions.IgnoreCase);
            said.Add(exclude
                ? c.Pick($"{Capital(r)} — мимо, по-моему.", $"Не верю в {ra}.", $"{Capital(r)} отпадает: улики притянуты.", $"Исключаю {ra}.")
                : tentative
                    ? c.Pick($"Я бы проверил {ra}.", $"А что если {r}?", $"Есть мысль про {ra}, но пока сыро.")
                    : doubt
                        ? c.LearnedFor(PhraseBook.Intents.Version, r, null) ?? c.Pick($"Может, {r}?", $"Не уверен, но, кажется, {r}.", $"Склоняюсь: {r}, но не точно.")
                        : c.LearnedFor(PhraseBook.Intents.Version, r, null) ?? Confident(r, sure, c));
        }

        if (weakLink && !doubt)
            said.Add(c.Pick("Но это так, догадка.", "Пока больше чутьё.", "Улик под это мало, конечно."));
        said.AddRange(tail.Where(t => t.Length > 0));
        return string.Join(" ", said);
    }

    private static string Confident(string r, bool sure, Ctx c)
    {
        if (sure)
            return c.Manner == SpokenStyle.Manner.Lively
                ? c.Pick($"{Capital(r)} — точно оно!", $"Уверен: {r}!")
                : c.Pick($"Уверен: {r}.", $"Здесь {r}, без вариантов.");
        return c.Manner switch
        {
            SpokenStyle.Manner.Lively => c.Pick($"Ставлю на {Acc(r)}!", $"Я за {Acc(r)}. Кто против?", $"По-моему, {r} — оно."),
            SpokenStyle.Manner.Formal => c.Pick($"Полагаю, {r}.", $"Предлагаю {Acc(r)}.", $"Наиболее вероятно — {r}."),
            _ => c.Pick($"Ставлю на {Acc(r)}.", $"Думаю, {r}.", $"Мне больше всего нравится {r}.", $"По-моему, {r}. Кто против?"),
        };
    }

    /// <summary>«по смыслу: море; по форме: предмет расположен по диагонали» → «там тоже море» (без тире и двоеточий — их ставит фраза вокруг).</summary>
    private static string Reason(string why, Ctx c)
    {
        var first = why.Split(';')[0].Trim();
        var m = Regex.Match(first, @"^по (?<kind>смыслу|форме|цвету|деталям): (?<what>.+)$");
        if (!m.Success) return first;
        var what = m.Groups["what"].Value.Trim();
        return m.Groups["kind"].Value switch
        {
            "смыслу" => what == "общий предмет или тема" ? "общая тема" : c.Pick($"там тоже {what}", $"в обеих есть {what}", $"{what} и там, и там"),
            "форме" => what == "похожий силуэт" ? "похожи формой" : c.Pick($"похожи формой ({what})", $"форма похожа ({what})"),
            "цвету" => what == "выраженный цветовой акцент" ? "похожи цветом" : c.Pick($"похожи цветом ({what})", $"цвет тот же ({what})"),
            _ => c.Pick($"общая деталь ({what})", $"совпадает деталь ({what})"),
        };
    }

    private static string Behaviour(string why) => why switch
    {
        "его версия хуже объясняет открытые подсказки, чем соседние карты" => "его версия плохо сходится с подсказками",
        "его голоса расходятся с моим чтением подсказок" => "голосует мимо подсказок",
        "пока опираюсь на подозрения стола и прошлые встречи, прямой улики нет" => "пока просто чутьё",
        _ => why,
    };

    /// <summary>«тайна 2» после «в», «на», «про», «за» — «тайну 2»; остальные ряды не склоняются.</summary>
    private static string Acc(string refs) => Regex.Replace(refs, @"(?<![а-яё])тайна(?=\s*\d)", "тайну", RegexOptions.IgnoreCase);

    private static string Nom(string category) => category.ToLowerInvariant().StartsWith("тайн", StringComparison.Ordinal) ? "тайна" : category.ToLowerInvariant();

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Ссылки на карты в тексте — для проверки, что пересказ ничего не потерял.</summary>
    public static IReadOnlyList<string> Refs(string text) =>
        CardRef().Matches(text).Select(m => RefParts().Match(m.Value) is var p ? Nom(p.Groups["cat"].Value) + " " + p.Groups["n"].Value : m.Value).ToList();
}
