using System.Text.RegularExpressions;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Infrastructure.Speech;

/// <summary>
/// Проверка «так ли можно назвать карту»: слова названия должны найтись в разметке карты — названиях, смыслах,
/// ассоциациях и деталях («грабли» → карта с названием «грабли»; «ужас» → смысл «страх, ужас»). Если же слово
/// точно описывает другую карту на столе, а эту — нет, это неправда.
/// </summary>
public static partial class CardNaming
{
    public enum Verdict { Valid, Unverified, Invalid }

    [GeneratedRegex(@"[а-яё]+")]
    private static partial Regex Words();

    private static string Stem(string word) => word.Length > 5 ? word[..^2] : word.Length > 3 ? word[..^1] : word;

    private static IEnumerable<string> WordsOf(string text) => Words().Matches(text.ToLowerInvariant().Replace('ё', 'е')).Select(m => m.Value);

    /// <summary>Все слова, которыми описана карта: названия, смыслы (с ассоциациями) и детали.</summary>
    public static HashSet<string> Vocabulary(string card, CardTags tags)
    {
        var annotation = tags.AnnotationOf(card);
        var labels = tags.NamesOf(card).Concat(annotation.Meanings.Select(m => m.Label)).Concat(annotation.Details.Select(d => d.Label));
        return labels.SelectMany(WordsOf).Where(w => w.Length >= 3).ToHashSet();
    }

    private static bool Matches(IEnumerable<string> phrase, HashSet<string> vocabulary) =>
        phrase.Any(p => vocabulary.Any(v => v.StartsWith(Stem(p), StringComparison.Ordinal) || p.StartsWith(Stem(v), StringComparison.Ordinal)));

    public static Verdict Check(string phrase, string card, IEnumerable<string> otherCards, CardTags tags)
    {
        var words = WordsOf(phrase).Where(w => w.Length >= 3).ToList();
        if (words.Count == 0) return Verdict.Unverified;
        if (Matches(words, Vocabulary(card, tags))) return Verdict.Valid;
        // Слово — название другой карты на столе: явно не та карта.
        var elsewhere = otherCards.Where(c => c != card)
            .Any(c => Matches(words, tags.NamesOf(c).SelectMany(WordsOf).Where(w => w.Length >= 3).ToHashSet()));
        return elsewhere ? Verdict.Invalid : Verdict.Unverified;
    }
}
