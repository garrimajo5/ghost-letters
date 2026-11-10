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

    /// <summary>
    /// Боты 3.0: Призрак решает по модели стола (TableModel) — открывает письма, которые
    /// поднимают вероятность истины в глазах стола, и не открывает те, что уводят ряд к ложной карте.
    /// </summary>
    public bool GhostModel { get; init; }

    /// <summary>Сколько выигрыша (в log-вероятности истины) нужно письму, чтобы его стоило открыть осторожному Призраку.</summary>
    public double GhostModelCost { get; init; } = .15;

    /// <summary>Насколько стол верит исчезновению чужого письма (узнаёт о нём со слов отправителя).</summary>
    public double GhostModelVanish { get; init; } = .5;

    /// <summary>Боты 3.0: мирный выбирает письмо по ожидаемой информации на модели стола (широкое в начале, узкое потом).</summary>
    public bool LetterModel { get; init; }

    /// <summary>
    /// Насколько мирный в модели письма учитывает, что Призрак не откроет двусмысленное письмо:
    /// указание на карту ослабляется, если письмо так же похоже на соседнюю карту ряда (0 — не учитывает).
    /// </summary>
    public double LetterClarity { get; init; }

    /// <summary>Боты 3.0, этап 2: модель игроков — подозрения как вероятности с учётом числа чёрных и «малой пользы».</summary>
    public bool RoleModel { get; init; } = true;

    /// <summary>Модель игроков: насколько «громкие» улики (ложь о проверках, чужие письма) при нескольких чёрных уводят подозрение с Убийцы на Сообщника.</summary>
    public double AccompliceLoud { get; init; } = .7;

    /// <summary>Призрак на модели: ряд, где стол уверен в истине не меньше этого, больше не уточняем (1 — уточняем всё).</summary>
    public double GhostModelSettled { get; init; } = 1.01;

    /// <summary>Модель игроков в подозрениях вслух. Выключено: боты с ней обвиняли реже, и стол терял (8 игроков: 44 → 35 %).</summary>
    public bool RoleModelSpeech { get; init; } = true;

    /// <summary>Для A/B: модель игроков в голосе за Убийцу.</summary>
    public bool RoleModelVote { get; init; } = true;

    /// <summary>Боты 3.0, этап 3: Убийца шлёт ложное письмо в ряд, где стол уже уверен (мало пользы мирным).</summary>
    public bool KillerClearRows { get; init; }

    /// <summary>Боты 3.0, этап 3: Сообщник вредит по ситуации (подозревают ли Убийцу или его самого, последний раунд).</summary>
    public bool AccompliceSituational { get; init; } = true;

    /// <summary>Сообщник по ситуации: доля вредных ходов, пока никого не подозревают.</summary>
    public double AccompliceSpoilBase { get; init; } = .8;

    /// <summary>Боты 3.0, этап 3: на охоте чёрные ищут Свидетеля по приметам (первым и в одиночку назвал Убийцу).</summary>
    public bool HuntTells { get; init; } = true;

    /// <summary>Боты 3.0, этап 4: Эксперт целит каждым письмом в свою истинную карту (раньше — все письма в одну).</summary>
    public bool ExpertAllLetters { get; init; } = true;

    /// <summary>Боты 3.0, этап 5: тема реплики — неясный ряд со своей уликой, а не самый ясный (меньше эха).</summary>
    public bool TopicUnclear { get; init; }

    /// <summary>Боты 3.0, этап 5: бот молчит, если его реплика — эхо (за карту в раунде уже сказали двое).</summary>
    public bool SkipEcho { get; init; }

    /// <summary>Боты 3.0, этап 4: Эксперт присваивает себе только часть открытых писем.</summary>
    public bool ExpertModestClaims { get; init; } = true;

    /// <summary>Боты 3.0, этап 5: в финале полную сводку дают двое, остальные — только расхождения.</summary>
    public bool FinalShort { get; init; } = true;

    /// <summary>Боты 4.0, шаг 2: доверие к словам игрока — из модели игроков (той же, что подозрения и голос).</summary>
    public bool UnifiedTrust { get; init; } = true;

    /// <summary>Боты 4.0, шаг 3: температура мягкого выбора (0 — всегда лучший ход).</summary>
    public double SoftTemp { get; init; } = .1;

    /// <summary>Множитель мягкого выбора для карт в рядах (мнения и голоса).</summary>
    public double SoftCards { get; init; }

    /// <summary>Множитель мягкого выбора для писем.</summary>
    public double SoftLetters { get; init; } = 1;

    /// <summary>Боты 4.0, шаг 3: голос за Убийцу по полезности (модель + стол) с мягким выбором.</summary>
    public bool VoteUtility { get; init; }

    /// <summary>Температура голоса за Убийцу.</summary>
    public double VoteTemp { get; init; } = .1;

    /// <summary>Вес мнения стола в голосе за Убийцу (умножается на сговорчивость).</summary>
    public double VoteConformity { get; init; } = .3;

    /// <summary>Боты 4.0, шаг 4: слова прошлых раундов забываются по памяти характера и заметности.</summary>
    public bool Forgetting { get; init; }

    /// <summary>Открывать письмо, только если на истину оно указывает не слабее этой доли от лучшей ложной (1 — «50/50»).</summary>
    public double GhostModelHonest { get; init; } = 1;

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

    /// <summary>
    /// Письмо исчезло, но Призрак в том же раунде выложил другую подсказку, ещё ближе к проверенной карте, —
    /// значит, он просто выбрал лучшее. Штраф карте умножается на это (1 — как раньше, отвергаем полностью).
    /// </summary>
    public double Outshone { get; init; } = .2;

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
                nameof(GhostModel) => t with { GhostModel = B() },
                nameof(GhostModelCost) => t with { GhostModelCost = D() },
                nameof(GhostModelVanish) => t with { GhostModelVanish = D() },
                nameof(GhostModelHonest) => t with { GhostModelHonest = D() },
                nameof(LetterModel) => t with { LetterModel = B() },
                nameof(LetterClarity) => t with { LetterClarity = D() },
                nameof(RoleModel) => t with { RoleModel = B() },
                nameof(AccompliceLoud) => t with { AccompliceLoud = D() },
                nameof(GhostModelSettled) => t with { GhostModelSettled = D() },
                nameof(RoleModelSpeech) => t with { RoleModelSpeech = B() },
                nameof(RoleModelVote) => t with { RoleModelVote = B() },
                nameof(KillerClearRows) => t with { KillerClearRows = B() },
                nameof(AccompliceSituational) => t with { AccompliceSituational = B() },
                nameof(AccompliceSpoilBase) => t with { AccompliceSpoilBase = D() },
                nameof(HuntTells) => t with { HuntTells = B() },
                nameof(ExpertAllLetters) => t with { ExpertAllLetters = B() },
                nameof(TopicUnclear) => t with { TopicUnclear = B() },
                nameof(SkipEcho) => t with { SkipEcho = B() },
                nameof(ExpertModestClaims) => t with { ExpertModestClaims = B() },
                nameof(FinalShort) => t with { FinalShort = B() },
                nameof(UnifiedTrust) => t with { UnifiedTrust = B() },
                nameof(SoftTemp) => t with { SoftTemp = D() },
                nameof(SoftCards) => t with { SoftCards = D() },
                nameof(SoftLetters) => t with { SoftLetters = D() },
                nameof(VoteUtility) => t with { VoteUtility = B() },
                nameof(VoteTemp) => t with { VoteTemp = D() },
                nameof(VoteConformity) => t with { VoteConformity = D() },
                nameof(Forgetting) => t with { Forgetting = B() },
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
                nameof(Outshone) => t with { Outshone = D() },
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
