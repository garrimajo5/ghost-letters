using System.Globalization;
using System.Text;
using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

/// <summary>
/// Боты 3.0, этап 0: метрики качества игры ботов — не только «кто победил», а как играют.
/// Серия — только с BOT_METRICS_GAMES=N (отчёт в BOT_METRICS_OUT); BOT_METRICS_VARIANTS — варианты BotTuning,
/// как в A/B-серии баланса («имя: Настройка=значение, … | …»). Обычный прогон — одна короткая проверка расчёта.
/// </summary>
public sealed class BotMetricsTests
{
    private static readonly IReadOnlyList<string> Deck = Enumerable.Range(1, 758).Select(i => $"orig_{i:0000}").ToList();

    /// <summary>Целимся в правила «Озона» — по ним играют опытные игроки.</summary>
    private static readonly (string Name, SandboxOptions Options)[] Configs =
    [
        ("Озон, 5 игроков (6 карт, 4 раунда)", SandboxOptions.Ozon(5)),
        ("Озон, 6 игроков (+Сообщник, 4 раунда)", SandboxOptions.Ozon(6)),
        ("Озон, 8 игроков (3 раунда)", SandboxOptions.Ozon(8)),
        ("Озон, 10 игроков (3 раунда)", SandboxOptions.Ozon(10)),
    ];

