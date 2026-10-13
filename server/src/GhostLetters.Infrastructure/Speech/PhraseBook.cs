using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Infrastructure.Speech;

/// <summary>Фраза человека, разобранная в шаблон: мысль, эмоция, манера и текст с местами для карты и игрока.</summary>
public sealed record LearnedPhrase(string Intent, string Emotion, SpokenStyle.Manner Manner, string Template, string Hash);

/// <summary>
/// Боты учатся у людей строить фразы. Из реплики человека выделяется <b>мысль</b> (что он делает: называет своё письмо,
/// выдвигает версию, исключает карту, обвиняет, соглашается, спорит, спрашивает), <b>эмоция</b> (уверенно, с сомнением,
/// увлечённо, в шутку, спокойно) и <b>манера</b> (спокойная, живая, официальная). Карты и имена заменяются местами
/// {card} и {player} — получается шаблон, которым потом говорят боты с той же манерой.
/// </summary>
public static partial class PhraseBook
{
    public static class Intents
    {
        public const string Claim = "claim";
        public const string ClaimLost = "claim_lost";
        public const string ClaimOpen = "claim_open";
        public const string Version = "version";
        public const string Exclude = "exclude";
        public const string Accuse = "accuse";
        public const string Agree = "agree";
        public const string Disagree = "disagree";
    }

    /// <summary>Какие места обязаны быть в шаблоне мысли (иначе бот не скажет главное).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Slots = new Dictionary<string, string[]>
    {
        [Intents.Claim] = ["{card}"], [Intents.ClaimLost] = ["{card}"], [Intents.ClaimOpen] = ["{card}"], [Intents.Version] = ["{card}"], [Intents.Exclude] = ["{card}"],
        [Intents.Accuse] = ["{player}"], [Intents.Agree] = ["{player}"], [Intents.Disagree] = ["{player}"],
    };

    [GeneratedRegex(@"(?<![а-яё])(мотив|место|способ|тайн[ау])(?:\s*[,—:-]\s*(?:карт[ау]\s*)?|\s+)(\d+|один|два|три|четыре|пять|шесть|семь|перв\w*|втор\w*|трет\w*|четв\w*|пят\w*|шест\w*|седьм\w*)(?![а-яё\d])", RegexOptions.IgnoreCase)]
    private static partial Regex CardByNumber();

    [GeneratedRegex(@"[а-яё]+", RegexOptions.IgnoreCase)]
    private static partial Regex Words();

    /// <summary>Карта сразу после предлога — её падеж зависит от названия, шаблон не годится.</summary>
    [GeneratedRegex(@"(?<![а-яё])(?:на|в|во|за|про|под|к|ко|о|об|у|от|из|с|со|до|по|без|для|над|при)\s+(?:(?:мотив|место|способ|тайн[ау])\s*—\s*)?\{card\}", RegexOptions.IgnoreCase)]
    private static partial Regex Prepositioned();

    [GeneratedRegex(@"(?<=[.!?…])\s+")]
    private static partial Regex Sentences();

    [GeneratedRegex(@"[a-z]|https?|www\.|@|\d", RegexOptions.IgnoreCase)]
    private static partial Regex Forbidden();

    // Грубые корни — такие фразы боты не перенимают.
    private static readonly string[] Rude = ["хуй", "хуе", "хуё", "пизд", "ебат", "ебан", "ёбан", "бля", "сука", "мудак", "мраз", "долбо", "дебил", "идиот", "чмо", "шлюх", "гандон", "пидор", "пидар"];

    private static string Stem(string word) => word.Length > 4 ? word[..^2] : word;

