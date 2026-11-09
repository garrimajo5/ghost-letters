using System.Text.RegularExpressions;

namespace GhostLetters.Infrastructure.Speech;

/// <summary>
/// Интонация бота вслух (разметка и подсказки SpeechKit). Логическое ударение — на названии карты, о которой
/// реплика (<c>**слово**</c>), амплуа — по настроению реплики (обвинение строже, радость теплее), темп — по манере.
/// Чат это не видит: разметка добавляется только в текст для синтеза.
/// </summary>
public static partial class SpeechProsody
{
    public enum Mood { Calm, Stern, Warm }

    [GeneratedRegex(@"подозр|ч[её]рн|убийц|присмотр|темнит|врёт|врет|лжёт|лжет|мутн", RegexOptions.IgnoreCase)]
    private static partial Regex Accusation();

    [GeneratedRegex(@"(?<![а-яё])(?:ура|отлично|здорово|браво|прекрасно|хорошая партия|спасибо|поздравля|на верном пути|тепло|получилось|молодц)", RegexOptions.IgnoreCase)]
    private static partial Regex Joy();

    [GeneratedRegex(@"[^.!?…]+[.!?…]*")]
    private static partial Regex Sentences();

    /// <summary>Настроение реплики: обвинение — строго, радость и похвала — тепло, остальное — спокойно.</summary>
    public static Mood MoodOf(string text)
    {
        if (Accusation().IsMatch(text)) return Mood.Stern;
        return Joy().IsMatch(text) || text.Count(c => c == '!') >= 2 ? Mood.Warm : Mood.Calm;
    }

    /// <summary>Тон голоса по замыслу реплики: подозревает или защищается — строже, рад или уверен — теплее.</summary>
    public static Mood MoodOf(SpeechMood mood) => mood switch
    {
        SpeechMood.Suspicious or SpeechMood.Defensive => Mood.Stern,
        SpeechMood.Excited or SpeechMood.Confident => Mood.Warm,
        _ => Mood.Calm,
    };

    /// <summary>
    /// Амплуа для этой реплики. Выбранное в кабинете «строго» или «зловеще» — характер, его не меняем;
    /// спокойный и радушный голос строже звучит в обвинении и теплее — в радости, если голос так умеет.
    /// </summary>
    public static string? RoleFor(string voice, string? role, Mood mood)
    {
        if (role is "strict" or "evil" || BotVoices.Find(voice) is not { } option) return role;
        string[] wanted = mood switch
        {
            Mood.Stern => ["strict"],
            Mood.Warm => ["good", "friendly"],
            _ => [],
        };
        return wanted.FirstOrDefault(option.Roles.Contains) ?? role;
    }

    /// <summary>Сколько символов в секунду читает голос при темпе 1 (русский, SpeechKit — около 14).</summary>
    public const double CharsPerSecond = 14;

    /// <summary>Сколько секунд может длиться голос бота.</summary>
    public const double MaxSeconds = 15;

    /// <summary>
    /// Уложить голос в <see cref="MaxSeconds"/>: сначала чуть быстрее (до 1.25), если всё ещё длинно —
    /// только первые предложения, которые влезают. Первое предложение остаётся всегда.
    /// </summary>
    public static (string Text, double Speed) Fit(string text, double speed)
    {
        double Seconds(string t, double s) => t.Length / (CharsPerSecond * s);
        if (Seconds(text, speed) <= MaxSeconds) return (text, speed);
        var faster = Math.Min(1.25, Math.Max(speed, text.Length / (CharsPerSecond * MaxSeconds)));
        if (Seconds(text, faster) <= MaxSeconds) return (text, faster);
        var sentences = System.Text.RegularExpressions.Regex.Split(text, @"(?<=[.!?…])\s+").Where(x => x.Length > 0).ToList();
        var kept = new System.Text.StringBuilder(sentences[0]);
        foreach (var sentence in sentences.Skip(1))
        {
            if (Seconds(kept + " " + sentence, faster) > MaxSeconds) break;
            kept.Append(' ').Append(sentence);
        }
        return (kept.ToString(), faster);
    }

    /// <summary>Темп по манере: живой говорит чуть быстрее, официальный — размереннее.</summary>
    public static double SpeedOf(SpokenStyle.Manner manner) => manner switch
    {
        SpokenStyle.Manner.Lively => 1.08,
        SpokenStyle.Manner.Formal => .95,
        _ => 1,
    };

    /// <summary>
    /// Логическое ударение: в каждом предложении — на первом названии карты (его последнем слове): «мотив — птичья **клетка**».
    /// Так голос выделяет то, о чём реплика, а не ровно читает всё подряд.
    /// </summary>
    public static string Mark(string text, IReadOnlyCollection<string> cardNames)
    {
        if (cardNames.Count == 0 || text.Contains("**", StringComparison.Ordinal)) return text;
        var names = cardNames.Where(n => n.Length >= 3).Distinct().OrderByDescending(n => n.Length).ToList();
        return Sentences().Replace(text, m =>
        {
            var sentence = m.Value;
            var first = names.Select(n => (Name: n, At: Find(sentence, n))).Where(x => x.At >= 0).OrderBy(x => x.At).FirstOrDefault();
            if (first.Name is null) return sentence;
            var last = first.Name.LastIndexOf(' ');
            var word = last < 0 ? first.Name : first.Name[(last + 1)..];
            var at = first.At + (last < 0 ? 0 : last + 1);
            if (word.Length < 3) return sentence;
            return sentence[..at] + "**" + sentence.Substring(at, word.Length) + "**" + sentence[(at + word.Length)..];
        });
    }

    private static int Find(string text, string name)
    {
        var m = Regex.Match(text, $@"(?<![\p{{L}}]){Regex.Escape(name)}(?![\p{{L}}])", RegexOptions.IgnoreCase);
        return m.Success ? m.Index : -1;
    }
}
