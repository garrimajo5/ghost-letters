using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

/// <summary>
/// Партии одних ботов на настоящих картах и тегах: с тегами детективы должны угадывать
/// заметно больше рядов, чем «вслепую» (без тегов боты выбирают почти наугад).
/// </summary>
public sealed class BotSimulationTests
{
    private static CardTags RealTags()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "client", "assets", "cards", "tags.json")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("tags.json лежит в client/assets/cards репозитория");
        return CardTags.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "client", "assets", "cards", "tags.json")));
    }

    /// <summary>Доля угаданных рядов в серии партий 6 ботов (Призрак, Убийца, Сообщник, детективы).</summary>
    private static double RowAccuracy(CardTags tags, int games)
    {
        var deck = Enumerable.Range(1, 758).Select(i => $"orig_{i:0000}").ToList();
        int correct = 0, total = 0;
        for (var g = 0; g < games; g++)
        {
            var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();
            var state = GameEngine.Create(Guid.NewGuid(), ids, new GameSettings { Rounds = 4 }, deck, 1000 + g);
            var rng = new Random(g);
            for (var step = 0; step < 3000 && state.Phase != Phase.Finished; step++)
            {
                var moved = false;
                foreach (var p in state.Players.OrderBy(_ => rng.Next()))
                {
                    if (BotPlayer.Decide(GameProjection.For(state, p.Id), rng, tags) is not { } command)
                    {
                        continue;
                    }

                    try
                    {
                        GameEngine.Execute(state, p.Id, command);
                    }
                    catch (GameRuleException) when (command is HuntPick hunt)
                    {
                        GameEngine.Execute(state, p.Id, hunt with { Guess = hunt.Guess == Role.Expert ? Role.Witness : Role.Expert });
                    }

                    moved = true;
                    break;
                }

                moved.Should().BeTrue($"в фазе {state.Phase} кто-то из ботов должен ходить");
            }

            state.Phase.Should().Be(Phase.Finished);
            var rows = state.VoteOutcomes.Where(o => o.Kind == VoteStageKind.Row).ToList();
            correct += rows.Count(o => o.Correct);
            total += rows.Count;
        }

        return correct / (double)total;
    }

    [Fact]
    public void WithTags_DetectivesSolveMoreRowsThanBlind()
    {
        var tags = RealTags();
        tags.Count.Should().BeGreaterThan(700);

        var smart = RowAccuracy(tags, 25);
        var blind = RowAccuracy(CardTags.Empty, 25);

        smart.Should().BeGreaterThan(blind + 0.1, $"с тегами {smart:P0}, вслепую {blind:P0}");
    }
}
