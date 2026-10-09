using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

public sealed class BotPublicRoleClaimTests
{
    [Theory]
    [InlineData("Я Свидетель.", Role.Witness)]
    [InlineData("Я — эксперт!", Role.Expert)]
    [InlineData("Слушайте. Я свидетель. Убийца — Боб.", Role.Witness)]
    public void ReadsExplicitSelfDeclaration(string text, Role role)
    {
        var author = Guid.NewGuid();
        PublicRoleClaims.Read([(author, text)])[author].Should().Be(role);
    }

    [Theory]
    [InlineData("Я не свидетель.")]
    [InlineData("Я свидетель?")]
    [InlineData("Если я свидетель, что дальше?")]
    [InlineData("Аня говорит: я свидетель.")]
    [InlineData("«Я свидетель».")]
    [InlineData("Я свидетель, шучу.")]
    [InlineData("Он эксперт.")]
    [InlineData("Аня сказала: «Слушайте. Я свидетель.»")]
    [InlineData("Она написала: «\nЯ свидетель.\n»")]
    [InlineData("Цитирую: \"Слушайте. Я эксперт.\"")]
    [InlineData("Мне сказали: “Слушайте. Я эксперт.”")]
    [InlineData("Он сказал «Стоп. Я свидетель.")]
    public void DoesNotTreatQuestionsQuotesOrConditionsAsClaims(string text) =>
        PublicRoleClaims.Read([(Guid.NewGuid(), text)]).Should().BeEmpty();

    [Fact]
    public void DenialRetractsOnlyMatchingRole_AndLatestClaimReplacesOld()
    {
        var author = Guid.NewGuid();
        PublicRoleClaims.Read([(author, "Я свидетель."), (author, "Я не свидетель.")]).Should().BeEmpty();
        PublicRoleClaims.Read([(author, "Я свидетель."), (author, "Я эксперт."), (author, "Я не свидетель.")])
            [author].Should().Be(Role.Expert);
    }

    [Fact]
    public void QuotedDenialDoesNotRetractAuthorsOwnClaim()
    {
        var author = Guid.NewGuid();
        PublicRoleClaims.Read([(author, "Я свидетель."), (author, "Он ответил: «Стоп. Я не свидетель.»")])
            [author].Should().Be(Role.Witness);
    }

    [Fact]
    public void OwnStatementAfterQuoteIsStillRead()
    {
        var author = Guid.NewGuid();
        PublicRoleClaims.Read([(author, "Он сказал «я свидетель». Я эксперт.")])[author].Should().Be(Role.Expert);
    }

    [Theory]
    [InlineData(Role.Witness)]
    [InlineData(Role.Expert)]
    public void HunterActsOnPublicClaim_EvenWhenItIsABluff(Role claimed)
    {
        var ids = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();
        var state = GameEngine.Create(Guid.NewGuid(), ids, new GameSettings(),
            Enumerable.Range(0, 200).Select(i => $"card_{i}").ToList(), 42);
        state.Truth = state.Board.Select(_ => 0).ToList();
        state.Phase = Phase.Hunt; state.Solved = true;
        var killer = state.Players.Single(p => p.Role == Role.Killer);
        var liar = state.Players.First(p => p.Role == Role.Detective);
        var claims = PublicRoleClaims.Read([(liar.Id, claimed == Role.Witness ? "Я свидетель." : "Я эксперт.")]);
        var mind = BotMind.Neutral with { RoleClaims = claims };
        var view = GameProjection.For(state, killer.Id);
        view.Players.Single(p => p.Id == liar.Id).KnownRole.Should().BeNull();
        var command = BotPlayer.Decide(view, new Random(42), CardTags.Empty, mind).Should().BeOfType<HuntPick>().Subject;
        command.Target.Should().Be(liar.Id);
        command.Guess.Should().Be(claimed);
        GameEngine.Execute(state, killer.Id, command);
        state.Hunt!.Success.Should().BeFalse("публичному блефу можно поверить, тайная роль не подглядывается");
    }

    [Fact]
    public void ClaimOfRoleAbsentFromHuntDoesNotChangeDecision()
    {
        var ids = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToList();
        var state = GameEngine.Create(Guid.NewGuid(), ids, new GameSettings(),
            Enumerable.Range(0, 200).Select(i => $"card_{i}").ToList(), 42);
        state.Truth = state.Board.Select(_ => 0).ToList();
        state.Phase = Phase.Hunt; state.Solved = true;
        var killer = state.Players.Single(p => p.Role == Role.Killer);
        var liar = state.Players.First(p => p.Role == Role.Detective);
        var view = GameProjection.For(state, killer.Id);
        view.HuntRoles.Should().NotContain(Role.Expert);
        var mind = BotMind.Neutral with { RoleClaims = new Dictionary<Guid, Role> { [liar.Id] = Role.Expert } };
        BotPlayer.Decide(view, new Random(42), CardTags.Empty, mind).Should().Be(
            BotPlayer.Decide(view, new Random(42), CardTags.Empty, BotMind.Neutral));
    }
}
