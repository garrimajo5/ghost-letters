using System.Text.Json;
using System.Text.RegularExpressions;

namespace GhostLetters.Infrastructure.Speech;

/// <summary>
/// Реплики известных сыщиков по ситуациям (Personas/*.json): Пуаро говорит как Пуаро, Жеглов — как Жеглов.
/// Места в шаблонах: {card} (карта поля в именительном), {card_acc} (в винительном), {who} (имя игрока),
/// {list} (проверенные карты в винительном), {why} (почему похожи), {hint} (подсказка), {to} (карта, к которой она ведёт).
/// Бот без известного персонажа говорит обычными фразами.
/// </summary>
public static partial class PersonaLines
{
    public static class Situations
    {
        public const string Version = "version";
        public const string VersionSure = "version_sure";
        public const string VersionDoubt = "version_doubt";
        public const string Tentative = "tentative";
        public const string Exclude = "exclude";
        public const string Weak = "weak";
        public const string Agree = "agree";
        public const string Disagree = "disagree";
        public const string Accuse = "accuse";
        public const string Defense = "defense";
        public const string ClaimLost = "claim_lost";
        public const string ClaimOpen = "claim_open";
        public const string ClaimWait = "claim_wait";
        public const string Check = "check";
        public const string LostConclusion = "lost_conclusion";
        public const string OpenConclusion = "open_conclusion";
        public const string Link = "link";
        public const string NoClues = "no_clues";
        public const string FarewellWin = "farewell_win";
        public const string FarewellLose = "farewell_lose";
        public const string FarewellBlackWin = "farewell_black_win";
    }

    /// <summary>Доля реплик, сказанных фразой своего персонажа (если для ситуации она есть).</summary>
    public const double Share = .65;

    private static readonly IReadOnlyDictionary<string, string> Keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Пуаро"] = "poirot", ["Марпл"] = "marple", ["Коломбо"] = "columbo",
        ["Ватсон"] = "watson", ["Фандорин"] = "fandorin", ["Жеглов"] = "zheglov",
    };

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string[]>>> All = new(Load);

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string[]>> Load()
    {
        var assembly = typeof(PersonaLines).Assembly;
        var result = new Dictionary<string, IReadOnlyDictionary<string, string[]>>();
        foreach (var key in Keys.Values.Distinct())
        {
            using var stream = assembly.GetManifestResourceStream($"Personas.{key}.json");
            if (stream is null) continue;
            var lines = JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream) ?? [];
            result[key] = lines.ToDictionary(x => x.Key, x => x.Value.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray());
        }
        return result;
    }

    /// <summary>Персонаж бота по нику («Бот Пуаро» → poirot); неизвестный — null.</summary>
    public static string? KeyOf(string? nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname)) return null;
        var bare = nickname.StartsWith("Бот ", StringComparison.Ordinal) ? nickname[4..] : nickname;
        // «Бот Пуаро 2» в песочнице — тоже Пуаро.
        bare = System.Text.RegularExpressions.Regex.Replace(bare, @"\s+\d+$", "");
        return Keys.TryGetValue(bare.Trim(), out var key) && All.Value.ContainsKey(key) ? key : null;
    }

    /// <summary>Род персонажа: Марпл — женский, остальные сыщики — мужской; неизвестный ник — null.</summary>
    public static bool? IsFemale(string? nickname) => KeyOf(nickname) switch { null => null, "marple" => true, _ => false };

    public static IReadOnlyList<string> Lines(string? persona, string situation) =>
        persona is not null && All.Value.TryGetValue(persona, out var book) && book.TryGetValue(situation, out var lines) ? lines : [];

    /// <summary>Случайная реплика персонажа с подстановками; нет реплик или не хватает подстановки — null.</summary>
    public static string? Pick(string? persona, string situation, Random rng, string? card = null, string? who = null,
        string? list = null, string? why = null, string? hint = null, string? to = null)
    {
        var lines = Lines(persona, situation);
        if (lines.Count == 0) return null;
        var line = lines[rng.Next(lines.Count)];
        string? Value(string name) => name switch
        {
            "card" => card, "card_acc" => card is null ? null : Accusative(card), "who" => who, "list" => list,
            "why" => why, "hint" => hint, "to" => to, _ => null,
        };
        var missing = false;
        var filled = Placeholder().Replace(line, m =>
        {
            if (Value(m.Groups[1].Value) is { Length: > 0 } v) return v;
            missing = true;
            return "";
        });
        return missing ? null : filled.Trim();
    }

    /// <summary>«тайна 2» → «тайну 2»; мотив, место и способ в винительном не меняются.</summary>
    public static string Accusative(string refs) => Regex.Replace(refs, @"(?<![а-яё])тайна(?=\s*\d)", "тайну", RegexOptions.IgnoreCase);

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();
}
