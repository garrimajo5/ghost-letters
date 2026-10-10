using System.Globalization;
using System.Text;
using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

/// <summary>
/// Баланс партий одних ботов в песочнице на настоящих картах и разметке.
/// Обычный прогон — короткая проверка, что партии 4–8 игроков доигрываются. Полная серия с отчётом
/// (винрейт сторон, ролей и характеров) — только с BOT_BALANCE_GAMES=N; отчёт пишется в BOT_BALANCE_OUT.
/// </summary>
public sealed class BotBalanceTests
{
    private sealed record Config(string Name, SandboxOptions Options);

    private static readonly Config[] Configs =
    [
        new("4 игрока", new SandboxOptions(4, null, null, false)),
        new("5 игроков", new SandboxOptions(5, null, null, false)),
        new("6 игроков", new SandboxOptions(6, null, null, false)),
        new("6 игроков, +1 Сообщник", new SandboxOptions(6, null, new RoleOptions(ExtraAccomplices: 1), false)),
        new("7 игроков (Сообщник, Свидетель)", new SandboxOptions(7, null, null, false)),
        new("8 игроков, +Шантажист", new SandboxOptions(8, null, new RoleOptions(UseBlackmailer: true), false)),
    ];

    private static readonly IReadOnlyList<string> Deck = Enumerable.Range(1, 758).Select(i => $"orig_{i:0000}").ToList();

