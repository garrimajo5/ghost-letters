using System.Text;
using System.Text.RegularExpressions;
using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Infrastructure.Speech;

/// <summary>
/// Текст реплики бота для озвучки. В чате карты — картинками («мотив 1»), а вслух их называют по-человечески:
/// «мотив — клетка», «подсказка второго раунда — колокол». Названия — из разметки карт (names.json / картотека).
/// </summary>
public static partial class SpeechText
{
    /// <summary>Длиннее не озвучиваем: длинное объяснение вслух тянет партию, оно остаётся в чате.</summary>
    public const int MaxLength = 600;

    private static readonly string[] Ordinals =
        ["нулевого", "первого", "второго", "третьего", "четвёртого", "пятого", "шестого", "седьмого", "восьмого", "девятого", "десятого"];

    [GeneratedRegex(@"(?<![а-яёa-z])(Подсказка р\.(\d+) №(\d+)|(мотив|место|способ|тайн[ау])(?:\s*[,—:]\s*(?:карт[ау]\s*)?|\s+)(\d+)(?!\d)|вот эт[оу]|(Зацепка))(?![а-яёa-z])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CardRef();

    [GeneratedRegex(@"[\p{Cs}\p{So}‍️•№]")]
    private static partial Regex Symbols();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <param name="namesUsed">Сюда складываются подставленные названия карт (для проверки пересказа).</param>
    public static string For(string? text, IReadOnlyList<string> cardIds, IReadOnlyList<string>? notes,
        IReadOnlyList<BoardRow> board, IReadOnlyList<HintGroup> hints, CardTags tags, ICollection<string>? namesUsed = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var source = French(text.Trim());
        // Показ человека со стола начинается так (клиент); боты так не пишут, но озвучка общая.
        const string tablePrefix = "Со стола: ";
        if (source.StartsWith(tablePrefix, StringComparison.Ordinal)) source = source[tablePrefix.Length..];
        string? claimed = null;
        for (var i = 0; i < cardIds.Count && notes is not null && i < notes.Count; i++)
            if (notes[i].StartsWith("кидал", StringComparison.Ordinal)) { claimed = cardIds[i]; break; }

        var spoken = CardRef().Replace(source, m =>
        {
            if (m.Groups[2].Success)
            {
                var round = int.Parse(m.Groups[2].Value);
                var index = int.Parse(m.Groups[3].Value) - 1;
                var hint = hints.FirstOrDefault(h => h.Round == round);
                var name = hint is not null && index >= 0 && index < hint.Cards.Count ? tags.NameOf(hint.Cards[index]) : null;
                var which = round == 0 ? "зацепка" : $"подсказка {(round < Ordinals.Length ? Ordinals[round] : round.ToString())} раунда";
                if (name is not null) namesUsed?.Add(name);
                return name is null ? which : $"{which} — {name}";
            }

            if (m.Groups[4].Success)
            {
                var word = m.Groups[4].Value;
                var column = int.Parse(m.Groups[5].Value) - 1;
                var row = board.FirstOrDefault(r => string.Equals(CategoryWord(r.Category), word.StartsWith("тайн", StringComparison.OrdinalIgnoreCase) ? "тайна" : word, StringComparison.OrdinalIgnoreCase));
                var name = row is not null && column >= 0 && column < row.Cards.Count ? tags.NameOf(row.Cards[column]) : null;
                if (name is not null) namesUsed?.Add(name);
                return name is null ? m.Value : $"{word} — {name}";
            }

            if (m.Groups[6].Success)
            {
                var first = hints.FirstOrDefault(h => h.Round == 0)?.Cards.FirstOrDefault();
                var name = first is null ? null : tags.NameOf(first);
                if (name is not null) namesUsed?.Add(name);
                return name is null ? m.Value : $"{m.Value} — {name}";
            }

            var own = claimed is null ? null : tags.NameOf(claimed);
            if (own is not null) namesUsed?.Add(own);
            return own is null ? m.Value : $"{m.Value} — {own}";
        });

        spoken = spoken.Replace("→", " указывает на ").Replace("\n", ". ");
        spoken = Symbols().Replace(spoken, " ");
        spoken = Spaces().Replace(spoken, " ").Replace(" .", ".").Replace(" ,", ",").Replace("..", ".").Trim();
        return Cut(spoken);
    }

    private static readonly (string From, string To)[] FrenchWords =
    [
        ("mon ami", "мон ами"), ("mon cher", "мон шер"), ("mon Dieu", "мон дьё"), ("voilà", "вуаля"), ("voila", "вуаля"),
        ("mais oui", "мэ уи"), ("mais non", "мэ нон"), ("bien sûr", "бьен сюр"), ("n'est-ce pas", "нэс па"), ("parbleu", "парблё"),
    ];

    /// <summary>Французские словечки Пуаро вслух — по-русски, иначе синтез читает их по буквам.</summary>
    private static string French(string text)
    {
        foreach (var (from, to) in FrenchWords)
            text = Regex.Replace(text, @"(?<![\p{L}])" + Regex.Escape(from) + @"(?![\p{L}])", to, RegexOptions.IgnoreCase);
        return text;
    }

    /// <summary>Обрезать по концу предложения, не длиннее <see cref="MaxLength"/>.</summary>
    public static string Cut(string text)
    {
        if (text.Length <= MaxLength) return text;
        var end = text.LastIndexOfAny(['.', '!', '?'], MaxLength - 1);
        return end > MaxLength / 3 ? text[..(end + 1)] : text[..MaxLength].TrimEnd() + "…";
    }

    /// <summary>Разбить на куски для синтеза (SpeechKit тарифицирует по 250 символов): по предложениям, затем по запятым.</summary>
    public static IReadOnlyList<string> Chunks(string text, int max = 240)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        foreach (var sentence in Pieces(text, ['.', '!', '?', '…'], max))
        {
            if (current.Length > 0 && current.Length + 1 + sentence.Length > max)
            {
                result.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0) current.Append(' ');
            current.Append(sentence);
        }

        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    private static IEnumerable<string> Pieces(string text, char[] ends, int max)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (!ends.Contains(text[i]) && i != text.Length - 1) continue;
            var piece = text[start..(i + 1)].Trim();
            start = i + 1;
            if (piece.Length == 0) continue;
            if (piece.Length <= max)
            {
                yield return piece;
                continue;
            }

            // Длинное предложение — по запятым и пробелам.
            var rest = piece;
            while (rest.Length > max)
            {
                var cut = rest.LastIndexOfAny([',', ';', '—', ' '], max - 1);
                if (cut < max / 3) cut = max - 1;
                yield return rest[..(cut + 1)].Trim();
                rest = rest[(cut + 1)..].Trim();
            }

            if (rest.Length > 0) yield return rest;
        }
    }

    public static string CategoryWord(Category c) => c switch
    {
        Category.Motive => "мотив",
        Category.Place => "место",
        Category.Method => "способ",
        _ => "тайна",
    };
}
