using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;

namespace GhostLetters.Domain.Tests.Game;

/// <summary>Доска улик: нити от подсказок и писем к картам поля, булавки, проверки — общие для всех.</summary>
public class TableTests
{
    /// <summary>Партия в обсуждении первого раунда с одной открытой подсказкой.</summary>
    private static (GameState State, string Hint) InDiscussion()
    {
        var state = TestGame.Create(players: 7);
        state.ToRound1();
        state.SendAll();
        state.Run(state.Ghost, new RevealHints([state.Mailbox[0].CardId]));
        state.KeepAll();
        state.Phase.Should().Be(Phase.Discussion);
        return (state, state.Hints.Single(h => h.Round == 1).Cards.Single());
    }

    private static TableOp Link(string hint, string target, TableStance stance = TableStance.For, string? reason = null) =>
        new(TableOpKind.Link, TableSourceKind.Hint, hint, target, stance, reason);

    [Fact]
    public void Link_FromHint_IsVisibleToEveryone_IncludingGhostAndTableScreen()
    {
        var (state, hint) = InDiscussion();
        var detective = state.WithRole(Role.Detective);
        var target = state.Board[1].Cards[2];
        var version = state.Version;

        state.Run(detective, new TablePost([Link(hint, target, reason: "  вода  ")]));

        state.Version.Should().Be(version + 1);
        foreach (var view in new[] { GameProjection.For(state, state.Ghost.Id), GameProjection.For(state, null), GameProjection.For(state, detective.Id) })
        {
            var thread = view.Table!.Threads.Should().ContainSingle().Subject;
            thread.Author.Should().Be(detective.Id);
            thread.Source.Should().Be(hint);
            thread.Target.Should().Be(target);
            thread.Stance.Should().Be(TableStance.For);
            thread.Reason.Should().Be("вода");
            thread.Round.Should().Be(1);
        }

        GameProjection.For(state, detective.Id).Table!.CanPost.Should().BeTrue();
        GameProjection.For(state, state.Ghost.Id).Table!.CanPost.Should().BeFalse("Призрак видит доску, но не пишет");
        GameProjection.For(state, null).Table!.CanPost.Should().BeFalse();
    }

    [Fact]
    public void Link_OnlyFromOpenHintOrClaimedLetter_ToBoardCard()
    {
        var (state, hint) = InDiscussion();
        var detective = state.WithRole(Role.Detective);
        var board = state.Board[0].Cards[0];
        var handCard = detective.Hand[0];

        Rejects(state, detective, new TablePost([Link(handCard, board)]), "из руки нить не тянется");
        Rejects(state, detective, new TablePost([Link(hint, handCard)]), "цель — только карта поля");
        Rejects(state, detective, new TablePost([Link(board, state.Board[1].Cards[0])]), "от карты поля к карте поля нельзя");
        Rejects(state, detective, new TablePost([new TableOp(TableOpKind.Link, TableSourceKind.Letter, "c299", board, TableStance.For)]),
            "письмо сначала надо назвать");

        state.Run(detective, new TablePost([
            new TableOp(TableOpKind.Claim, Source: "c299", Round: 1),
            new TableOp(TableOpKind.Link, TableSourceKind.Letter, "c299", board, TableStance.Against),
        ]));

        var table = GameProjection.For(state, null).Table!;
        table.Claims.Should().ContainSingle().Which.Should().Be(new LetterClaimView(detective.Id, 1, "c299"));
        table.Threads.Should().ContainSingle().Which.SourceKind.Should().Be(TableSourceKind.Letter);
    }

    [Fact]
    public void Ghost_CannotPost_AndPostIsAtomic()
    {
        var (state, hint) = InDiscussion();
        var detective = state.WithRole(Role.Detective);

        var byGhost = () => state.Run(state.Ghost, new TablePost([Link(hint, state.Board[0].Cards[0])]));
        byGhost.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.NotAllowed);