    private static CardTags RealTags()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "client", "assets", "cards", "tags.json"))) dir = dir.Parent;
        dir.Should().NotBeNull("tags.json лежит в client/assets/cards репозитория");
        return CardTags.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "client", "assets", "cards", "tags.json")));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(8)]
    public void Bots_FinishGames_OfEverySize(int players)
    {
        var tags = RealTags();
        var options = new SandboxOptions(players, null, players == 8 ? new RoleOptions(UseBlackmailer: true) : null, false);
        for (var seed = 1; seed <= 2; seed++)
        {
            var (report, state) = BotSandboxRunner.Simulate("full-game", seed, Deck, tags, CancellationToken.None, options);
            report.Status.Should().Be("completed", $"{players} игроков, seed {seed}: {report.Error}");
            state.Result.Should().NotBeNull();
            report.Frames.Should().BeEmpty("серия баланса не хранит снимки шагов");
            state.Players.Should().HaveCount(players);
        }
    }

    [Fact]
    public void Balance_Report()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("BOT_BALANCE_GAMES"), out var games) || games <= 0) return;
        var output = Environment.GetEnvironmentVariable("BOT_BALANCE_OUT") ?? Path.Combine(Path.GetTempPath(), "bot-balance.md");
        var tags = RealTags();
        var md = new StringBuilder();
        var commit = Environment.GetEnvironmentVariable("GITHUB_SHA") is { Length: >= 7 } sha ? sha[..7] : "локально";
        md.AppendLine("# Баланс ботов в песочнице").AppendLine();
        md.AppendLine($"Сборка {commit}, {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC. По {games} партий на состав, seed 1…{games}, свободное обсуждение, раунды как в лобби по умолчанию.");
        md.AppendLine("Шесть характеров рассаживаются по кругу (место i — характер i по списку), роли раздаёт движок по seed. Боты не используют доску улик (её нет в песочнице).").AppendLine();

        var roleWins = new Dictionary<Role, (int Games, int Wins)>();
        var presetWins = new Dictionary<string, (int Games, int Wins, int DetGames, int DetWins, int KillGames, int KillWins)>();
        md.AppendLine("## Исход по составам").AppendLine();
        md.AppendLine("| Состав | Партий | Детективы | Убийца | Шантажист | Никто | Ряды угаданы | Убийца пойман | Охота удалась | Сбои | мс / партия |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var config in Configs)
        {
            var sides = new Dictionary<WinningSide, int>();
            int correct = 0, rows = 0, caught = 0, hunts = 0, huntOk = 0, failed = 0, finished = 0;
            long ms = 0;
            var failures = new List<string>();
            for (var seed = 1; seed <= games; seed++)
            {
                var (report, state) = BotSandboxRunner.Simulate("full-game", seed, Deck, tags, CancellationToken.None, config.Options);
                ms += report.ElapsedMs;
                if (state.Result is not { } result)
                {
                    failed++;
                    if (failures.Count < 5) failures.Add($"seed {seed}: {report.Status} — {report.Error}");
                    continue;
                }

                finished++;
                sides[result.Side] = sides.GetValueOrDefault(result.Side) + 1;
                correct += report.CorrectRows;
                rows += report.TotalRows;
                if (result.KillerCaught) caught++;
                if (state.Hunt is { } hunt) { hunts++; if (hunt.Success) huntOk++; }
                var presets = report.Seats.ToDictionary(s => s.Id, s => s.Name.Split(' ')[1]);
                foreach (var p in state.Players)
                {
                    var won = result.Winners.Contains(p.Id);
                    var r = roleWins.GetValueOrDefault(p.Role);
                    roleWins[p.Role] = (r.Games + 1, r.Wins + (won ? 1 : 0));
                    if (p.Role == Role.Ghost) continue;
                    var name = presets[p.Id];
                    var x = presetWins.GetValueOrDefault(name);
                    var killer = p.Role.IsKillerTeam();
                    presetWins[name] = (x.Games + 1, x.Wins + (won ? 1 : 0),
                        x.DetGames + (killer ? 0 : 1), x.DetWins + (!killer && won ? 1 : 0),
                        x.KillGames + (killer ? 1 : 0), x.KillWins + (killer && won ? 1 : 0));
                }
            }

            string Share(int n) => finished == 0 ? "—" : Pct(n, finished);
            md.AppendLine($"| {config.Name} | {games} | {Share(sides.GetValueOrDefault(WinningSide.Detectives))} | " +
                $"{Share(sides.GetValueOrDefault(WinningSide.Killer))} | {Share(sides.GetValueOrDefault(WinningSide.Blackmailer))} | " +
                $"{Share(sides.GetValueOrDefault(WinningSide.Nobody))} | {(rows == 0 ? "—" : Pct(correct, rows))} | {Share(caught)} | " +
                $"{(hunts == 0 ? "—" : $"{Pct(huntOk, hunts)} из {hunts}")} | {failed} | {ms / Math.Max(1, games)} |");
            foreach (var f in failures) md.AppendLine($"|  ↳ сбой: {f.Replace("|", "/")} |||||||||||");
        }

        md.AppendLine().AppendLine("Погрешность при 100 партиях — около ±10 п.п. на долю 50 %.").AppendLine();
        md.AppendLine("## Победы по ролям (все составы вместе)").AppendLine();
        md.AppendLine("| Роль | В партиях | Побед |").AppendLine("|---|---|---|");
        foreach (var (role, (g, w)) in roleWins.OrderBy(r => r.Key)) md.AppendLine($"| {role} | {g} | {Pct(w, g)} |");

        md.AppendLine().AppendLine("## Победы по характерам (без Призрака)").AppendLine();
        md.AppendLine("| Характер | Партий | Побед | За детективов | За команду Убийцы |").AppendLine("|---|---|---|---|---|");
        foreach (var (name, x) in presetWins.OrderByDescending(p => p.Value.Wins / (double)Math.Max(1, p.Value.Games)))
            md.AppendLine($"| {name} | {x.Games} | {Pct(x.Wins, x.Games)} | {Pct(x.DetWins, x.DetGames)} из {x.DetGames} | {Pct(x.KillWins, x.KillGames)} из {x.KillGames} |");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, md.ToString());
    }

    private static string Pct(int n, int of) =>
        of == 0 ? "—" : (100.0 * n / of).ToString("0", CultureInfo.InvariantCulture) + " %";
}
