namespace GhostLetters.Infrastructure.Bots;

/// <summary>
/// Характер бота — спектры (все 0…1). Задаётся админом в кабинете ботов, на каждую партию
/// чуть «плывёт» в пределах изменчивости.
/// </summary>
public sealed record BotPersonality
{
    /// <summary>Внимание к смыслу предмета (нож → оружие).</summary>
    public double Meaning { get; init; } = 0.5;

    /// <summary>Внимание к форме (длинное, круглое…).</summary>
    public double Shape { get; init; } = 0.25;

    /// <summary>Внимание к цвету.</summary>
    public double Color { get; init; } = 0.25;

    /// <summary>Выводы из того, что НЕ открыли: 0 — «не достали и ладно», 1 — «не достали — точно не оно».</summary>
    public double Negative { get; init; } = 0.5;

    /// <summary>Память о прошлых партиях с этими людьми: 0 — только текущая игра, 1 — все завершённые партии; свежие воспоминания сильнее.</summary>
    public double Memory { get; init; } = 0.3;

    /// <summary>Риск: 0 — никогда не врёт и не выдаёт себя, 1 — блефует и обвиняет в лоб.</summary>
    public double Risk { get; init; } = 0.5;

    /// <summary>Компромисс: 0 — не слушает даже Эксперта, 1 — договаривается даже с Убийцей.</summary>
    public double Compromise { get; init; } = 0.5;

    /// <summary>
    /// Строгость ассоциаций: 0 — «одной уликой проверил и оружие, и всё, что связано с водой»,
    /// 1 — «выложил деньги — проверил только деньги, ничего лишнего».
    /// </summary>
    public double Strictness { get; init; } = 0.5;

    /// <summary>0 — только общий образ; 1 — мелкие детали важнее общего образа.</summary>
    public double Details { get; init; } = 0.25;

    /// <summary>0 — только основные смыслы (вес 1); 1 — учитывает и второстепенные, с их весами.</summary>
    public double SecondaryMeanings { get; init; } = 0.35;

    /// <summary>Изменчивость: 0 — всегда одинаковый, 1 — от партии к партии другой.</summary>
    public double Variability { get; init; } = 0.2;

    public BotSocialTraits Social { get; init; } = new();

    public static readonly BotPersonality Default = new();

    /// <summary>Доли внимания (смысл, форма, цвет) с суммой 1.</summary>
    public (double Meaning, double Shape, double Color) Attention
    {
        get
        {
            var m = Math.Max(0, Meaning);
            var s = Math.Max(0, Shape);
            var c = Math.Max(0, Color);
            var sum = m + s + c;
            if (sum <= 0) return (.8, .15, .05);
            var meaning = .7 + .3 * m / sum;
            var visual = s + c;
            return visual <= 0 ? (1, 0, 0) : (meaning, (1 - meaning) * s / visual, (1 - meaning) * c / visual);
        }
    }

    /// <summary>Все значения в пределах 0…1.</summary>
    public BotPersonality Clamped() => new()
    {
        Meaning = Clamp(Meaning),
        Shape = Clamp(Shape),
        Color = Clamp(Color),
        Negative = Clamp(Negative),
        Memory = Clamp(Memory),
        Risk = Clamp(Risk),
        Compromise = Clamp(Compromise),
        Strictness = Clamp(Strictness),
        Details = Clamp(Details),
        SecondaryMeanings = Clamp(SecondaryMeanings),
        Variability = Clamp(Variability),
        Social = Social.Clamped(),
    };

    /// <summary>Сколько карт поля проверяет одно письмо: строгий — 1, широкий — до 4 (середина — 3, как раньше).</summary>
    public int CheckBreadth => 1 + (int)Math.Round(3 * (1 - Clamp(Strictness)));

    /// <summary>
    /// Характер на конкретную партию: каждый спектр сдвигается на случайную величину до ±0.35 × изменчивость
    /// (одинаково для одной и той же партии — бот не «скачет» посреди игры).
    /// </summary>
    public BotPersonality ForGame(Guid gameId, Guid botId)
    {
        var p = Clamped();
        if (p.Variability <= 0)
        {
            return p;
        }

        var rng = new Random(HashCode.Combine(gameId, botId));
        double Drift(double v) => Clamp(v + (rng.NextDouble() * 2 - 1) * 0.35 * p.Variability);
        return p with
        {
            Meaning = Drift(p.Meaning),
            Shape = Drift(p.Shape),
            Color = Drift(p.Color),
            Negative = Drift(p.Negative),
            Memory = p.Memory is 0 or 1 ? p.Memory : Drift(p.Memory),
            Risk = Drift(p.Risk),
            Compromise = Drift(p.Compromise),
            Strictness = Drift(p.Strictness),
            Details = p.Details is 0 or 1 ? p.Details : Drift(p.Details),
            SecondaryMeanings = p.SecondaryMeanings is 0 or 1 ? p.SecondaryMeanings : Drift(p.SecondaryMeanings),
        };
    }