    /// <summary>
    /// Разобрать реплику человека. Карты — те, что есть на столе (по номеру в ряду или по названию), игроки — по именам.
    /// Возвращает шаблоны для предложений, где мысль понятна и все важные места на месте.
    /// </summary>
    public static IReadOnlyList<LearnedPhrase> Learn(string? text, IReadOnlyList<BoardRow> board, IReadOnlyList<HintGroup> hints,
        IReadOnlyCollection<string> playerNames, CardTags tags)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var source = text.Trim();
        // Показ со стола собран кнопками, а не сказан человеком; расшифровка голоса — сказана.
        if (source.StartsWith("Со стола: ", StringComparison.Ordinal)) return [];
        if (source.StartsWith("Голосом: ", StringComparison.Ordinal)) source = source["Голосом: ".Length..];
        var result = new List<LearnedPhrase>();
        foreach (var raw in Sentences().Split(source))
        {
            var sentence = Regex.Replace(raw.Trim(), @"\s+", " ");
            if (sentence.Length is < 8 or > 160) continue;
            var lower = sentence.ToLowerInvariant();
            if (Rude.Any(lower.Contains)) continue;
            var template = Slot(sentence, board, hints, playerNames, tags);
            if (template is null || Forbidden().IsMatch(template.Replace("{card}", "").Replace("{player}", ""))) continue;
            // Короткие фразы — манера, а не чужие доводы: длинные несут факты, которые боту не подходят.
            if (Words().Matches(template).Count > 12) continue;
            var intent = IntentOf(template);
            if (intent is null || !Slots[intent].All(template.Contains)) continue;
            if (template.Split("{card}").Length > 2 || template.Split("{player}").Length > 2) continue;
            var emotion = EmotionOf(template);
            var manner = MannerOf(template);
            result.Add(new LearnedPhrase(intent, emotion, manner, template, Hash(intent, template)));
        }