    private static CardTags RealTags()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "client", "assets", "cards", "tags.json"))) dir = dir.Parent;
        var cards = Path.Combine(dir!.FullName, "client", "assets", "cards");
        return CardTags.Parse(File.ReadAllText(Path.Combine(cards, "tags.json")))
            .WithDetails(File.ReadAllText(Path.Combine(cards, "details.json")))
            .WithAssociations(File.ReadAllText(Path.Combine(cards, "associations.json")));
    }

    /// <summary>Накопленные метрики серии.</summary>
    public sealed class Metrics
    {
        public int Games, DetWins, Rows, RowsOk;
        public int GhostRounds, GhostReveals, GhostEmptyRounds, RevealTruth, RevealFalse, WithheldTruth;
        public readonly Dictionary<string, (int Letters, int AimTruth, double Breadth, double Clarity, int CutTruth, int Checks)> Letters = new();
        public int KillerAccusedGames, KillerAccusedRoundSum, Accusations, AccusationsBlack;
        public int Messages, Echo, EchoCard, DiscussionRounds, Novel, MindChanges, Speakers, RowRounds, RowVersions, Deductions;

        public void Add(SandboxReport report, GameState state, CardTags tags)
        {
            if (state.Result is not { } result || state.Truth is not { } truth) return;
            Games++;
            if (result.Side == WinningSide.Detectives) DetWins++;
            Rows += report.TotalRows;
            RowsOk += report.CorrectRows;
            var board = state.Board.SelectMany((r, ri) => r.Cards.Select((c, ci) => (Card: c, Row: ri, Truth: truth[ri] == ci))).ToList();
            var truthCards = board.Where(b => b.Truth).Select(b => b.Card).ToHashSet();
            var role = state.Players.ToDictionary(p => p.Id, p => p.Role);
            var ghost = state.Players.FirstOrDefault(p => p.Role == Role.Ghost)?.Id;

            // Самое похожее на письмо на поле и сколько карт почти так же похожи (ширина проверки).
            (string Best, double Top, int Breadth, double Margin) Read(string letter)
            {
                var s = board.Where(b => b.Card != letter).Select(b => (b.Card, S: tags.Similarity(letter, b.Card))).OrderByDescending(x => x.S).ToList();
                if (s.Count == 0 || s[0].S <= 0) return ("", 0, 0, 0);
                return (s[0].Card, s[0].S, s.Count(x => x.S >= s[0].S * .6), s[0].S - (s.Count > 1 ? s[1].S : 0));
            }

            // Призрак: сколько открывает, к истине ли, что придержал.
            foreach (var round in state.Letters.GroupBy(l => l.Round))
            {
                GhostRounds++;
                var revealed = round.Where(l => l.Revealed).ToList();
                GhostReveals += revealed.Count;
                if (revealed.Count == 0) GhostEmptyRounds++;
                foreach (var l in revealed)
                {
                    var best = Read(l.CardId).Best;
                    if (truthCards.Contains(best)) RevealTruth++; else if (best != "") RevealFalse++;
                }
                WithheldTruth += round.Count(l => !l.Revealed && Read(l.CardId) is { Best: var b, Margin: > .05 } && truthCards.Contains(b));
            }

            // Письма по ролям (Призрак не в счёт).
            foreach (var l in state.Letters.Where(l => l.From != ghost))
            {
                var group = Group(role.GetValueOrDefault(l.From));
                var (best, top, breadth, margin) = Read(l.CardId);
                var x = Letters.GetValueOrDefault(group);
                Letters[group] = (x.Letters + 1, x.AimTruth + (truthCards.Contains(best) ? 1 : 0), x.Breadth + breadth, x.Clarity + margin, x.CutTruth, x.Checks);
            }

            // Отсечения истины в речи: «проверял эту, исчезла» / «исключаю» на истинной карте.
            foreach (var m in report.Messages)
            {
                var group = Group(role.GetValueOrDefault(m.Author));
                for (var i = 0; i < m.Cards.Count && i < m.Notes.Count; i++)
                {
                    var negative = m.Notes[i].Contains("исчезла", StringComparison.Ordinal) || m.Notes[i].StartsWith("исключ", StringComparison.Ordinal);
                    var check = m.Notes[i].StartsWith("проверял", StringComparison.Ordinal) || m.Notes[i].StartsWith("исключ", StringComparison.Ordinal);
                    if (!check) continue;
                    var x = Letters.GetValueOrDefault(group);
                    Letters[group] = (x.Letters, x.AimTruth, x.Breadth, x.Clarity, x.CutTruth + (negative && truthCards.Contains(m.Cards[i]) ? 1 : 0), x.Checks + 1);
                }
            }

            // Подозрения: когда мирные впервые всерьёз обвинили Убийцу, и точность обвинений мирных.
            var names = report.Seats.ToDictionary(s => s.Id, s => s.Name);
            var killer = role.FirstOrDefault(r => r.Value == Role.Killer).Key;
            int? first = null;
            foreach (var m in report.Messages.Where(m => !role.GetValueOrDefault(m.Author).IsKillerTeam() && role.GetValueOrDefault(m.Author) != Role.Ghost))
            {
                foreach (var a in AccusationReader.Read(m.Author, m.Text, names).Where(a => a.Strength >= .6 && a.Target != m.Author))
                {
                    Accusations++;
                    if (role.GetValueOrDefault(a.Target).IsKillerTeam()) AccusationsBlack++;
                    if (a.Target == killer) first ??= m.Round;
                }
            }
            if (first is { } f) { KillerAccusedGames++; KillerAccusedRoundSum += f; }

            // Эхо: реплики без своих улик — только «думаю, эта» на одной карте (поддакивание).
            var discussion = report.Messages.Where(m => role.GetValueOrDefault(m.Author) != Role.Ghost).ToList();
            Messages += discussion.Count;
            DiscussionRounds += discussion.Select(m => m.Round).Distinct().Count();
            bool Echoes(SandboxMessage m) => m.Cards.Count <= 1 && m.Notes.All(n => n.StartsWith("думаю", StringComparison.Ordinal))
                && !m.Text.Contains("Убийц", StringComparison.Ordinal) && !m.Text.Contains("чёрн", StringComparison.Ordinal);
            Echo += discussion.Count(Echoes);
            // Отдельно — поддакивание картой («думаю, эта»); остальное эхо — реплики без карт («Аня, согласен»).
            EchoCard += discussion.Count(m => m.Cards.Count == 1 && Echoes(m));

            // Человечность (#288): говорит ли бот новое, меняет ли мнение, насколько разнятся версии за столом.
            var rowOf = board.ToDictionary(b => b.Card, b => b.Row);
            Deductions += discussion.Count(m => m.Text.Contains("хоть один из них честный", StringComparison.Ordinal)
                || m.Text.Contains("вряд ли все они чёрные", StringComparison.Ordinal) || m.Text.Contains("так не бывает", StringComparison.Ordinal));
            foreach (var author in discussion.GroupBy(m => m.Author))
            {
                Speakers++;
                var said = new HashSet<string>();
                var version = new Dictionary<int, string>();
                foreach (var m in author)
                {
                    if (m.Cards.Any(c => !said.Contains(c))) Novel++;
                    said.UnionWith(m.Cards);
                    foreach (var (card, i) in m.Cards.Select((c, i) => (c, i)))
                    {
                        if (i >= m.Notes.Count || !m.Notes[i].StartsWith("думаю", StringComparison.Ordinal) || !rowOf.TryGetValue(card, out var row)) continue;
                        if (version.TryGetValue(row, out var before) && before != card) MindChanges++;
                        version[row] = card;
                    }
                }
            }
            foreach (var round in discussion.GroupBy(m => m.Round))
                foreach (var row in round.SelectMany(m => m.Cards.Select((c, i) => (c, i, m))).Where(x => x.i < x.m.Notes.Count && x.m.Notes[x.i].StartsWith("думаю", StringComparison.Ordinal) && rowOf.ContainsKey(x.c))
                             .GroupBy(x => rowOf[x.c]))
                {
                    RowRounds++;
                    RowVersions += row.Select(x => x.c).Distinct().Count();
                }
        }

        private static string Group(Role r) => r switch
        {
            Role.Killer => "Убийца",
            Role.Accomplice => "Сообщник",
            Role.Witness => "Свидетель",
            Role.Expert => "Эксперт",
            Role.Blackmailer => "Шантажист",
            _ => "Детектив",
        };
    }

    [Fact]
    public void Metrics_AreComputed_ForOneGame()
    {
        var tags = RealTags();
        var metrics = new Metrics();
        var (report, state) = BotSandboxRunner.Simulate("full-game", 3, Deck, tags, CancellationToken.None, SandboxOptions.Ozon(6));
        metrics.Add(report, state, tags);
        metrics.Games.Should().Be(1);
        metrics.GhostRounds.Should().BeGreaterThan(0);
        metrics.Letters.Should().ContainKey("Детектив");
        metrics.Messages.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Metrics_Report()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("BOT_METRICS_GAMES"), out var games) || games <= 0) return;
        var output = Environment.GetEnvironmentVariable("BOT_METRICS_OUT") ?? Path.Combine(Path.GetTempPath(), "bot-metrics.md");
        var variants = new List<(string Name, BotTuning Tuning)> { ("по умолчанию", BotTuning.Default) };
        foreach (var part in (Environment.GetEnvironmentVariable("BOT_METRICS_VARIANTS") ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = part.IndexOf(':');
            variants.Add(colon < 0 ? (part, BotTuning.Parse(part)) : (part[..colon].Trim(), BotTuning.Parse(part[(colon + 1)..])));
        }

        var tags = RealTags();
        var md = new StringBuilder();
        var commit = Environment.GetEnvironmentVariable("GITHUB_SHA") is { Length: >= 7 } sha ? sha[..7] : "локально";
        md.AppendLine("# Метрики игры ботов (Боты 3.0)").AppendLine();
        md.AppendLine($"Сборка {commit}, {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC. По {games} партий на состав, seed 1…{games}, одинаковые для всех вариантов.").AppendLine();
        md.AppendLine("""
            Что значат столбцы:
            - **Призрак: открыто / раунд** — среднее число открытых писем; **пустых** — раундов без открытий; **к истине / к ложной** — доля открытых писем, чьё самое похожее на поле — истинная / ложная карта; **придержал истину** — неоткрытых писем, ясно указывающих на истину, на партию.
            - **Письма по ролям:** **в истину** — доля писем, чьё самое похожее — истина; **ширина** — сколько карт поля похожи на письмо почти как лучшая (≥ 60 %); **ясность** — отрыв лучшей карты от второй; **отсёк истину** — доля «проверок» в речи, которые отвергли истинную карту.
            - **Подозрения:** **Убийцу обвинили** — доля партий, где мирный всерьёз назвал Убийцу, и средний раунд первого обвинения; **точность** — доля серьёзных обвинений мирных, попавших в чёрных.
            - **Эхо** — доля реплик без своих улик (только «думаю, эта» на одной карте или реплика без карт; в скобках — с картой); **реплик / раунд** — сколько говорят боты.
            """).AppendLine();

        foreach (var (configName, options) in Configs)
        {
            md.AppendLine($"## {configName}").AppendLine();
            md.AppendLine("| Вариант | Детективы | Ряды | Призрак: открыто / раунд | пустых | к истине / к ложной | придержал истину | Убийцу обвинили (раунд) | точность | эхо | реплик / раунд |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            var letterLines = new List<string>();
            var humanLines = new List<string>();
            foreach (var (name, tuning) in variants)
            {
                var m = new Metrics();
                using (BotTuning.Use(tuning))
                {
                    for (var seed = 1; seed <= games; seed++)
                    {
                        var (report, state) = BotSandboxRunner.Simulate("full-game", seed, Deck, tags, CancellationToken.None, options);
                        m.Add(report, state, tags);
                    }
                }
                var revealed = m.RevealTruth + m.RevealFalse;
                md.AppendLine($"| {name} | {Pct(m.DetWins, m.Games)} | {Pct(m.RowsOk, m.Rows)} | {Num(m.GhostReveals, m.GhostRounds)} | {Pct(m.GhostEmptyRounds, m.GhostRounds)} | " +
                    $"{Pct(m.RevealTruth, revealed)} / {Pct(m.RevealFalse, revealed)} | {Num(m.WithheldTruth, m.Games)} | " +
                    $"{Pct(m.KillerAccusedGames, m.Games)} ({Num(m.KillerAccusedRoundSum, m.KillerAccusedGames)}) | {Pct(m.AccusationsBlack, m.Accusations)} | " +
                    $"{Pct(m.Echo, m.Messages)} (картой {Pct(m.EchoCard, m.Messages)}) | {Num(m.Messages, m.DiscussionRounds)} |");
                humanLines.Add($"| {name} | {Pct(m.Novel, m.Messages)} | {Num(m.MindChanges, m.Speakers)} | {Num(m.RowVersions, m.RowRounds)} | {Num(m.Deductions, m.Games)} |");
                foreach (var (group, x) in m.Letters.OrderBy(g => g.Key))
                    letterLines.Add($"| {name} | {group} | {x.Letters} | {Pct(x.AimTruth, x.Letters)} | {Num(x.Breadth, x.Letters)} | {Num(x.Clarity, x.Letters, "0.00")} | {Pct(x.CutTruth, x.Checks)} из {x.Checks} |");
            }
            md.AppendLine().AppendLine("| Вариант | новое в реплике | смен мнения на игрока | разных версий на ряд за раунд | рассуждений на партию |").AppendLine("|---|---|---|---|---|");
            foreach (var line in humanLines) md.AppendLine(line);
            md.AppendLine().AppendLine("| Вариант | Роль | Писем | в истину | ширина | ясность | отсёк истину |").AppendLine("|---|---|---|---|---|---|---|");
            foreach (var line in letterLines) md.AppendLine(line);
            md.AppendLine();
        }

        md.AppendLine($"Погрешность долей ±{Math.Round(100 / Math.Sqrt(games))} п.п. на долю 50 % (на партию); варианты сравниваются на тех же seed.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, md.ToString());
    }

    private static string Pct(int n, int of) => of == 0 ? "—" : (100.0 * n / of).ToString("0", CultureInfo.InvariantCulture) + " %";

    private static string Num(double n, int of, string format = "0.0") => of == 0 ? "—" : (n / of).ToString(format, CultureInfo.InvariantCulture);
}
