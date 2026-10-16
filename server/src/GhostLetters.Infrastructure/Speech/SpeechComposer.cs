using System.Text.RegularExpressions;

namespace GhostLetters.Infrastructure.Speech;

/// <summary>
/// Слова для того, что бот решил сказать (<see cref="SpeechIntent"/>). На каждую мысль ищется подходящая фраза:
/// своего персонажа (Пуаро, Марпл…), выученная у людей той же манеры или общая — с учётом настроения
/// (обрадованный говорит с восклицанием, сомневающийся — «может», «кажется») и положения за столом
/// (последний раунд, все согласны, мнения расходятся). Ссылки на карты, «вот эту», подсказки и слова-обвинения
/// остаются как есть — по ним клиент показывает карты, а боты читают мнения и обвинения.
/// </summary>
public static partial class SpeechComposer
{
    /// <summary>С чем бот говорит: характер речи и что ему уже известно.</summary>
    public sealed record Voice(
        Random Rng,
        SpokenStyle.Manner Manner,
        bool Male,
        string? Persona = null,
        IReadOnlyList<(string Intent, string Emotion, string Template)>? Learned = null,
        IReadOnlyCollection<string>? Emotions = null,
        IReadOnlyCollection<string>? Earlier = null,
        IReadOnlyDictionary<string, bool>? Genders = null);

    /// <summary>Эмоции фраз (<see cref="PhraseBook.EmotionOf"/>), которые подходят настроению.</summary>
    public static IReadOnlyCollection<string> EmotionsOf(SpeechMood mood) => mood switch
    {
        SpeechMood.Excited => ["excited", "confident", "joking"],
        SpeechMood.Confident => ["confident", "calm"],
        SpeechMood.Doubtful => ["doubtful"],
        SpeechMood.Frustrated => ["doubtful", "calm"],
        SpeechMood.Suspicious => ["confident", "calm"],
        SpeechMood.Defensive => ["excited", "calm"],
        _ => ["calm"],
    };

    /// <summary>Реплика целиком. Длиннее чата — null (тогда говорит старый путь).</summary>
    public static string? Compose(SpeechIntent intent, Voice voice)
    {
        if (intent.Thoughts.Count == 0) return null;
        var rng = voice.Rng;
        var thoughts = Order(intent, rng);
        var mood = EmotionsOf(intent.Mood);
        var parts = new List<string>();
        var links = 0;
        var reasons = new HashSet<string>();
        foreach (var t in thoughts)
        {
            // Одна и та же причина дважды подряд («там тоже животные… и там животные») звучит как заевшая пластинка.
            if (t.Kind is ThoughtKind.Check or ThoughtKind.Link && t.Why is { } why0 && !reasons.Add(why0.Split(';')[0].Trim()) && t.Kind == ThoughtKind.Link) continue;
            if (t.Kind == ThoughtKind.Link && links++ > 0 && rng.NextDouble() < .7) continue;
            if (t.Kind == ThoughtKind.Whole && rng.NextDouble() < .5) continue;
            if (t.Kind is ThoughtKind.LostConclusion or ThoughtKind.OpenConclusion && rng.NextDouble() < .3) continue;
            if (t.Kind == ThoughtKind.Accuse && t.Who is { } who && Repeats(voice.Earlier, who) && rng.NextDouble() < .8) continue;
            if (Say(t, intent, voice, mood) is { Length: > 0 } said) parts.Add(said);
        }
        if (parts.Count == 0) return null;

        // Вступление — по настроению и столу, и не каждый раз: иначе оно само становится шаблоном.
        // У персонажа свои присказки — общее вступление ему реже нужно.
        if ((voice.Persona is null || rng.NextDouble() < .5) && Opener(intent, voice, parts[0]) is { } opener) parts.Insert(0, opener);
        var text = string.Join(" ", parts.Select(End));
        text = Regex.Replace(text, @"\s+", " ").Replace(" .", ".").Replace("..", ".").Trim();
        text = Regex.Replace(text, @"(?<=[.!?…] )[а-яё]", m => m.Value.ToUpperInvariant());
        text = Regex.Replace(text, @"(?<![а-яёА-ЯЁ])Бот (?=[А-ЯЁ])", "");
        text = char.ToUpperInvariant(text[0]) + text[1..];
        text = RussianGender.People(RussianGender.Speaker(text, !voice.Male), voice.Genders);
        return text.Length <= 1000 ? text : null;
    }