    private static double Clamp(double v) => double.IsNaN(v) ? 0.5 : Math.Clamp(v, 0, 1);
}

/// <summary>Что бот помнит о соигроке по прошлым партиям с ним: сколько игр и кем тот был.</summary>
public sealed record PlayerHistory(int Games, int KillerTeam, int Informed,
    double? RecentGames = null, double? RecentKillerTeam = null, double? RecentInformed = null)
{
    /// <summary>Как часто был в команде Убийцы, со сглаживанием (без игр — средняя доля ~0.25).</summary>
    public double KillerRate => ((RecentKillerTeam ?? KillerTeam) + 0.5) / ((RecentGames ?? Games) + 2.0);

    /// <summary>Как часто был Свидетелем или Экспертом.</summary>
    public double InformedRate => ((RecentInformed ?? Informed) + 0.3) / ((RecentGames ?? Games) + 2.0);
}

public sealed record RememberedPlayer(Guid GameId, Guid UserId, string Role, DateTimeOffset FinishedAt);

public static class BotMemory
{
    /// <summary>Окно памяти растёт до всей истории; каждые 20 более свежих игр вес уменьшается вдвое.</summary>
    public static IReadOnlyDictionary<Guid, PlayerHistory> Recall(IEnumerable<RememberedPlayer> past, double memory)
    {
        if (memory <= 0) return new Dictionary<Guid, PlayerHistory>();
        var games = past.GroupBy(p => p.GameId).OrderByDescending(g => g.First().FinishedAt).ThenBy(g => g.Key);
        var limit = memory >= 1 ? int.MaxValue : (int)Math.Min(int.MaxValue, 1 + 20 * memory / (1 - memory));
        var weighted = games.Take(limit).SelectMany((g, index) => g.Select(p =>
            (Player: p, Weight: Math.Max(1e-12, Math.Pow(0.5, index / 20.0))))).ToList();
        return weighted.GroupBy(x => x.Player.UserId).ToDictionary(g => g.Key, g => new PlayerHistory(
            g.Count(), g.Count(x => x.Player.Role is "Killer" or "Accomplice"),
            g.Count(x => x.Player.Role is "Witness" or "Expert"), g.Sum(x => x.Weight),
            g.Where(x => x.Player.Role is "Killer" or "Accomplice").Sum(x => x.Weight),
            g.Where(x => x.Player.Role is "Witness" or "Expert").Sum(x => x.Weight)));
    }
}

/// <summary>
/// Всё, что бот знает помимо своей проекции партии: характер, память о людях, мнения из чата, имена.
/// </summary>
public sealed record BotMind(
    BotPersonality Personality,
    IReadOnlyDictionary<Guid, PlayerHistory> History,
    IReadOnlyList<ChatOpinion> Opinions,
    IReadOnlyDictionary<Guid, string> Names,
    IReadOnlyList<Accusation>? Accusations = null,
    IReadOnlyDictionary<Guid, double>? Breadth = null,
    IReadOnlyDictionary<Guid, int>? Affinities = null,
    IReadOnlyDictionary<Guid, GhostLetters.Domain.Roles.Role>? RoleClaims = null)
{
    public double AffinityBias(Guid id) => Math.Clamp(Affinities?.GetValueOrDefault(id) ?? 0, -100, 100) / 100.0 * Personality.Social.Influence;

    public double TrustMultiplier(Guid id) => 1 + .5 * AffinityBias(id);

    /// <summary>
    /// Сколько карт поля игроки в среднем «проверяют» одним письмом — по их словам в чате («проверял эту»).
    /// Никто не рассказывал — 3. Призрак так понимает, насколько строго стол читает подсказки.
    /// </summary>
    public double TableBreadth => Breadth is { Count: > 0 } b ? b.Values.Average() : 3;

    public static readonly BotMind Neutral = new(BotPersonality.Default, new Dictionary<Guid, PlayerHistory>(), [], new Dictionary<Guid, string>());

    public IReadOnlyList<Accusation> AccusationList => Accusations ?? [];
}

/// <summary>Обвинение из чата: автор назвал игрока Убийцей (1) или «из чёрных»/подозрительным (0.6), «присмотрелся бы» — 0.4.</summary>
public sealed record Accusation(Guid Author, Guid Target, double Strength);

