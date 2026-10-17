using System.Text.RegularExpressions;

namespace GhostLetters.Infrastructure.Speech;

/// <summary>Общие для живой речи ботов обороты: падеж ссылки на карту, «почему похожи» и «почему подозреваю» по-человечески.</summary>
public static class SpeechWords
{
    /// <summary>«тайна 2» после «в», «на», «про», «за» — «тайну 2»; мотив, место и способ в винительном не меняются.</summary>
    public static string Acc(string refs) => Regex.Replace(refs, @"(?<![а-яё])тайна(?=\s*\d)", "тайну", RegexOptions.IgnoreCase);

    /// <summary>«по смыслу: море; по форме: вытянутая форма» → «там тоже море» (без тире и двоеточий — их ставит фраза вокруг).</summary>
    public static string Reason(string why, Random rng)
    {
        var first = why.Split(';')[0].Trim();
        var m = Regex.Match(first, @"^по (?<kind>смыслу|форме|цвету|деталям): (?<what>.+)$");
        if (!m.Success) return first;
        var what = m.Groups["what"].Value.Trim();
        string Pick(params string[] options) => options[rng.Next(options.Length)];
        return m.Groups["kind"].Value switch
        {
            "смыслу" => what == "общий предмет или тема" ? "общая тема" : Pick($"там тоже {what}", $"и там {what}", $"там опять {what}"),
            "форме" => Shapes.TryGetValue(what, out var both) ? Pick($"{both}, похожи формой", $"похожи формой, {both}") : "похожи формой",
            "цвету" => what == "выраженный цветовой акцент" ? "похожи цветом" : Pick($"похожи цветом ({what})", $"цвет тот же ({what})"),
            _ => Pick($"общая деталь ({what})", $"совпадает деталь ({what})"),
        };
    }

    /// <summary>Как человек скажет о похожей форме: «оба вытянутые», а не «вытянутая форма».</summary>
    private static readonly IReadOnlyDictionary<string, string> Shapes = new Dictionary<string, string>
    {
        ["круглая форма"] = "оба круглые", ["вытянутая форма"] = "оба вытянутые", ["квадратная форма"] = "оба квадратные",
        ["треугольная форма"] = "оба треугольные", ["прямоугольная форма"] = "оба прямоугольные", ["изогнутая форма"] = "оба изогнутые",
        ["острые выступы"] = "у обоих острые края", ["высокий вытянутый силуэт"] = "оба высокие и узкие", ["широкий силуэт"] = "оба широкие",
    };

    /// <summary>Причина подозрения из рассуждения бота — короче, как сказал бы человек.</summary>
    public static string Behaviour(string why) => why switch
    {
        "его версия хуже объясняет открытые подсказки, чем соседние карты" => "его версия плохо сходится с подсказками",
        "его голоса расходятся с моим чтением подсказок" => "голосует мимо подсказок",
        "пока опираюсь на подозрения стола и прошлые встречи, прямой улики нет" => "пока просто чутьё",
        _ => why,
    };
}