    /// <summary>
    /// Порядок мыслей, как у человека: защита — первой; иногда версия раньше рассказа о письме;
    /// подозревающий иногда начинает с обвинения.
    /// </summary>
    private static List<Thought> Order(SpeechIntent intent, Random rng)
    {
        var list = intent.Thoughts.ToList();
        var defense = list.Where(t => t.Kind == ThoughtKind.Defense).ToList();
        list.RemoveAll(t => t.Kind == ThoughtKind.Defense);
        var letter = list.Where(t => t.Kind is ThoughtKind.ClaimLost or ThoughtKind.ClaimOpen or ThoughtKind.ClaimWait or ThoughtKind.Check
            or ThoughtKind.CheckNothing or ThoughtKind.LostConclusion or ThoughtKind.LostIgnored or ThoughtKind.OpenConclusion).ToList();
        var accusation = list.Where(t => t.Kind == ThoughtKind.Accuse).ToList();
        var opinion = list.Except(letter).Except(accusation).ToList();
        var ordered = new List<Thought>(defense);
        if (intent.Mood == SpeechMood.Suspicious && rng.NextDouble() < .35) ordered.AddRange(accusation);
        if (letter.Count > 0 && opinion.Count > 0 && rng.NextDouble() < .25) ordered.AddRange([.. opinion, .. letter]);
        else ordered.AddRange([.. letter, .. opinion]);
        ordered.AddRange(accusation.Except(ordered));
        // «Давайте вместе против него» — после того, как сказал, кого подозревает.
        var against = ordered.Where(t => t.Kind == ThoughtKind.VoteAgainst).ToList();
        ordered.RemoveAll(t => t.Kind == ThoughtKind.VoteAgainst);
        ordered.AddRange(against);
        return ordered;
    }