        var version = state.Version;
        var broken = () => GameEngine.Execute(state, detective.Id,
            new TablePost([Link(hint, state.Board[0].Cards[0]), new TableOp(TableOpKind.Unlink, Thread: 999)]));
        broken.Should().Throw<GameRuleException>();
        state.Version.Should().Be(version, "версия не меняется, если пост отклонён (снимок партии сервер не сохраняет)");
    }

    [Fact]
    public void SameThreadAgain_ChangesIt_StanceChangeDropsEndorsements()
    {
        var (state, hint) = InDiscussion();
        var players = state.Investigators.ToList();
        var author = players[0];
        var other = players[1];
        var target = state.Board[2].Cards[1];

        state.Run(author, new TablePost([Link(hint, target)]));
        var id = state.Table.Threads.Single().Id;
        state.Run(other, new TablePost([new TableOp(TableOpKind.Endorse, Thread: id)]));
        state.Table.Threads.Single().EndorsedBy.Should().Equal(other.Id);

        state.Run(author, new TablePost([Link(hint, target, TableStance.Against, "цвет")]));

        var thread = state.Table.Threads.Should().ContainSingle().Subject;
        thread.Id.Should().Be(id);
        thread.Stance.Should().Be(TableStance.Against);
        thread.EndorsedBy.Should().BeEmpty("нить поменяла смысл");

        var selfEndorse = () => state.Run(author, new TablePost([new TableOp(TableOpKind.Endorse, Thread: id)]));
        selfEndorse.Should().Throw<GameRuleException>();
        var foreignUnlink = () => state.Run(other, new TablePost([new TableOp(TableOpKind.Unlink, Thread: id)]));
        foreignUnlink.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.NotAllowed);

        state.Run(other, new TablePost([new TableOp(TableOpKind.Dispute, Thread: id)]));
        thread.DisputedBy.Should().Equal(other.Id);
        state.Run(author, new TablePost([new TableOp(TableOpKind.Unlink, Thread: id)]));
        state.Table.Threads.Should().BeEmpty();
    }

    [Fact]
    public void Pin_OnePerRow_CheckAndClaimReplace()
    {
        var (state, _) = InDiscussion();
        var detective = state.WithRole(Role.Detective);

        state.Run(detective, new TablePost([
            new TableOp(TableOpKind.Pin, Target: state.Board[1].Cards[0]),
            new TableOp(TableOpKind.Pin, Target: state.Board[1].Cards[3]),
            new TableOp(TableOpKind.Pin, Target: state.Board[2].Cards[4]),
            new TableOp(TableOpKind.Check, Target: state.Board[0].Cards[1]),
            new TableOp(TableOpKind.Claim, Source: "c001", Round: 1),
            new TableOp(TableOpKind.Claim, Source: "c002", Round: 1),
        ]));

        var table = GameProjection.For(state, null).Table!;
        table.Pins.Should().BeEquivalentTo([new TablePinView(detective.Id, 1, 3), new TablePinView(detective.Id, 2, 4)]);
        table.Checks.Should().ContainSingle().Which.Card.Should().Be(state.Board[0].Cards[1]);
        table.Claims.Should().ContainSingle().Which.Card.Should().Be("c002", "за раунд одно письмо — новое заменяет");

        Rejects(state, detective, new TablePost([new TableOp(TableOpKind.Claim, Source: "c003", Round: 2)]), "второй раунд ещё не начался");
        state.Run(detective, new TablePost([
            new TableOp(TableOpKind.Unpin, Target: state.Board[1].Cards[0]),
            new TableOp(TableOpKind.Uncheck, Target: state.Board[0].Cards[1]),
        ]));
        state.Table.Pins.Should().ContainSingle().Which.Row.Should().Be(2);
        state.Table.Checks.Should().BeEmpty();
    }

    [Fact]
    public void Limits_PostSize_ReasonLength_ThreadsPerPlayer()
    {
        var (state, hint) = InDiscussion();
        var detective = state.WithRole(Role.Detective);
        var board = state.Board[0].Cards[0];

        Rejects(state, detective, new TablePost([]), "пустой пост");
        Rejects(state, detective, new TablePost(Enumerable.Repeat(new TableOp(TableOpKind.Pin, Target: board), GameEngine.MaxTableOps + 1).ToList()), "слишком много");
        Rejects(state, detective, new TablePost([Link(hint, board, reason: new string('я', GameEngine.MaxReasonLength + 1))]), "длинная причина");

        // Нити от разных писем к одной карте — до лимита на игрока.
        for (var i = 0; i < GameEngine.MaxThreadsPerPlayer; i++)
        {
            state.Table.Claims.Add(new LetterClaim { Author = detective.Id, Round = 1, Card = $"x{i}" });
            state.Run(detective, new TablePost([new TableOp(TableOpKind.Link, TableSourceKind.Letter, $"x{i}", board, TableStance.For)]));
        }

        state.Table.Claims.Add(new LetterClaim { Author = detective.Id, Round = 1, Card = "extra" });
        Rejects(state, detective, new TablePost([new TableOp(TableOpKind.Link, TableSourceKind.Letter, "extra", board, TableStance.For)]), "лимит нитей");
    }

    [Fact]
    public void Phases_BoardClosedAtNight_PinsOnlyInVoting()
    {
        var state = TestGame.Create(players: 7);
        state.AckAll();
        var detective = state.WithRole(Role.Detective);
        GameProjection.For(state, detective.Id).Table!.CanPost.Should().BeFalse("ночью доска закрыта");
        Rejects(state, detective, new TablePost([new TableOp(TableOpKind.Pin, Target: state.Board[0].Cards[0])]), "ночь");

        var voting = FinaleGame.ToVoting(players: 7);
        var voter = voting.WithRole(Role.Detective);
        var view = GameProjection.For(voting, voter.Id).Table!;
        view.CanPost.Should().BeTrue();
        view.PinsOnly.Should().BeTrue();
        voting.Run(voter, new TablePost([new TableOp(TableOpKind.Pin, Target: voting.Board[0].Cards[2])]));
        voting.Table.Pins.Should().ContainSingle();
        Rejects(voting, voter, new TablePost([new TableOp(TableOpKind.Check, Target: voting.Board[0].Cards[2])]), "в голосовании только булавки");
    }

    [Fact]
    public void TableCommand_IsNotInAllowedCommands_SoItNeverLooksLikeYourTurn()
    {
        var (state, _) = InDiscussion();
        GameProjection.For(state, state.WithRole(Role.Detective).Id).AllowedCommands.Should().NotContain(nameof(TablePost));
    }

    private static void Rejects(GameState state, PlayerState actor, GameCommand command, string because)
    {
        var act = () => state.Run(actor, command);
        act.Should().Throw<GameRuleException>(because);
    }
}
