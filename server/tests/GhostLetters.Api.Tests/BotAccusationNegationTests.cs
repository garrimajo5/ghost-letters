using GhostLetters.Infrastructure.Bots;

namespace GhostLetters.Api.Tests;

public sealed class BotAccusationNegationTests
{
    private static readonly Guid Author = Guid.NewGuid(), Bob = Guid.NewGuid(), Ann = Guid.NewGuid();
    private static readonly Dictionary<Guid, string> Names = new() { [Author] = "Федя", [Bob] = "Бот Боб", [Ann] = "Аня" };

    [Theory]
    [InlineData("Не считаю, что Боб убийца.")]
    [InlineData("Не думаю, что Аня из чёрных.")]
    [InlineData("Не верю, что Бот Боб — сообщник.")]
    [InlineData("Я не уверен, что Аня убийца.")]
    [InlineData("Я НЕ УВЕРЕНА, что Боб убийца.")]
    [InlineData("Аня не сообщник.")]
    [InlineData("Боб не из черных.")]
    [InlineData("Не подозреваю Боба. Аня не подозрительная.")]
    public void DenialAndExplicitUncertaintyAreNotAccusations(string text) =>
        AccusationReader.Read(Author, text, Names).Should().BeEmpty();

    [Theory]
    [InlineData("Боб не убийца, но Аня — сообщник.", .6)]
    [InlineData("Не считаю, что Боб из чёрных, а Аня — убийца.", 1)]
    [InlineData("Не думаю, что Боб убийца; подозреваю Аню. Аня из чёрных.", .6)]
    public void DenialDoesNotEraseAnAccusationInAnotherClause(string text, double strength)
    {
        var result = AccusationReader.Read(Author, text, Names);
        result.Should().ContainSingle().Which.Should().Be(new Accusation(Author, Ann, strength));
    }

    [Theory]
    [InlineData("Не верю Бобу. Боб, я тебе не верю.", .4)]
    [InlineData("Считаю, что Боб убийца.", 1)]
    [InlineData("Подозреваю, что Боб из чёрных.", .6)]
    public void PositiveSuspicionAndDistrustRemainReadable(string text, double strength) =>
        AccusationReader.Read(Author, text, Names).Should().ContainSingle().Which.Should().Be(new Accusation(Author, Bob, strength));
}
