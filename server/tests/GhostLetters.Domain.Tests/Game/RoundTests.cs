using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;

namespace GhostLetters.Domain.Tests.Game;

public class RoundTests
{
    [Fact]
    public void Mailbox_FirstInvestigatorTakesRadio_GhostDoesNot()
    {
        var state = TestGame.Create();
        state.ToRound1();
        var detective = state.WithRole(Role.Detective);

        state.Run(state.Ghost, new SendLetter([state.Ghost.Hand[0]]));
        state.RadioHolder.Should().BeNull();

        state.Run(detective, new SendLetter([detective.Hand[0]]));
        state.RadioHolder.Should().Be(detective.Id);
        detective.Hand.Should().HaveCount(4);
    }

    [Fact]
    public void Mailbox_AfterEveryoneSent_GhostPicks()
    {
        var state = TestGame.Create(players: 6);
        state.ToRound1();

        state.SendAll();

        state.Phase.Should().Be(Phase.GhostPick);
        state.Mailbox.Should().HaveCount(6);
        state.Letters.Should().HaveCount(6).And.OnlyContain(l => l.Round == 1);
    }

    [Fact]
    public void Mailbox_SecondLetterInSameRound_Throws()
    {
        var state = TestGame.Create();
        state.ToRound1();
        var p = state.WithRole(Role.Detective);
        state.Run(p, new SendLetter([p.Hand[0]]));

        var act = () => state.Run(p, new SendLetter([p.Hand[0]]));

        act.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.NotAllowed);
    }

    [Fact]
    public void Mailbox_CardNotInHand_Throws()
    {
        var state = TestGame.Create();
        state.ToRound1();

        var act = () => state.Run(state.WithRole(Role.Detective), new SendLetter([state.Board[0].Cards[0]]));

        act.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.Validation);
    }

    [Fact]
    public void Mailbox_TwoPlayers_SendTwoCardsEach()
    {
        var state = TestGame.Create(players: 2);
        state.ToRound1();
        var detective = state.WithRole(Role.Detective);

        var single = () => state.Run(detective, new SendLetter([detective.Hand[0]]));
        single.Should().Throw<GameRuleException>();

        state.SendAll();

        state.Mailbox.Should().HaveCount(4);
        state.Phase.Should().Be(Phase.GhostPick);
    }

    [Fact]
    public void Reveal_ChosenBecomeHints_RestVanish()
    {
        var state = TestGame.Create(players: 5);
        state.ToRound1();
        state.SendAll();
        var chosen = state.Mailbox.Take(2).Select(l => l.CardId).ToList();

        state.Run(state.Ghost, new RevealHints(chosen));

        state.Hints.Single(h => h.Round == 1).Cards.Should().BeEquivalentTo(chosen);
        state.Vanished.Should().HaveCount(3);
        state.Mailbox.Should().BeEmpty();
        state.Letters.Where(l => l.Revealed).Select(l => l.CardId).Should().BeEquivalentTo(chosen);
        state.Phase.Should().Be(Phase.Refill);
    }

    [Fact]
    public void Reveal_Nothing_LeavesEmptyHintGroup()
    {
        var state = TestGame.Create(players: 5);
        state.ToRound1();
        state.SendAll();

        state.Run(state.Ghost, new RevealHints([]));

        state.Hints.Single(h => h.Round == 1).Cards.Should().BeEmpty();
        state.Vanished.Should().HaveCount(5);
    }

    [Fact]
    public void Reveal_CardNotInMailbox_Throws()
    {
        var state = TestGame.Create();
        state.ToRound1();
        state.SendAll();

        var act = () => state.Run(state.Ghost, new RevealHints([state.Board[0].Cards[0]]));

        act.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.Validation);
    }

    [Fact]
    public void Discard_DuringGhostPick_RefillsAndCounts()
    {
        var state = TestGame.Create(players: 4);
        state.ToRound1();
        state.SendAll();
        var detective = state.WithRole(Role.Detective);
        var dropped = detective.Hand[0];

        state.Run(detective, new Discard(dropped));

        detective.Hand.Should().HaveCount(5).And.NotContain(dropped);
        state.DiscardPile.Should().Contain(dropped);
        state.Phase.Should().Be(Phase.GhostPick);
    }

    [Fact]
    public void Discard_IsRememberedOnlyForTheDiscarder()
    {
        var state = TestGame.Create(players: 4);
        state.ToRound1();
        state.SendAll();
        var detective = state.WithRole(Role.Detective);
        var other = state.Players.First(p => p.Id != detective.Id && p.Id != state.Ghost.Id);
        var dropped = detective.Hand[0];

        state.Run(detective, new Discard(dropped));

        GameProjection.For(state, detective.Id).Me!.Discarded.Should().Equal(dropped);
        GameProjection.For(state, other.Id).Me!.Discarded.Should().BeEmpty();
    }

    [Fact]
    public void Discussion_StartsWhenHintsOpenAndEveryoneDecided()
    {
        var state = TestGame.Create(players: 4);
        state.ToRound1();
        state.SendAll();
        state.KeepAll();
        state.Phase.Should().Be(Phase.GhostPick);

        state.Run(state.Ghost, new RevealHints([]));

        state.Phase.Should().Be(Phase.Discussion);
        state.Players.Should().OnlyContain(p => p.Hand.Count == 5);
    }

    [Fact]
    public void Radio_OrderStartsWithHolder_SkipsGhost_AndRoundAdvances()
    {
        var state = TestGame.Create(players: 7);
        state.ToRound1();
        var last = state.Investigators.Last();
        state.SendAll(first: last);
        state.Run(state.Ghost, new RevealHints([]));
        state.KeepAll();

        state.Phase.Should().Be(Phase.Discussion);
        state.CurrentSpeaker.Should().Be(last.Id);
        state.SpeakingOrder.Should().HaveCount(6).And.NotContain(state.Ghost.Id);

        var notSpeaker = state.Investigators.First(p => p.Id != last.Id);
        var act = () => state.Run(notSpeaker, new EndTurn());
        act.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.NotYourTurn);

        state.TalkThrough();

        state.Round.Should().Be(2);
        state.Phase.Should().Be(Phase.Mailbox);
        state.RadioHolder.Should().BeNull();
    }

    [Fact]
    public void Radio_GiveFloorAndRaiseHand()
    {
        var state = TestGame.Create(players: 5);
        state.ToRound1();
        state.SendAll();
        state.Run(state.Ghost, new RevealHints([]));
        state.KeepAll();
        var speaker = state.Player(state.CurrentSpeaker!.Value);
        var other = state.Investigators.First(p => p.Id != speaker.Id);

        state.Run(other, new RaiseHand(true));
        state.RaisedHands.Should().Contain(other.Id);

        state.Run(speaker, new GiveFloor(other.Id));
        state.FloorGrantedTo.Should().Be(other.Id);
        state.RaisedHands.Should().NotContain(other.Id);

        var toGhost = () => state.Run(speaker, new GiveFloor(state.Ghost.Id));
        toGhost.Should().Throw<GameRuleException>();

        var ghostHand = () => state.Run(state.Ghost, new RaiseHand(true));
        ghostHand.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.NotAllowed);
    }

    [Fact]
    public void FreeChat_AllReady_NextRound()
    {
        var state = TestGame.Create(players: 4, settings: new GameSettings { Discussion = DiscussionMode.FreeChat });
        state.ToRound1();
        state.SendAll();
        state.Run(state.Ghost, new RevealHints([]));
        state.KeepAll();

        var endTurn = () => state.Run(state.Investigators.First(), new EndTurn());
        endTurn.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.NotAllowed);

        foreach (var p in state.Investigators.ToList())
        {
            state.Run(p, new ReadyNextRound());
        }

        state.Round.Should().Be(2);
        state.Phase.Should().Be(Phase.Mailbox);
    }

    [Fact]
    public void LastRound_LeadsToVoting()
    {
        var state = TestGame.Create(players: 7, settings: new GameSettings { Rounds = 2 });
        state.ToRound1();

        state.PlayRound();
        state.PlayRound();

        state.Phase.Should().Be(Phase.Voting);
        state.Round.Should().Be(2);
        state.Hints.Select(h => h.Round).Should().Equal(0, 1, 2);
    }

    [Fact]
    public void Deck_RunsOut_DiscardPileIsReshuffled()
    {
        var settings = new GameSettings();
        var deck = TestGame.Deck.Take(settings.MinDeckSize(4)).ToList();
        var state = TestGame.Create(players: 4, settings: settings, deck: deck);
        state.ToRound1();
        state.SendAll();

        foreach (var p in state.Players.OrderBy(x => x.Seat).ToList())
        {
            state.Run(p, new Discard(p.Hand[0]));
        }

        state.Players.Should().OnlyContain(p => p.Hand.Count == 5);
        state.Deck.Should().BeEmpty();
        state.DiscardPile.Should().BeEmpty();
    }

    [Fact]
    public void Timeouts_DriveWholeGameToVoting()
    {
        var state = TestGame.Create(players: 6);
        var guard = 0;

        while (state.Phase != Phase.Voting && guard++ < 200)
        {
            GameEngine.Timeout(state);
        }

        state.Phase.Should().Be(Phase.Voting);
        state.Truth.Should().HaveCount(4);
        state.Hints.Should().HaveCount(state.TotalRounds + 1);
        state.Letters.Should().HaveCount(6 * state.TotalRounds);
        state.Players.Should().OnlyContain(p => p.Hand.Count == 5);
        state.Vanished.Should().HaveCount(6 * state.TotalRounds);
    }

    [Fact]
    public void ChangeRounds_NotBelowCurrent_NotInFinale()
    {
        var state = TestGame.Create(players: 7, settings: new GameSettings { Rounds = 4 });
        state.ToRound1();
        state.PlayRound();
        state.Round.Should().Be(2);

        var tooFew = () => GameEngine.ChangeRounds(state, 1);
        tooFew.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.Validation);

        var version = state.Version;
        GameEngine.ChangeRounds(state, 2).Should().ContainSingle(e => e.Type == "RoundsChanged");
        state.TotalRounds.Should().Be(2);
        state.Version.Should().Be(version + 1);

        state.PlayRound();
        state.Phase.Should().Be(Phase.Voting, "раунд 2 стал последним");
        var inFinale = () => GameEngine.ChangeRounds(state, 3);
        inFinale.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.NotAllowed);
    }

    [Fact]
    public void ChangeDiscussion_BetweenDiscussions_TakesEffectNextTime()
    {
        var state = TestGame.Create(players: 5, settings: new GameSettings { Discussion = DiscussionMode.Radio });
        state.ToRound1();

        GameEngine.ChangeDiscussion(state, DiscussionMode.FreeChat).Should().ContainSingle(e => e.Type == "DiscussionChanged");
        state.Settings.Discussion.Should().Be(DiscussionMode.FreeChat);
        GameEngine.ChangeDiscussion(state, DiscussionMode.FreeChat).Should().BeEmpty("тот же режим — ничего не меняется");

        state.SendAll();
        state.Run(state.Ghost, new RevealHints([state.Mailbox[0].CardId]));
        state.KeepAll();
        state.Phase.Should().Be(Phase.Discussion);

        // Свободное обсуждение: рации нет, раунд идёт дальше, когда все готовы.
        var speak = () => state.Run(state.Players.First(p => p.Role != Role.Ghost), new EndTurn());
        speak.Should().Throw<GameRuleException>();
        var during = () => GameEngine.ChangeDiscussion(state, DiscussionMode.Radio);
        during.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.NotAllowed);

        foreach (var p in state.Players.Where(p => p.Role != Role.Ghost).ToList())
        {
            state.Run(p, new ReadyNextRound());
        }

        state.Round.Should().Be(2);
    }
}
