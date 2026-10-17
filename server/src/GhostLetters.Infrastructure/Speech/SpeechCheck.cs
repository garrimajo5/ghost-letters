using System.Text.RegularExpressions;
using GhostLetters.Infrastructure.Bots;

namespace GhostLetters.Infrastructure.Speech;

/// <summary>
/// Проверка реплики, которую написала ИИ-модель, против черновика бота: те же ссылки на карты, те же имена,
/// те же обвинения и заявления о роли — стол и боты читают её так же. Не прошла — бот говорит черновиком.
/// </summary>
public static partial class SpeechCheck
{
    public const int MaxLength = 600;

    public static (string? Text, string? Why) Accept(string? text, string draft, IReadOnlyCollection<string> names)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, "пустой ответ");
        var clean = Regex.Replace(text.Trim().Trim('"', '«', '»', '“', '”'), @"[ \t]+", " ").Replace("\r", "").Trim();
        clean = Regex.Replace(clean, @"^(?:реплика|ответ)\s*:\s*", "", RegexOptions.IgnoreCase);
        if (clean.Length < 4) return (null, "слишком коротко");
        var limit = Math.Min(MaxLength, Math.Max(160, draft.Length * 2 + 60));
        if (clean.Length > limit) return (null, $"длиннее {limit} символов");
        if (Foreign().IsMatch(clean)) return (null, "латиница или разметка");
        if (Meta().IsMatch(clean)) return (null, "говорит об ИИ или правилах");

        var want = Refs(draft);
        var got = Refs(clean);
        if (want.Except(got).FirstOrDefault() is { } lost) return (null, $"пропала ссылка «{lost}»");
        if (got.Except(want).FirstOrDefault() is { } extra) return (null, $"лишняя ссылка «{extra}»");

        var bare = names.Select(BotNames.Bare).Where(n => n.Length >= 2).Distinct().ToList();
        foreach (var n in bare)
        {
            var inDraft = Mentions(draft, n);
            var inText = Mentions(clean, n);
            if (inDraft && !inText) return (null, $"пропало имя «{n}»");
            if (!inDraft && inText) return (null, $"лишнее имя «{n}»");
        }

        var ids = bare.Select((n, i) => (Id: new Guid(i + 1, 0, 0, new byte[8]), Name: n)).ToDictionary(x => x.Id, x => x.Name);
        if (Accusations(draft, ids) != Accusations(clean, ids)) return (null, "обвинения не совпадают с замыслом");
        var claims = PublicRoleClaims.Read([(Guid.Empty, draft)]);
        var newClaims = PublicRoleClaims.Read([(Guid.Empty, clean)]);
        if (string.Join(",", claims.Values) != string.Join(",", newClaims.Values)) return (null, "заявление о роли не совпадает");
        if (Negative().IsMatch(draft) != Negative().IsMatch(clean)) return (null, "«исключаю» и «думаю» перепутаны");
        return (clean, null);
    }

    /// <summary>Ссылки на карты, как их читает стол: «мотив 1», «тайна 2», «Подсказка р.1 №2», «Зацепка», «вот это».</summary>
    public static IReadOnlySet<string> Refs(string text)
    {
        var lower = text.ToLowerInvariant().Replace('ё', 'е');
        var set = new HashSet<string>();
        foreach (Match m in Category().Matches(lower)) set.Add(m.Groups["c"].Value[..4] + " " + m.Groups["n"].Value);
        foreach (Match m in Hint().Matches(lower)) set.Add($"подсказка р.{m.Groups["r"].Value} №{m.Groups["n"].Value}");
        if (Clue().IsMatch(lower)) set.Add("зацепка");
        if (This().IsMatch(lower)) set.Add("вот это");
        return set;
    }

    private static bool Mentions(string text, string name) =>
        Regex.IsMatch(text, @"(?<![\p{L}\p{N}])" + Regex.Escape(name) + @"\p{L}{0,3}(?![\p{L}\p{N}])", RegexOptions.IgnoreCase);

    /// <summary>Кого и насколько обвиняет: «убийца» (1), «из чёрных» (≥ .5), «присмотрелся бы» (.4).</summary>
    private static string Accusations(string text, IReadOnlyDictionary<Guid, string> names) =>
        string.Join(",", AccusationReader.Read(Guid.Empty, text, names).GroupBy(a => a.Target)
            .Select(g => (g.Key, Level: g.Max(a => a.Strength) switch { >= 1 => 2, >= .5 => 1, _ => 0 }))
            .OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Level}"));

    [GeneratedRegex(@"(?<![\p{L}])(?<c>тайн|мотив|способ|мест)\p{L}*\s*[—–-]?\s*(?<n>\d)(?!\d)")]
    private static partial Regex Category();

    [GeneratedRegex(@"подсказк\p{L}*\s*р\.\s*(?<r>\d+)\s*№\s*(?<n>\d+)")]
    private static partial Regex Hint();

    [GeneratedRegex(@"(?<![\p{L}])зацепк\p{L}*")]
    private static partial Regex Clue();

    [GeneratedRegex(@"(?<![\p{L}])вот\s+(?:это|эту|эта|этот)(?![\p{L}])")]
    private static partial Regex This();

    [GeneratedRegex(@"[A-Za-z*#_`\[\]<>{}]|(?!№)\p{So}|\p{Cs}")]
    private static partial Regex Foreign();

    [GeneratedRegex(@"(?<![\p{L}])(?:ии|нейросет\p{L}*|языков\p{L}* модел\p{L}*|я\s+модель|как\s+бот)(?![\p{L}])", RegexOptions.IgnoreCase)]
    private static partial Regex Meta();

    [GeneratedRegex(@"исключ|отпада|выч[её]рк|не\s+верю\s+в", RegexOptions.IgnoreCase)]
    private static partial Regex Negative();
}
