namespace GhostLetters.Infrastructure.Bots;

/// <summary>
/// Боты 3.0: модель стола — насколько каждая карта поля может быть истинной, и модель Призрака —
/// с какой вероятностью он откроет письмо при той или иной истине.
///
/// Призрак открывает письмо, если оно указывает на истинную карту какого-то ряда. Указывает — значит, эта карта
/// среди самых похожих на письмо на всём поле (как игрок «объясняет» подсказку лучшим совпадением).
/// Вероятность открыть письмо при истине t — «шумное ИЛИ» по рядам: 1 − Π(1 − λ(r, t_r)), где λ — насколько
/// письмо указывает на карту (от 0 до 1) и небольшой шум ε (Призрак ошибается или открывает ради другого).
/// По этой модели считаются: обновление после открытия / исчезновения письма, шанс открытия и ожидаемая
/// информация письма (насколько оно уменьшит неопределённость рядов).
/// </summary>
public sealed class TableModel
{
    /// <summary>Шум модели Призрака: открывает неуказывающее письмо или не открывает указывающее.</summary>
    public const double Noise = .05;

    private readonly IReadOnlyList<IReadOnlyList<string>> _rows;
    private readonly double[][] _p;
    private readonly Func<string, string, double> _sim;
    private readonly double _focus;

    private TableModel(IReadOnlyList<IReadOnlyList<string>> rows, double[][] p, Func<string, string, double> sim, double focus)
    {
        _rows = rows;
        _p = p;
        _sim = sim;
        _focus = focus;
    }

    /// <summary>Чистая модель: все карты ряда равновероятны.</summary>
    /// <param name="focus">Как резко игрок «объясняет» письмо лучшим совпадением (больше — строже).</param>
    public static TableModel Uniform(IReadOnlyList<IReadOnlyList<string>> rows, Func<string, string, double> sim, double focus = 8) =>
        new(rows, rows.Select(r => Enumerable.Repeat(1.0 / Math.Max(1, r.Count), r.Count).ToArray()).ToArray(), sim, focus);

    public IReadOnlyList<IReadOnlyList<string>> Rows => _rows;

    /// <summary>Вероятность, что карта (ряд, столбец) истинна.</summary>
    public double P(int row, int column) => _p[row][column];

    /// <summary>Вероятность карты по её id; нет на поле — 0.</summary>
    public double P(string card)
    {
        for (var r = 0; r < _rows.Count; r++)
            for (var c = 0; c < _rows[r].Count; c++)
                if (_rows[r][c] == card) return _p[r][c];
        return 0;
    }

    /// <summary>Самая вероятная карта ряда.</summary>
    public int Best(int row) => Array.IndexOf(_p[row], _p[row].Max());

    /// <summary>Неопределённость ряда в битах (0 — ряд ясен).</summary>
    public double Entropy(int row) => -_p[row].Where(x => x > 0).Sum(x => x * Math.Log2(x));

    public double Entropy() => Enumerable.Range(0, _rows.Count).Sum(Entropy);

    /// <summary>Насколько письмо указывает на каждую карту поля: λ ∈ [0, 1], лучшее совпадение — 1.</summary>
    public double[][] Pointing(string letter)
    {
        var s = _rows.Select(r => r.Select(c => c == letter ? 0 : Math.Max(0, _sim(letter, c))).ToArray()).ToArray();
        var top = s.SelectMany(x => x).DefaultIfEmpty(0).Max();
        if (top <= 0) return s.Select(r => r.Select(_ => 0.0).ToArray()).ToArray();
        // Карта заметно слабее лучшей почти не «читается» — как у живого игрока.
        return s.Select(r => r.Select(x => x <= 0 ? 0 : x / top * Math.Exp(-_focus * (top - x))).ToArray()).ToArray();
    }

    /// <summary>Вероятность, что Призрак откроет письмо при истине (row, column) — ε + (1 − 2ε)·λ.</summary>
    private static double Lambda(double pointing) => Noise + (1 - 2 * Noise) * pointing;

    /// <summary>Шанс, что Призрак откроет письмо — по текущим вероятностям рядов.</summary>
    public double RevealChance(string letter)
    {
        var w = Pointing(letter);
        var none = 1.0;
        for (var r = 0; r < _rows.Count; r++)
            none *= 1 - Enumerable.Range(0, _rows[r].Count).Sum(c => _p[r][c] * Lambda(w[r][c]));
        // ε у каждого ряда складывается в шум выше заданного: оставляем один ε на всё письмо.
        return Math.Clamp(1 - none, Noise, 1 - Noise);
    }

    /// <summary>
    /// Модель после того, как Призрак открыл письмо (<paramref name="revealed"/>) или оно исчезло.
    /// <paramref name="weight"/> — насколько верим, что это письмо и правда было (1 — своё или открытое;
    /// меньше — чужое «исчезло» со слов игрока, которому доверяем не до конца).
    /// </summary>
    public TableModel With(string letter, bool revealed, double weight = 1)
    {
        var w = Pointing(letter);
        // Вероятность, что ряд r «не сработал» (Призрак не открыл бы письмо ради этого ряда), в среднем по ряду.
        var miss = Enumerable.Range(0, _rows.Count)
            .Select(r => 1 - Enumerable.Range(0, _rows[r].Count).Sum(c => _p[r][c] * Lambda(w[r][c]))).ToArray();
        var next = new double[_rows.Count][];
        for (var r = 0; r < _rows.Count; r++)
        {
            var others = 1.0;
            for (var o = 0; o < _rows.Count; o++) if (o != r) others *= miss[o];
            // Открыл: хоть один ряд сработал. Исчезло: ни один не сработал.
            var likelihood = Enumerable.Range(0, _rows[r].Count)
                .Select(c => revealed ? 1 - (1 - Lambda(w[r][c])) * others : (1 - Lambda(w[r][c])) * others).ToArray();
            // Доверие к источнику: смесь со средним правдоподобием ряда — «ничего не узнали».
            var mean = Enumerable.Range(0, likelihood.Length).Sum(c => _p[r][c] * likelihood[c]);
            var row = Enumerable.Range(0, likelihood.Length).Select(c => _p[r][c] * (weight * likelihood[c] + (1 - weight) * mean)).ToArray();

            var sum = row.Sum();
            next[r] = sum <= 0 ? (double[])_p[r].Clone() : row.Select(x => x / sum).ToArray();
        }

        return new TableModel(_rows, next, _sim, _focus);
    }

    /// <summary>Ожидаемая информация письма в битах: насколько в среднем уменьшится неопределённость рядов.</summary>
    public double InfoGain(string letter)
    {
        var q = RevealChance(letter);
        return Entropy() - (q * With(letter, true).Entropy() + (1 - q) * With(letter, false).Entropy());
    }

    /// <summary>Сумма log-вероятностей истины — насколько стол близок к ответу (для Призрака).</summary>
    public double TruthScore(IReadOnlyList<int> truth) =>
        Enumerable.Range(0, Math.Min(truth.Count, _rows.Count)).Sum(r => Math.Log(Math.Max(1e-6, _p[r][truth[r]])));

    /// <summary>Сколько рядов стол сейчас назвал бы верно.</summary>
    public int RowsRight(IReadOnlyList<int> truth) =>
        Enumerable.Range(0, Math.Min(truth.Count, _rows.Count)).Count(r => Best(r) == truth[r]);
}