    private static bool Repeats(IReadOnlyCollection<string>? earlier, string who) =>
        earlier?.Any(e => e.Contains(who, StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(e, "подозр|ч[её]рн|убийц|присмотр", RegexOptions.IgnoreCase)) == true;

    private static string End(string s) => s.Length == 0 || ".!?…".Contains(s[^1]) ? s : s + ".";

    /// <summary>Одна мысль словами: фраза персонажа, выученная или общая — что подходит настроению.</summary>
    private static string? Say(Thought t, SpeechIntent intent, Voice voice, IReadOnlyCollection<string> mood)
    {
        var rng = voice.Rng;
        var card = t.Card;
        var list = t.Cards is { Count: > 0 } cards ? Acc(cards.Count == 1 ? cards[0] : string.Join(", ", cards.Take(cards.Count - 1)) + " и " + cards[^1]) : null;
        var why = t.Why is null ? null : t.Kind == ThoughtKind.Accuse ? Behaviour(t.Why) : Reason(t.Why, rng);
        var whole = t.Kind == ThoughtKind.Whole && t.Cards is { Count: > 0 } w ? string.Join(", ", w) : null;
        var vars = new Dictionary<string, string?>
        {
            ["card"] = card, ["card_acc"] = card is null ? null : Acc(card), ["who"] = t.Who, ["list"] = list,
            ["why"] = why, ["hint"] = t.Hint, ["to"] = card, ["whole"] = whole,
            ["when"] = t.Round is null ? null : "в прошлом раунде",
        };
        // Давний спор — общими фразами с пометкой раунда: у персонажей таких фраз нет.
        var earlier = t.Kind is ThoughtKind.Agree or ThoughtKind.Disagree && t.Round is not null;

        var candidates = new List<(string Template, double Weight, bool Learned)>();
        if (!earlier && SituationOf(t.Kind) is { } situation)
            foreach (var line in PersonaLines.Lines(voice.Persona, situation))
                candidates.Add((line, 3 * Fit(PhraseBook.EmotionOf(line), mood), false));
        if (!earlier && IntentOf(t.Kind) is { } learnedIntent && voice.Learned is { Count: > 0 } learned)
        {
            var addresseeKnown = t.Who is null || voice.Genders?.ContainsKey(t.Who) == true;
            foreach (var l in learned.Where(l => (l.Intent == learnedIntent || (learnedIntent is PhraseBook.Intents.ClaimLost or PhraseBook.Intents.ClaimOpen && l.Intent == PhraseBook.Intents.Claim))
                         && (voice.Emotions is null || voice.Emotions.Contains(l.Emotion)) && (addresseeKnown || !RussianGender.DependsOnAddressee(l.Template))))
                candidates.Add((l.Template, 2 * Fit(l.Emotion, mood), true));
        }
        foreach (var line in Generic(t.Kind, intent.Mood, voice.Manner, earlier))
            candidates.Add((line, 1.5 * Fit(PhraseBook.EmotionOf(line), mood), false));

        // Весом — по источнику и по настроению; неподходящая (без нужной подстановки) пропускается.
        foreach (var _ in Enumerable.Range(0, 6))
        {
            if (candidates.Count == 0) return null;
            var pick = Pick(candidates, rng);
            candidates.Remove(pick);
            var said = pick.Learned ? FillLearned(pick.Template, t, card) : Fill(pick.Template, vars);
            if (said is { Length: > 0 } && Keeps(t, said)) return said;
        }
        return null;
    }

    /// <summary>Фраза годится, если в ней осталось то, по чему стол читает мысль.</summary>
    private static bool Keeps(Thought t, string said) => t.Kind switch
    {
        ThoughtKind.ClaimLost or ThoughtKind.ClaimOpen or ThoughtKind.ClaimWait => Regex.IsMatch(said, "вот эт[оу]", RegexOptions.IgnoreCase),
        ThoughtKind.Accuse => t.Who is not null && said.Contains(t.Who, StringComparison.OrdinalIgnoreCase) &&
                              Regex.IsMatch(said, "подозр|ч[её]рн|убийц|присмотр", RegexOptions.IgnoreCase),
        ThoughtKind.Defense => !Regex.IsMatch(Regex.Replace(said, @"не\s+(?:из\s+)?ч[её]рн\p{L}*|не\s+убийц\p{L}*", "", RegexOptions.IgnoreCase),
            "подозр|ч[её]рн|убийц|присмотр", RegexOptions.IgnoreCase),
        _ => t.Card is null || said.Contains(t.Card, StringComparison.OrdinalIgnoreCase) || (Acc(t.Card) is var a && said.Contains(a, StringComparison.OrdinalIgnoreCase)),
    };

    private static double Fit(string emotion, IReadOnlyCollection<string> mood) =>
        mood.Contains(emotion) ? 2.5 : emotion == "calm" ? 1 : .3;

    private static (string Template, double Weight, bool Learned) Pick(List<(string Template, double Weight, bool Learned)> candidates, Random rng)
    {
        var total = candidates.Sum(c => c.Weight);
        var x = rng.NextDouble() * total;
        foreach (var c in candidates)
        {
            x -= c.Weight;
            if (x <= 0) return c;
        }
        return candidates[^1];
    }

    private static string? Fill(string template, IReadOnlyDictionary<string, string?> vars)
    {
        var missing = false;
        var text = Regex.Replace(template, @"\{(\w+)\}", m =>
        {
            if (vars.TryGetValue(m.Groups[1].Value, out var v) && v is { Length: > 0 }) return v;
            missing = true;
            return "";
        });
        return missing ? null : text.Trim();
    }

    private static string? FillLearned(string template, Thought t, string? card)
    {
        // У людей письмо — «вот это»: клиент покажет карту письма.
        var filled = PhraseBook.Fill(template, t.Kind is ThoughtKind.ClaimLost or ThoughtKind.ClaimOpen or ThoughtKind.ClaimWait ? "вот это" : card, t.Who);
        return filled.Length == 0 ? null : SpokenStyle.Masculine(filled);
    }

    private static string? SituationOf(ThoughtKind kind) => kind switch
    {
        ThoughtKind.Defense => PersonaLines.Situations.Defense,
        ThoughtKind.ClaimLost => PersonaLines.Situations.ClaimLost,
        ThoughtKind.ClaimOpen => PersonaLines.Situations.ClaimOpen,
        ThoughtKind.ClaimWait => PersonaLines.Situations.ClaimWait,
        ThoughtKind.Check => PersonaLines.Situations.Check,
        ThoughtKind.LostConclusion => PersonaLines.Situations.LostConclusion,
        ThoughtKind.OpenConclusion => PersonaLines.Situations.OpenConclusion,
        ThoughtKind.Agree => PersonaLines.Situations.Agree,
        ThoughtKind.Disagree => PersonaLines.Situations.Disagree,
        ThoughtKind.Version => PersonaLines.Situations.Version,
        ThoughtKind.VersionSure => PersonaLines.Situations.VersionSure,
        ThoughtKind.VersionDoubt => PersonaLines.Situations.VersionDoubt,
        ThoughtKind.Tentative => PersonaLines.Situations.Tentative,
        ThoughtKind.Exclude => PersonaLines.Situations.Exclude,
        ThoughtKind.Weak => PersonaLines.Situations.Weak,
        ThoughtKind.Link => PersonaLines.Situations.Link,
        ThoughtKind.Accuse => PersonaLines.Situations.Accuse,
        ThoughtKind.NoClues => PersonaLines.Situations.NoClues,
        _ => null,
    };

    private static string? IntentOf(ThoughtKind kind) => kind switch
    {
        ThoughtKind.ClaimLost => PhraseBook.Intents.ClaimLost,
        ThoughtKind.ClaimOpen => PhraseBook.Intents.ClaimOpen,
        ThoughtKind.Version or ThoughtKind.VersionSure or ThoughtKind.VersionDoubt => PhraseBook.Intents.Version,
        ThoughtKind.Exclude => PhraseBook.Intents.Exclude,
        ThoughtKind.Agree => PhraseBook.Intents.Agree,
        ThoughtKind.Disagree => PhraseBook.Intents.Disagree,
        ThoughtKind.Accuse => PhraseBook.Intents.Accuse,
        _ => null,
    };

    /// <summary>Общие фразы — на случай, когда у бота нет персонажа или его фразы не подошли.</summary>
    private static IEnumerable<string> Generic(ThoughtKind kind, SpeechMood mood, SpokenStyle.Manner manner, bool earlier)
    {
        var lively = manner == SpokenStyle.Manner.Lively;
        var formal = manner == SpokenStyle.Manner.Formal;
        return kind switch
        {
            ThoughtKind.Defense => ["{who}, это не я.", "{who}, зря ты так — я играю за стол.", "Я?! {who}, ты что, я мирный!", "{who}, позволь возразить: я честный сыщик."],
            ThoughtKind.ClaimLost => mood == SpeechMood.Frustrated
                ? ["Эх, опять мимо: кидал вот эту — исчезла.", "Снова пропало! Вот эту кидал.", "Ну вот, и вот эту Призрак не открыл."]
                : lively ? ["Моё вот это — пропало!", "Эх, кидал вот эту — не открыли."] : formal ? ["Я отправлял вот эту, письмо исчезло.", "Моё письмо — вот это; Призрак его не открыл."]
                : ["Кидал вот эту — исчезла.", "Моё письмо — вот это, не открылось.", "Я отправлял вот эту, она пропала."],
            ThoughtKind.ClaimOpen => mood == SpeechMood.Excited
                ? ["Есть! Моё вот это открыли!", "Вот это — моё, и Призрак его открыл!"]
                : ["Моё письмо — вот это, открылось.", "Кидал вот это — открыли.", "Вот это моё, Призрак его открыл."],
            ThoughtKind.ClaimWait => ["Кинул вот эту, посмотрим.", "Моё — вот это. Ждём Призрака.", "Отправил вот эту, ждём."],
            ThoughtKind.Check => formal ? ["Письмом проверял {list}: {why}.", "Целился в {list} — {why}."]
                : ["Проверял {list} — {why}.", "Целился в {list}: {why}.", "Думал про {list}, {why}."],
            ThoughtKind.CheckNothing => ["Кидал почти наугад, честно.", "С полем оно особо не связано."],
            ThoughtKind.LostConclusion => mood == SpeechMood.Frustrated
                ? ["Значит, опять не туда. Но вычёркивать рано.", "Ладно, минус этим картам — но не приговор."]
                : ["Значит, скорее мимо, но одной проверки мало.", "Под вопросом, но вычёркивать рано.", "Слабее, но не приговор."],
            ThoughtKind.LostIgnored => ["Но я из этого выводов не делаю.", "Исчезло — ну и ладно, это ещё ничего не значит."],
            ThoughtKind.OpenConclusion => (mood is SpeechMood.Excited or SpeechMood.Confident)
                ? ["Значит, я на верном пути!", "Тепло, очень тепло!"] : ["Значит, я на верном пути.", "Это в плюс этим картам."],
            ThoughtKind.Agree => earlier
                ? ["{who}, {when} у тебя тоже был {card} — теперь и я так думаю.", "{who}, ты ещё {when} говорил про {card_acc} — согласен."]
                : ["{who}, согласен.", "{who}, поддержу.", "{who}, да, я тоже так думаю."],
            ThoughtKind.Disagree => earlier
                ? ["{who}, {when} у тебя был {card} — тут не соглашусь.", "{who}, помню, {when} ты ставил на {card_acc}, но я не согласен."]
                : mood == SpeechMood.Suspicious ? ["{who}, нет, тут ты меня не убедишь.", "{who}, а вот тут не соглашусь."]
                : ["{who}, тут не соглашусь.", "{who}, а я не согласен.", "{who}, спорно."],
            ThoughtKind.Version => lively ? ["Ставлю на {card_acc}!", "Я за {card_acc}. Кто против?", "По-моему, {card} — оно."]
                : formal ? ["Полагаю, {card}.", "Предлагаю {card_acc}.", "Наиболее вероятно — {card}."]
                : ["Ставлю на {card_acc}.", "Думаю, {card}.", "Мне больше всего нравится {card}.", "По-моему, {card}. Кто против?"],
            ThoughtKind.VersionSure => lively ? ["{card} — точно оно!", "Уверен: {card}!"] : ["Уверен: {card}.", "Здесь {card}, без вариантов."],
            ThoughtKind.VersionDoubt => ["Может, {card}?", "Не уверен, но, кажется, {card}.", "Склоняюсь: {card}, но не точно."],
            ThoughtKind.Tentative => ["Я бы проверил {card_acc}.", "А что если {card}?", "Есть мысль про {card_acc}, но пока сыро."],
            ThoughtKind.Exclude => ["{card} — мимо, по-моему.", "Не верю в {card_acc}.", "{card} отпадает: улики притянуты.", "Исключаю {card_acc}."],
            ThoughtKind.Weak => ["Но это так, догадка.", "Пока больше чутьё.", "Улик под это мало, конечно."],
            ThoughtKind.Link => ["{hint} намекает на {to}: {why}.", "{hint} и {to} — {why}.", "Смотрите: {hint} — {why}, как и {to}."],
            ThoughtKind.Whole => ["В целом у меня так: {whole}.", "Целиком: {whole}."],
            ThoughtKind.Accuse => mood == SpeechMood.Suspicious
                ? ["Я подозреваю, что {who} из чёрных — {why}.", "{who}, по-моему, из чёрных: {why}.", "Присмотритесь к игроку {who}: похоже, из чёрных."]
                : ["Подозреваю, что {who} из чёрных.", "{who}, по-моему, из чёрных.", "Присмотритесь к игроку {who}: похоже, из чёрных."],
            ThoughtKind.NoClues => ["Улик пока мало — версию не строю, жду подсказок.", "Пока гадать рано: подсказок почти нет.", "Подожду улик, наугад называть не буду."],
            ThoughtKind.Trust => ["{who}, тебе я пока верю.", "Похоже, {who} играет за стол.", "{who}, с тобой мы, кажется, заодно."],
            ThoughtKind.Support => ["Давайте поддержим {card_acc}.", "Предлагаю вместе за {card_acc}.", "Я бы всем столом взял {card_acc}."],
            ThoughtKind.VoteAgainst => ["Давайте вместе голосовать против игрока {who}.", "Предлагаю голосовать против игрока {who}."],
            _ => [],
        };
    }

    /// <summary>Вступление — по настроению и положению за столом; null — без вступления.</summary>
    private static string? Opener(SpeechIntent intent, Voice voice, string first)
    {
        var rng = voice.Rng;
        if (rng.NextDouble() >= .35 || Regex.IsMatch(first, @"^(?:ну|хм|эх|ах|ох|ой|о,|увы|так|слушайте|смотрите|итак|знаете|mon|voilà|bien|mais|т-|б-|к-)", RegexOptions.IgnoreCase)) return null;
        var s = intent.Situation;
        string[] options =
            s.LastRound && rng.NextDouble() < .5 ? ["Последний раунд — давайте соберёмся.", "Последний шанс, так что по делу."]
            : s.Consensus && rng.NextDouble() < .5 ? ["Кажется, мы сходимся.", "Похоже, стол думает одинаково."]
            : s.Contested && rng.NextDouble() < .5 ? ["Мнения расходятся, так что скажу своё.", "Тут мы явно не сошлись."]
            : intent.Mood switch
            {
                SpeechMood.Excited => ["Слушайте!", "О, смотрите!"],
                SpeechMood.Doubtful => ["Хм…", "Так…", "Даже не знаю…"],
                SpeechMood.Frustrated => ["Эх.", "Ну вот…"],
                SpeechMood.Suspicious => ["Так, внимание.", "Слушайте внимательно."],
                SpeechMood.Confident => ["Итак.", "Смотрите."],
                _ => voice.Manner == SpokenStyle.Manner.Lively ? ["Ну смотрите.", "Короче."] : ["Так.", "Смотрите."],
            };
        return options.Length == 0 ? null : options[rng.Next(options.Length)];
    }

    /// <summary>«тайна 2» после «в», «на», «про», «за» — «тайну 2».</summary>
    private static string Acc(string refs) => Regex.Replace(refs, @"(?<![а-яё])тайна(?=\s*\d)", "тайну", RegexOptions.IgnoreCase);

    /// <summary>«по смыслу: море; по форме: …» → «там тоже море».</summary>
    private static string Reason(string why, Random rng)
    {
        var first = why.Split(';')[0].Trim();
        var m = Regex.Match(first, @"^по (?<kind>смыслу|форме|цвету|деталям): (?<what>.+)$");
        if (!m.Success) return first;
        var what = m.Groups["what"].Value.Trim();
        string P(params string[] o) => o[rng.Next(o.Length)];
        return m.Groups["kind"].Value switch
        {
            "смыслу" => what == "общий предмет или тема" ? "общая тема" : P($"там тоже {what}", $"и там {what}", $"там опять {what}"),
            "форме" => what == "похожий силуэт" ? "похожи формой" : P($"похожи формой ({what})", $"форма похожа ({what})"),
            "цвету" => what == "выраженный цветовой акцент" ? "похожи цветом" : P($"похожи цветом ({what})", $"цвет тот же ({what})"),
            _ => P($"общая деталь ({what})", $"совпадает деталь ({what})"),
        };
    }

    private static string Behaviour(string why) => why switch
    {
        "его версия хуже объясняет открытые подсказки, чем соседние карты" => "его версия плохо сходится с подсказками",
        "его голоса расходятся с моим чтением подсказок" => "голосует мимо подсказок",
        "пока опираюсь на подозрения стола и прошлые встречи, прямой улики нет" => "пока просто чутьё",
        _ => why,
    };
}
