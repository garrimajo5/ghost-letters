using System.Text.Json;
using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;
using Xunit.Abstractions;

namespace GhostLetters.Api.Tests;

/// <summary>
/// Бенчмарк по живой партии: подсказки человека-Призрака и чат ботов той партии. Каждый мирный бот в конце партии
/// говорит, какую карту в каждом ряду считает истиной; сравниваем с настоящей истиной — с общей теорией и без неё.
/// </summary>
public sealed class LiveGameBenchmarkTests(ITestOutputHelper output)
{
    public sealed record Fixture(string Source, List<Row> Board, List<int> Truth, List<Hint> Hints, List<Player> Players, List<Message> Messages);
    public sealed record Row(string Category, List<string> Cards);
    public sealed record Hint(int Round, List<string> Cards);
    public sealed record Player(Guid Id, int Seat, string Role, List<Letter> Letters, List<string> Hand);
    public sealed record Letter(int Round, string CardId, bool? Revealed);
    public sealed record Message(Guid Author, int Round, string? Text, List<string> Cards, List<string> Notes, bool Public);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Fixture Load(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "server", "tests", "GhostLetters.Api.Tests", "Benchmarks"))) dir = dir.Parent;
        var path = Path.Combine(dir!.FullName, "server", "tests", "GhostLetters.Api.Tests", "Benchmarks", name);
        return JsonSerializer.Deserialize<Fixture>(File.ReadAllText(path), Json)!;
    }

    private static CardTags RealTags()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "client", "assets", "cards", "tags.json"))) dir = dir.Parent;
        var cards = Path.Combine(dir!.FullName, "client", "assets", "cards");
        return CardTags.Parse(File.ReadAllText(Path.Combine(cards, "tags.json")))
            .WithDetails(File.ReadAllText(Path.Combine(cards, "details.json")))
            .WithAssociations(File.ReadAllText(Path.Combine(cards, "associations.json")));
    }

    /// <summary>Сколько рядов угадали мирные боты (сумма по ботам) и что каждый считает истиной.</summary>
    public static (int Correct, int Total, List<string> Lines) Score(Fixture fx, CardTags tags, int round)
    {
        var board = fx.Board.Select(r => new BoardRowView(Enum.Parse<Category>(r.Category), r.Cards)).ToList();
        var boardSet = fx.Board.SelectMany(r => r.Cards).ToHashSet();
        var hints = fx.Hints.Where(h => h.Round <= round).Select(h => new HintGroupView(h.Round, h.Cards)).ToList();
        var names = fx.Players.ToDictionary(p => p.Id, p => $"Игрок {p.Seat}");
        var players = fx.Players.Select(p => new PlayerInfoView(p.Id, p.Seat, p.Role == "Ghost", p.Role == "Ghost" ? Role.Ghost : null, false, 5)).ToList();
        var messages = fx.Messages.Where(m => m.Round <= round)
            .Select(m => new BotService.MindMessage(m.Author, m.Round, m.Text, m.Cards, m.Notes, m.Public)).ToList();
        var (correct, total, lines) = (0, 0, new List<string>());
        foreach (var p in fx.Players.Where(p => p.Role is "Detective" or "Witness" or "Expert"))
        {
            var role = Enum.Parse<Role>(p.Role);
            var me = new MeView(p.Id, role, p.Hand, p.Letters.Where(l => l.Round <= round).Select(l => new MyLetterView(l.Round, l.CardId, l.Revealed)).ToList());
            var view = new PlayerView(Guid.NewGuid(), 1, Phase.Discussion, round, 4, DiscussionMode.Radio, board, hints, 0,
                players, me, null, 0, null, null, null, null, [], [], null);
            var mind = BotService.BuildMind(p.Id, BotPersonality.Default, new Dictionary<Guid, PlayerHistory>(), messages, boardSet, names);
            var journal = BotPlayer.Journal(view, tags, mind)!;
            var picks = fx.Board.Select((r, i) => r.Cards.MaxBy(c => journal.Truth[c])!).ToList();
            var ok = picks.Select((c, i) => fx.Board[i].Cards.IndexOf(c) == fx.Truth[i]).ToList();
            correct += ok.Count(x => x);
            total += ok.Count;
            lines.Add($"{p.Role} {p.Seat}: " + string.Join(", ", picks.Select((c, i) => $"{fx.Board[i].Category} {fx.Board[i].Cards.IndexOf(c) + 1}{(ok[i] ? "✓" : "✗")} ({journal.Truth[c]:0.00})")));
        }
        return (correct, total, lines);
    }

    [Fact]
    public void Game20261112_HumanGhost_TheoryReadsHintsBetter()
    {
        var fx = Load("game-2026-11-12.json");
        var tags = RealTags();
        var results = new Dictionary<string, (int Correct, int Total)>();
        foreach (var (name, tuning) in new[] { ("без теории", BotTuning.Default with { HintTheory = false }), ("с теорией", BotTuning.Default with { HintTheory = true }) })
        {
            using var _ = BotTuning.Use(tuning);
            for (var round = 1; round <= 4; round++)
            {
                var (c, t, lines) = Score(fx, tags, round);
                output.WriteLine($"{name}, раунд {round}: {c}/{t}");
                if (round == 4) foreach (var l in lines) output.WriteLine("  " + l);
                if (round == 4) results[name] = (c, t);
            }
        }
        using (BotTuning.Use(BotTuning.Default with { HintTheory = true }))
        {
            var view = new PlayerView(Guid.NewGuid(), 1, Phase.Discussion, 4, 4, DiscussionMode.Radio,
                fx.Board.Select(r => new BoardRowView(Enum.Parse<Category>(r.Category), r.Cards)).ToList(),
                fx.Hints.Select(h => new HintGroupView(h.Round, h.Cards)).ToList(), 0,
                fx.Players.Select(p => new PlayerInfoView(p.Id, p.Seat, p.Role == "Ghost", null, false, 5)).ToList(),
                new MeView(fx.Players[2].Id, Role.Detective, [], []), null, 0, null, null, null, null, [], [], null);
            var theory = BotPlayer.Theory(view, tags, null)!;
            output.WriteLine("Лучшая теория без чата: " + string.Join(", ", theory.Best.Select((c, i) => $"{fx.Board[i].Category} {fx.Board[i].Cards.IndexOf(c) + 1}")));
            output.WriteLine("Не объяснены: " + string.Join(", ", theory.Unexplained));
        }
        results["с теорией"].Correct.Should().BeGreaterThanOrEqualTo(results["без теории"].Correct, "общая теория не должна читать подсказки хуже");
    }
}
