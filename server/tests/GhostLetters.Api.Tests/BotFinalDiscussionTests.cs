using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

public sealed class BotFinalDiscussionTests
{
    private static readonly Guid Me = Guid.NewGuid(), Ann = Guid.NewGuid(), Ghost = Guid.NewGuid();
    private static readonly CardTags Tags = new(new Dictionary<string, HashSet<string>>
    {
        ["knife"] = ["weapon"], ["sword"] = ["weapon"], ["dagger"] = ["weapon"],
        ["rose"] = ["flower"], ["tulip"] = ["flower"], ["boat"] = ["water"], ["sea"] = ["water"], ["cat"] = ["animal"],
    });
    private static BotMind Mind => BotMind.Neutral with
    {
        Personality = new BotPersonality { Meaning = 1, Shape = 0, Color = 0, Compromise = 0, Variability = 0 },
        Names = new Dictionary<Guid, string> { [Me] = "Бот Я", [Ann] = "Аня", [Ghost] = "Призрак" },
    };
    private static PlayerView View => new(Guid.NewGuid(), 1, Phase.Discussion, 3, 3, DiscussionMode.FreeChat,
        [new(Category.Motive, ["knife", "rose"]), new(Category.Place, ["boat", "cat"])],
        [new(0, ["sword"]), new(1, ["dagger", "sea"]), new(3, ["tulip"])], 0,
        [new(Ghost, 0, true, Role.Ghost, false, 0), new(Me, 1, false, Role.Detective, false, 0), new(Ann, 2, false, null, false, 0)],
        new(Me, Role.Detective, [], []), null, 0, null, null, null, null, [], [nameof(ReadyNextRound)], null);

    [Fact]
    public void AffinityChangesWhoTheBotContacts_ZeroInfluenceKeepsSeatOrder()
    {
        var bob = Guid.NewGuid();
        var view = View with { Players = [.. View.Players, new(bob, 3, false, null, false, 0)] };
        var mind = Mind with { Names = new Dictionary<Guid, string>(Mind.Names) { [bob] = "Борис" },
            Affinities = new Dictionary<Guid, int> { [Ann] = -50, [bob] = 70 },
            Personality = Mind.Personality with { Social = new() { Influence = 1 } } };
        List<DiscussionLine> lines = [new(Me, "Моя версия", ["knife", "boat"], DateTimeOffset.UtcNow)];
        BotDiscussion.Compose(view, Tags, mind, lines, new Random(1))!.Value.Text.Should().StartWith("Борис,");
        mind = mind with { Personality = mind.Personality with { Social = new() { Influence = 0 } } };
        BotDiscussion.Compose(view, Tags, mind, lines, new Random(1))!.Value.Text.Should().StartWith("Аня,");
    }

    [Fact]
    public void FullVersionUsesEarlyClues_AndExplainsEveryRow()
    {
        var view = View;
        BotPlayer.Plan(view, Tags, Mind, new Random(1)).Should().Equal(0, 0);
        BotPlayer.Plan(view with { Hints = [view.Hints[^1]] }, Tags, Mind, new Random(1))[0].Should().Be(1);
        var line = BotDiscussion.Compose(view, Tags, Mind, [], new Random(1))!.Value;
        line.Text.Should().Contain("мотив 1").And.Contain("место 1").And.Contain("Подсказка р.0 №1").And.Contain("Подсказка р.1 №2");
        line.Cards.Should().Equal("knife", "boat");
        line.Text.Length.Should().BeLessThanOrEqualTo(ChatService.MaxTextLength);
    }