        return result;
    }

    /// <summary>
    /// Карты → {card}, имена игроков → {player}. Боты подставляют название карты в именительном падеже, поэтому
    /// фраза, где карта названа в другом падеже («ставлю на клетку»), не годится (null): иначе выйдет «ставлю на клетка».
    /// Номер в ряду («мотив два») становится {card}; после предлога («ставлю на мотив два») фраза не годится — падеж.
    /// </summary>
    private static string? Slot(string sentence, IReadOnlyList<BoardRow> board, IReadOnlyList<HintGroup> hints,
        IReadOnlyCollection<string> playerNames, CardTags tags)
    {
        // «мотив два» → {card}: бот подставит свою карту (в чате — «тайна 2», вслух — название).
        var text = CardByNumber().Replace(sentence, "{card}");
        var oblique = false;
        var cards = board.SelectMany(r => r.Cards).Concat(hints.SelectMany(h => h.Cards)).Distinct();
        foreach (var name in cards.SelectMany(tags.NamesOf).Distinct().OrderByDescending(n => n.Length))
        {
            var parts = Words().Matches(name).Select(m => Regex.Escape(Stem(m.Value.ToLowerInvariant()))).ToList();
            if (parts.Count == 0 || parts.Any(p => p.Length < 3)) continue;
            var pattern = @"(?<![а-яё])" + string.Join(@"\s+", parts.Select(p => p + @"[а-яё]*")) + @"(?![а-яё])";
            text = Regex.Replace(text, pattern, m =>
            {
                if (!Norm(m.Value).Equals(Norm(name), StringComparison.Ordinal)) oblique = true;
                return "{card}";
            }, RegexOptions.IgnoreCase);
        }

        if (oblique || Prepositioned().IsMatch(text)) return null;

        foreach (var name in playerNames.Where(n => n.Length >= 2).OrderByDescending(n => n.Length))
        {
            var bare = name.StartsWith("Бот ", StringComparison.Ordinal) ? name[4..] : name;
            text = Regex.Replace(text, $@"(?<![а-яёa-z])(?:Бот\s+)?{Regex.Escape(bare)}[а-яё]{{0,3}}(?![а-яёa-z])", "{player}", RegexOptions.IgnoreCase);
        }

        // «{card} и {card}» — слишком сложная фраза для шаблона одной карты: оставляем одно место.
        text = Regex.Replace(text, @"\{card\}(?:\s*(?:,|и)\s*\{card\})+", "{card}");
        return text.Length > 0 ? text : null;
    }

    private static string Norm(string s) => Regex.Replace(s.ToLowerInvariant().Replace('ё', 'е'), @"\s+", " ").Trim();

    private static bool Has(string lower, params string[] words) => words.Any(w => Regex.IsMatch(lower, $@"(?<![а-яё]){w}"));

    public static string? IntentOf(string template)
    {
        var lower = template.ToLowerInvariant();
        var card = template.Contains("{card}");
        var player = template.Contains("{player}");
        if (card && Has(lower, "кидал", "отправлял", "отправил", "скинул", "моё письмо", "мое письмо", "моя карта"))
            return Has(lower, "исчез", "не открыл", "пропал", "не откры", "закрыт") ? Intents.ClaimLost
                : Has(lower, "открыл", "открылось", "открылась") ? Intents.ClaimOpen : Intents.Claim;
        if (card && (Has(lower, "исключ", "точно не", "мимо", "отпада", "вычерк") || Regex.IsMatch(lower, @"(?<![а-яё])не \{card\}"))) return Intents.Exclude;
        if (player && Has(lower, "не соглас", "не соглаш", "спорно", "сомнева", "ну нет", "не думаю")) return Intents.Disagree;
        if (player && Has(lower, "убийц", "подозрева", "чёрн", "черн", "врёт", "врет", "лжец", "темнит", "мутн")) return Intents.Accuse;
        if (player && Has(lower, "соглас", "поддерж", "точно так", "верно", "прав")) return Intents.Agree;
        if (card && Has(lower, "став", "думаю", "уверен", "голосую", "выбира", "скорее", "по-моему", "по моему", "верси", "за ", "это")) return Intents.Version;
        return null;
    }

    public static string EmotionOf(string template)
    {
        var lower = template.ToLowerInvariant();
        if (Has(lower, "ха", "хах", "лол", "шучу", "смешно")) return "joking";
        if (Has(lower, "наверное", "может", "кажется", "вроде", "сомнева", "не уверен", "пожалуй", "возможно")) return "doubtful";
        if (template.Contains('!')) return "excited";
        if (Has(lower, "точно", "уверен", "однозначно", "сто процентов", "железно", "без сомнен")) return "confident";
        return "calm";
    }

    public static SpokenStyle.Manner MannerOf(string template)
    {
        var lower = template.ToLowerInvariant();
        if (Has(lower, "полагаю", "коллеги", "итак", "позвольте", "вероятно", "предлагаю", "считаю", "следовательно")) return SpokenStyle.Manner.Formal;
        if (template.Contains('!') || Has(lower, "ну", "блин", "короче", "слушайте", "смотрите", "ого", "вау", "капец", "жесть", "кароче")) return SpokenStyle.Manner.Lively;
        return SpokenStyle.Manner.Calm;
    }

    private static string Hash(string intent, string template)
    {
        var norm = intent + "|" + Regex.Replace(template.ToLowerInvariant(), @"[^а-яё{}]+", " ").Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(norm)))[..32];
    }

    /// <summary>Эмоции, которые подходят манере бота (и его смелости).</summary>
    public static IReadOnlyCollection<string> EmotionsFor(SpokenStyle.Manner manner, double risk) => manner switch
    {
        SpokenStyle.Manner.Lively => risk >= .4 ? ["excited", "confident", "joking", "calm"] : ["excited", "joking", "doubtful", "calm"],
        SpokenStyle.Manner.Formal => risk >= .5 ? ["confident", "calm"] : ["calm", "doubtful"],
        _ => risk >= .5 ? ["calm", "confident"] : ["calm", "doubtful"],
    };

    /// <summary>Заполнить шаблон: {card} — название карты, {player} — имя (без «Бот»).</summary>
    public static string Fill(string template, string? card, string? player)
    {
        // Старые шаблоны: «мотив — {card}» — ряд мог быть другим, оставляем только карту.
        var text = Regex.Replace(template, @"(?<![а-яё])(?:мотив|место|способ|тайн[ау])\s*—\s*\{card\}", "{card}", RegexOptions.IgnoreCase);
        if (Prepositioned().IsMatch(text)) return "";
        if (card is not null) text = text.Replace("{card}", card);
        if (player is not null) text = text.Replace("{player}", player.StartsWith("Бот ", StringComparison.Ordinal) ? player[4..] : player);
        return text.Contains('{') ? "" : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
