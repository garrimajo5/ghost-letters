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

    /// <summary>Письма — проверки: однозначные, по правдоподобным картам неясных рядов, разные у разных игроков.</summary>
    public bool Probe { get; init; } = true;

    /// <summary>Вес чужих исчезнувших писем (по их публичным «кидал эту — проверял…») относительно своих.</summary>
    public double OthersVanished { get; init; } = .5;

    /// <summary>Команда Убийцы не спорит с ясными рядами: голосует «как все» и лжёт только там, где ложь правдоподобна.</summary>
    public bool KillerBlend { get; init; } = true;

    /// <summary>Как часто в ясном ряду голос «как все» (иначе — обычная ложь по характеру).</summary>
    public double KillerBlendChance { get; init; } = .8;

    /// <summary>С какого отрыва истины от лучшей ложной карты ряд считается ясным.</summary>
    public double KillerClearMargin { get; init; } = .15;

    /// <summary>Мирный следует мнению стола, если отрыв двух его лучших карт меньше этого (0 — по характеру).</summary>
    public double UnsureFollow { get; init; }

    /// <summary>Проверка: насколько важно правдоподобие карты и неясность ряда (степени).</summary>
    public double ProbePlausible { get; init; } = 2;
    public double ProbeUnclear { get; init; } = 1;

    /// <summary>Как часто чёрный выдаёт за своё исчезнувшее письмо другую карту — похожую на истину (× его склонность ко лжи).</summary>
    public double KillerFakeVanish { get; init; }

    /// <summary>Чёрные смелее лгут за большим столом (больше подозреваемых) и осторожнее — за маленьким.</summary>
    public bool KillerSizeAware { get; init; } = true;

    /// <summary>
    /// Эксперт не выдаёт себя точностью: в ясных рядах иногда голосует «как стол», в речи чаще отсекает ложные
    /// карты, чем называет истину, письма шлёт полезные, но не всегда.
    /// </summary>
    public bool ExpertSubtle { get; init; } = true;

    /// <summary>Свидетель молчит, когда стол и так побеждает по картам или уже подозревает Убийцу.</summary>
    public bool WitnessQuiet { get; init; } = true;

    /// <summary>Сообщник мешает картам, а не прячется: чаще лжёт о письме, отметает истину, редко голосует «как все».</summary>
    public bool AccompliceSpoiler { get; init; } = true;

    /// <summary>Как часто Сообщник-вредитель отметает истину в речи и выдаёт другую карту за исчезнувшее письмо.</summary>
    public double SpoilerExclude { get; init; } = .5;
    public double SpoilerVanish { get; init; } = .7;

    /// <summary>Вес подозрения к тому, кто отверг карту, на которую потом указала подсказка (0 — не замечать).</summary>
    public double LieDetect { get; init; } = 2;

    /// <summary>Насколько мирный не верит проверкам и мнениям подозреваемых (0 — верит всем одинаково).</summary>
    public double SuspectDistrust { get; init; } = 1;

    /// <summary>Множитель того, как часто Шантажист голосует за вторую по уликам карту (1 — как раньше; .4 — по замеру 8 игроков 28 → 35 %).</summary>
    public double BlackmailerSpoil { get; init; } = .4;

    /// <summary>Чёрный в финальном голосовании идёт за столом, если стол сам склоняется к ложной карте.</summary>
    public bool KillerFollowWrong { get; init; } = true;

    /// <summary>
    /// Призрак не оставляет стол без улик: если полезных писем нет, открывает лучшее, которое вредит не больше этого
    /// (по оценке стола). 0 — как раньше: может не открыть ничего и не дать зацепку.
    /// </summary>
    public double GhostFallback { get; init; } = .15;

    /// <summary>
    /// Зацепку Призрак даёт всегда, если в руке есть карта, хоть как-то похожая на истину: без неё первое
    /// обсуждение пустое. Берётся карта с лучшим отрывом истины от ложных, даже если ложная похожа сильнее.
    /// </summary>
    public bool FirstClueAlways { get; init; } = true;

    /// <summary>
    /// Запасное письмо раунда — только если на истину оно похоже сильнее, чем на любую ложную карту.
    /// false — достаточно, чтобы отрыв был лучшим среди писем и стол от него почти не терял.
    /// </summary>
    public bool GhostFallbackStrict { get; init; } = true;

    /// <summary>Множитель веса ассоциаций по смыслу (вторичных смыслов карт); 0 — не учитывать.</summary>
    public double AssociationScale { get; init; } = .5;

    /// <summary>Доля подозрения по последней версии игрока (0 — все его мнения за партию одинаково).</summary>
    public double LatestVersionWeight { get; init; } = .8;

    /// <summary>
    /// Письмо «проверяет» только те карты поля, что похожи на него почти как самая похожая (доля от лучшего сходства).
    /// Слабая побочная ассоциация — не проверка: Призрак-человек открыл бы письмо ради сильной связи, а не ради неё.
    /// </summary>
    public double CheckRelative { get; init; } = .7;

    /// <summary>Призрак открывает письмо, только если истинная карта — среди сильных связей письма (доля от лучшего сходства).</summary>
    public double GhostClear { get; init; } = .75;

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
                nameof(Probe) => t with { Probe = B() },
                nameof(KillerSizeAware) => t with { KillerSizeAware = B() },
                nameof(KillerFakeVanish) => t with { KillerFakeVanish = D() },
                nameof(ProbePlausible) => t with { ProbePlausible = D() },
                nameof(ProbeUnclear) => t with { ProbeUnclear = D() },
                nameof(UnsureFollow) => t with { UnsureFollow = D() },
                nameof(KillerBlend) => t with { KillerBlend = B() },
                nameof(KillerBlendChance) => t with { KillerBlendChance = D() },
                nameof(KillerClearMargin) => t with { KillerClearMargin = D() },
                nameof(OthersVanished) => t with { OthersVanished = D() },
                nameof(LetterStrict) => t with { LetterStrict = D() },
                nameof(ExpertSubtle) => t with { ExpertSubtle = B() },
                nameof(WitnessQuiet) => t with { WitnessQuiet = B() },
                nameof(AccompliceSpoiler) => t with { AccompliceSpoiler = B() },
                nameof(SpoilerExclude) => t with { SpoilerExclude = D() },
                nameof(SpoilerVanish) => t with { SpoilerVanish = D() },
                nameof(BlackmailerSpoil) => t with { BlackmailerSpoil = D() },
                nameof(AssociationScale) => t with { AssociationScale = D() },
                nameof(KillerFollowWrong) => t with { KillerFollowWrong = B() },
                nameof(GhostFallback) => t with { GhostFallback = D() },
                nameof(FirstClueAlways) => t with { FirstClueAlways = B() },
                nameof(GhostFallbackStrict) => t with { GhostFallbackStrict = B() },
                nameof(LatestVersionWeight) => t with { LatestVersionWeight = D() },
                nameof(LieDetect) => t with { LieDetect = D() },
                nameof(SuspectDistrust) => t with { SuspectDistrust = D() },
                nameof(CheckRelative) => t with { CheckRelative = D() },
                nameof(GhostClear) => t with { GhostClear = D() },
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
