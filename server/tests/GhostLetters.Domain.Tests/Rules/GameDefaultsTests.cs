using GhostLetters.Domain.Rules;

namespace GhostLetters.Domain.Tests.Rules;

public class GameDefaultsTests
{
    // Таблица «Игроки → Раунды» из правил (с. 13).
    [Theory]
    [InlineData(2, 5)]
    [InlineData(3, 5)]
    [InlineData(4, 5)]
    [InlineData(5, 4)]
    [InlineData(6, 4)]
    [InlineData(7, 4)]
    [InlineData(8, 3)]
    [InlineData(9, 3)]
    [InlineData(10, 3)]
    [InlineData(11, 2)]
    [InlineData(12, 2)]
    public void Rounds_MatchRulebook(int players, int rounds) =>
        GameDefaults.Rounds(players).Should().Be(rounds);

    [Theory]
    [InlineData(1)]
    [InlineData(13)]
    public void Rounds_OutOfRange_Throws(int players)
    {
        var act = () => GameDefaults.Rounds(players);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    [InlineData(12, 1)]
    public void LettersPerPlayer_TwoPlayersSendTwo(int players, int letters) =>
        GameDefaults.LettersPerPlayer(players).Should().Be(letters);
}
