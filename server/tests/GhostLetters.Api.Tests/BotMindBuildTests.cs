using FluentAssertions;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

/// <summary>#298: ум бота из чата собирается одной функцией для живой партии и песочницы — проверяем, что в нём всё нужное.</summary>
public class BotMindBuildTests
{
    private static readonly Guid Me = Guid.NewGuid(), Ann = Guid.NewGuid(), Bob = Guid.NewGuid();

    [Fact]
    public void BuildMind_KeepsRoundsOwnWordsClaimsAndAccusations()
    {
        var names = new Dictionary<Guid, string> { [Me] = "Пуаро", [Ann] = "Аня", [Bob] = "Боб" };
        var board = new HashSet<string> { "knife", "boat", "cat" };
        var messages = new List<BotService.MindMessage>
        {
            new(Me, 1, "Думаю, нож.", ["knife"], ["думаю, эта"], true),
            new(Ann, 1, "Кидал вот это.", ["sword", "knife"], ["кидал эту", "проверял эту"], true),
            new(Bob, 2, "Думаю, Аня из чёрных.", ["boat"], ["думаю, эта"], true),
        };

        var mind = BotService.BuildMind(Me, new BotPersonality(), new Dictionary<Guid, PlayerHistory>(), messages, board, names);

        mind.OwnSaid.Should().ContainSingle(o => o.CardId == "knife" && o.Round == 1);
        mind.Opinions.Should().OnlyContain(o => o.Author != Me && o.Round != null);
        mind.Opinions.Should().Contain(o => o.Author == Bob && o.CardId == "boat" && o.Round == 2);
        mind.ClaimList.Should().ContainSingle(c => c.Author == Ann && c.Card == "sword" && c.Round == 1);
        mind.AccusationList.Should().Contain(a => a.Author == Bob && a.Target == Ann);
    }
}