/// <summary>Разбор обвинений в тексте чата — и бота, и человека: имя игрока рядом со словами-подозрениями.</summary>
public static class AccusationReader
{
    private static readonly System.Text.RegularExpressions.Regex Clauses = new(
        @"[.!?\n;]+|,\s*(?:а|но|зато)\s+",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.NonBacktracking);
    private static readonly System.Text.RegularExpressions.Regex Denial = new(
        @"\bне\s+(?:считаю|думаю|полагаю|уверен|уверена|подозреваю)\b|" +
        @"\bне\s+верю\s*,?\s*что\b|" +
        @"\bне\s+(?:убийц\p{L}*|сообщник\p{L}*|подозрительн\p{L}*|(?:из\s+)?ч[её]рн\p{L}*)\b",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.NonBacktracking);

    public static IReadOnlyList<Accusation> Read(Guid author, string? text, IReadOnlyDictionary<Guid, string> names)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var found = new List<Accusation>();
        // Обращение к союзнику в предыдущем предложении не является обвинением.
        foreach (var sentence in Clauses.Split(text.ToLowerInvariant()))
        {
            double strength = sentence.Contains("убийц") ? 1
                : sentence.Contains("чёрн") || sentence.Contains("черн") || sentence.Contains("сообщник") || sentence.Contains("подозр") ? 0.6
                : sentence.Contains("присмотр") || sentence.Contains("не верю") ? 0.4 : 0;
            // Negation belongs to its clause, not to a separate accusation after "но"/"а".
            // "Не верю Бобу" is distrust, while "не верю, что Боб убийца" denies the accusation.
            if (strength == 0 || Denial.IsMatch(sentence)) continue;
            foreach (var (id, name) in names)
            {
                if (id == author) continue;
                var full = name.ToLowerInvariant();
                var bare = full.StartsWith("бот ", StringComparison.Ordinal) ? full[4..] : full;
                if (bare.Length < 2) continue;
                var pattern = @"(?<![\p{L}\p{N}])" + System.Text.RegularExpressions.Regex.Escape(bare) + @"(?![\p{L}\p{N}])";
                if (System.Text.RegularExpressions.Regex.IsMatch(sentence, pattern))
                    found.Add(new Accusation(author, id, strength));
            }
        }

        return found;
    }
}

/// <summary>Мнение из чата: автор показал карту поля с подписью («думаю, эта», «проверял эту»).</summary>
public sealed record ChatOpinion(Guid Author, string CardId, double Strength, bool IsCheck = false, string? SourceCard = null);

/// <summary>Готовые характеры — кабинет предлагает создать их одной кнопкой.</summary>
public static class BotPresets
{
    public static readonly IReadOnlyList<(string Name, string Color, string About, BotPersonality P)> All =
    [
        ("Пуаро", "#5C7C99", "Смысл прежде всего, упрям, мало врёт, письмом проверяет одну карту.",
            new BotPersonality { SecondaryMeanings = 0.15, Meaning = 0.8, Shape = 0.1, Color = 0.1, Negative = 0.7, Memory = 0.4, Risk = 0.25, Compromise = 0.2, Strictness = 0.85, Variability = 0.1 }),
        ("Марпл", "#B370D9", "Помнит всех и всё, верит людям.",
            new BotPersonality { SecondaryMeanings = 0.65, Meaning = 0.5, Shape = 0.2, Color = 0.3, Negative = 0.5, Memory = 0.9, Risk = 0.3, Compromise = 0.7, Strictness = 0.45, Variability = 0.15 }),
        ("Коломбо", "#E57F4F", "Смотрит на форму, обвиняет в лоб, рискует, видит связь во всём.",
            new BotPersonality { SecondaryMeanings = 0.85, Meaning = 0.3, Shape = 0.5, Color = 0.2, Negative = 0.4, Memory = 0.5, Risk = 0.85, Compromise = 0.4, Strictness = 0.15, Variability = 0.3 }),
        ("Ватсон", "#4AA3DF", "Слушает большинство, не делает выводов из исчезнувшего.",
            new BotPersonality { Meaning = 0.45, Shape = 0.25, Color = 0.3, Negative = 0.15, Memory = 0.2, Risk = 0.35, Compromise = 0.9, Strictness = 0.4, Variability = 0.2 }),
        ("Фандорин", "#F2A541", "Видит цвета, холодный расчёт, строг к ассоциациям, каждый раз немного другой.",
            new BotPersonality { Meaning = 0.3, Shape = 0.2, Color = 0.5, Negative = 0.85, Memory = 0.3, Risk = 0.5, Compromise = 0.3, Strictness = 0.9, Variability = 0.6 }),
        ("Жеглов", "#3D6A99", "Блефует на любой роли, никому не верит.",
            new BotPersonality { Meaning = 0.6, Shape = 0.2, Color = 0.2, Negative = 0.5, Memory = 0.7, Risk = 0.95, Compromise = 0.05, Strictness = 0.3, Variability = 0.4 }),
    ];
}
