using System.Globalization;

namespace GhostLetters.Infrastructure.Games;

/// <summary>
/// Настройки игры ботов, которые подбираются замерами баланса. В игре всегда значения по умолчанию;
/// серия экспериментов в песочнице подменяет их на время своего потока (AsyncLocal), не задевая другие партии.
/// </summary>
public sealed record BotTuning
{
    /// <summary>Как сильно подсказка «объясняется» лучшим совпадением: фокус = Base + Strict × строгость (0 и 0 — без фокуса).</summary>
    public double FocusBase { get; init; } = 4;
    public double FocusStrict { get; init; } = 8;

    /// <summary>Призрак примеряет письма на стол (иначе — прежний отбор по ясности каждого письма).</summary>
    public bool GhostPlans { get; init; } = true;

    /// <summary>Сколько вреда для стола рискованный Призрак готов стерпеть ради сомнительного письма (× риск²).</summary>
    public double GhostTolerance { get; init; } = 0;

    /// <summary>Призрак моделирует стол «средним» взглядом на карты, а не своим.</summary>
    public bool GhostTableDefault { get; init; }

    /// <summary>Призрак подстраивает свою модель стола под ширину чтения игроков.</summary>
    public bool GhostBreadth { get; init; } = true;

    /// <summary>Фокус чтения и для исчезнувших писем.</summary>
    public bool VanishedFocus { get; init; } = true;

    /// <summary>Однозначность письма: штраф за сходство с другими картами поля = Base + Strict × строгость.</summary>
    public double LetterBase { get; init; } = .4;
    public double LetterStrict { get; init; } = .4;

    public static readonly BotTuning Default = new();

    private static readonly AsyncLocal<BotTuning?> Local = new();

    public static BotTuning Current => Local.Value ?? Default;

    /// <summary>Подменить настройки до Dispose (только для серий экспериментов).</summary>
    public static IDisposable Use(BotTuning tuning)
    {
        var previous = Local.Value;
        Local.Value = tuning;
        return new Restore(() => Local.Value = previous);
    }

    /// <summary>«FocusBase=0, GhostPlans=false» → настройки поверх значений по умолчанию.</summary>
    public static BotTuning Parse(string spec)
    {
        var t = Default;
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2) throw new FormatException($"Ожидалось имя=значение: {part}");
            double D() => double.Parse(kv[1], CultureInfo.InvariantCulture);
            bool B() => bool.Parse(kv[1]);
            t = kv[0] switch
            {
                nameof(FocusBase) => t with { FocusBase = D() },
                nameof(FocusStrict) => t with { FocusStrict = D() },
                nameof(GhostPlans) => t with { GhostPlans = B() },
                nameof(GhostTolerance) => t with { GhostTolerance = D() },
                nameof(GhostBreadth) => t with { GhostBreadth = B() },
                nameof(GhostTableDefault) => t with { GhostTableDefault = B() },
                nameof(VanishedFocus) => t with { VanishedFocus = B() },
                nameof(LetterBase) => t with { LetterBase = D() },
                nameof(LetterStrict) => t with { LetterStrict = D() },
                _ => throw new FormatException($"Неизвестная настройка: {kv[0]}"),
            };
        }

        return t;
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