    [Fact]
    public void AsksNamedPlayer_AnswersSpecificRow_AndDoesNotAnswerSameQuestionTwice()
    {
        var now = DateTimeOffset.UtcNow;
        var own = new DiscussionLine(Me, "Моя версия", ["knife", "boat"], now);
        var ask = BotDiscussion.Compose(View, Tags, Mind, [own], new Random(1))!.Value;
        ask.Text.Should().StartWith("Аня,").And.Contain("будешь голосовать?");
        var question = new DiscussionLine(Ann, "Бот Я, за какую карту места будешь голосовать?", ["cat"], now.AddSeconds(15));
        var answer = BotDiscussion.Compose(View, Tags, Mind, [own, question], new Random(1))!.Value;
        answer.Text.Should().StartWith("Аня,").And.Contain("не согласен").And.Contain("место 1").And.Contain("Подсказка р.1 №2");
        answer.Cards[0].Should().Be("boat");
        var delayed = BotDiscussion.Compose(View, Tags, Mind,
            [question, own with { At = now.AddSeconds(20) }], new Random(1))!.Value;
        delayed.Text.Should().Contain("отвечаю про голосование", "вопрос до вступительной версии тоже ждёт ответа");
        var again = BotDiscussion.Compose(View, Tags, Mind,
            [own, question, new(Me, answer.Text, answer.Cards, now.AddSeconds(30))], new Random(1));
        again?.Text.Should().NotContain("отвечаю про голосование");
    }

    [Fact]
    public void CompromisingBotReconsidersPublicAnswer_AndPlanMatchesVote()
    {
        var mind = Mind with { Personality = Mind.Personality with { Compromise = 1 }, Opinions = [new(Ann, "rose", 1)] };
        BotPlayer.Plan(View, Tags, mind, new Random(1))[0].Should().Be(1);
        var now = DateTimeOffset.UtcNow;
        var line = BotDiscussion.Compose(View, Tags, mind,
            [new(Me, "Аня, что думаешь?", ["knife"], now), new(Ann, "Бот Я, я выбираю мотив 2.", ["rose"], now.AddSeconds(15))], new Random(1))!.Value;
        line.Text.Should().Contain("сверил твой ответ").And.Contain("совпадают").And.Contain("мотив 2");
    }

    [Fact]
    public void DirectQuestionAboutPlayerGetsSuspicionAnswer_NotCardNumber()
    {
        var now = DateTimeOffset.UtcNow;
        var line = BotDiscussion.Compose(View, Tags, Mind,
            [new(Me, "Версия", ["knife"], now), new(Ann, "Бот Я, за кого будешь голосовать?", [], now.AddSeconds(15))], new Random(1))!.Value;
        line.Text.Should().Contain("про подозрения").And.Contain("недостаточно оснований").And.NotContain("мотив 1");
    }

    [Fact]
    public void DialogueIsBounded_AndWaitsForAnswers()
    {
        var now = DateTimeOffset.UtcNow;
        List<DiscussionLine> lines = [new(Me, "Версия", [], now)];
        BotDiscussion.ShouldWait(Me, lines, now.AddSeconds(5)).Should().BeTrue();
        BotDiscussion.ShouldWait(Me, lines, now.AddMinutes(3)).Should().BeFalse();
        for (var i = 1; i < BotDiscussion.MaxMessages; i++) lines.Add(new(Me, "Ответ", [], now.AddSeconds(i)));
        BotDiscussion.Compose(View, Tags, Mind, lines, new Random(1)).Should().BeNull();
        BotDiscussion.ShouldWait(Me, lines, now.AddSeconds(10)).Should().BeFalse();
        BotDiscussion.Compose(View with { Me = View.Me! with { Role = Role.Ghost } }, Tags, Mind, [], new Random(1)).Should().BeNull();
    }

    [Fact]
    public void FinalDiscussionIsSharedAndTwiceAsLong_TurnBasedUnchanged()
    {
        var state = new GameState { Phase = Phase.Discussion, Round = 1, TotalRounds = 2,
            Settings = new GameSettings { Discussion = DiscussionMode.Radio } };
        var settings = new LobbySettings { Timers = new PhaseTimers { FreeDiscussion = 120, SpeakerTurn = 45 } };
        settings.TimerFor(state).Should().Be(TimeSpan.FromSeconds(45));
        state.Round = 2;
        state.EffectiveDiscussion.Should().Be(DiscussionMode.FreeChat);
        state.CurrentSpeaker.Should().BeNull();
        settings.TimerFor(state).Should().Be(TimeSpan.FromSeconds(240));
        (settings with { Tempo = Tempos.TurnBased, TurnHours = 12 }).TimerFor(state).Should().Be(TimeSpan.FromHours(12));
    }
}
