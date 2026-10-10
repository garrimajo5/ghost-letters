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
    public void AffinityCannotOverrideKnownKillerRole()
    {
        var bob = Guid.NewGuid();
        var view = View with { Phase = Phase.Voting, AllowedCommands = [nameof(CastVote)],
            Players = [new(Ghost, 0, true, Role.Ghost, false, 0), new(Me, 1, false, Role.Witness, false, 0),
                new(Ann, 2, false, Role.Killer, false, 0), new(bob, 3, false, null, false, 0)],
            Me = View.Me! with { Role = Role.Witness },
            Finale = new(new(2, VoteStageKind.Killer, -1, 1, [], [Ann, bob]), 3, null, [], [], [], null, null, null, [], []) };
        var mind = Mind with { Affinities = new Dictionary<Guid, int> { [Ann] = 100, [bob] = -100 },
            Personality = Mind.Personality with { Social = new() { Influence = 1 } } };
        var vote = (CastVote)BotPlayer.Decide(view, new Random(1), Tags, mind)!;
        vote.Suspect.Should().Be(Ann);
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

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void SummaryExplainsWhetherPersonalityUsesVanishedLetters(double negative)
    {
        var view = View with { Me = View.Me! with { Letters = [new(2, "sword", false)] } };
        var mind = Mind with { Personality = Mind.Personality with { Negative = negative } };
        var withLetter = BotPlayer.Evidence(view, Tags, mind, "knife");
        var withoutLetter = BotPlayer.Evidence(View, Tags, mind, "knife");
        var line = BotDiscussion.Compose(view, Tags, mind, [], new Random(1))!.Value;
        if (negative == 0)
        {
            withLetter.Should().Be(withoutLetter);
            line.Text.Should().NotContain("Учёл и свои исчезнувшие письма")
                .And.Contain("Исчезновение своих писем не считаю доводом против карт");
        }
        else
        {
            withLetter.Should().BeLessThan(withoutLetter);
            line.Text.Should().Contain("Учёл и свои исчезнувшие письма");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void SummaryDoesNotInventVanishingForOpenedOrPendingLetter(bool? revealed)
    {
        var view = View with { Me = View.Me! with { Letters = [new(2, "sword", revealed)] } };
        var mind = Mind with { Personality = Mind.Personality with { Negative = 0 } };
        var line = BotDiscussion.Compose(view, Tags, mind, [], new Random(1))!.Value;
        line.Text.Should().NotContain("исчезнувшие письма").And.NotContain("Исчезновение своих писем");
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

    [Theory]
    [InlineData("rose", true)]
    [InlineData("knife", false)]
    [InlineData("unrelated-letter", false)]
    public void ChangedChoiceIsExplicit_WithoutInventingARevision(string earlier, bool changed)
    {
        var now = DateTimeOffset.UtcNow;
        var line = BotDiscussion.Compose(View, Tags, Mind,
            [new(Me, "Моя версия", [earlier, "boat"], now),
             new(Ann, "Бот Я, что думаешь о мотиве?", [], now.AddSeconds(1))], new Random(1))!.Value;
        line.Cards[0].Should().Be("knife");
        line.Text.Should().Contain("Подсказка р.0 №1");
        if (changed) line.Text.Should().Contain("Пересмотрел выбор: мотив 2 → мотив 1");
        else line.Text.Should().NotContain("Пересмотрел выбор");
    }

    [Fact]
    public void RevisionUsesLatestPublicChoiceRatherThanOpeningVersion()
    {
        var now = DateTimeOffset.UtcNow;
        var line = BotDiscussion.Compose(View, Tags, Mind,
            [new(Me, "Сначала так", ["rose", "boat"], now),
             new(Me, "Теперь так", ["knife", "boat"], now.AddSeconds(1)),
             new(Ann, "Бот Я, что думаешь о мотиве?", [], now.AddSeconds(2))], new Random(1))!.Value;
        line.Text.Should().NotContain("Пересмотрел выбор");
    }

    [Fact]
    public void DoesNotAcknowledgeAnAcknowledgementAgain()
    {
        var now = DateTimeOffset.UtcNow;
        List<DiscussionLine> lines = [new(Me, "Аня, что думаешь о месте?", ["boat"], now),
            new(Ann, "Бот Я, выбираю место 1.", ["boat"], now.AddSeconds(1))];
        var first = BotDiscussion.Compose(View, Tags, Mind, lines, new Random(1))!.Value;
        first.Text.Should().Contain("сверил твой ответ");
        lines.Add(new(Me, first.Text, first.Cards, now.AddSeconds(2)));
        lines.Add(new(Ann, "Бот Я, сверил твой ответ: место 1, согласен.", ["boat"], now.AddSeconds(3)));
        var next = BotDiscussion.Compose(View, Tags, Mind, lines, new Random(1));
        next?.Text.Should().NotContain("сверил твой ответ");
        next?.Text.Should().NotContain("отвечаю по всей версии");
        if (next is { } summary) lines.Add(new(Me, summary.Text, summary.Cards, now.AddSeconds(4)));
        lines.Add(new(Ann, "Бот Я, почему выбрал место 1?", ["boat"], now.AddSeconds(5)));
        BotDiscussion.Compose(View, Tags, Mind, lines, new Random(1))!.Value.Text
            .Should().Contain("отвечаю про голосование", "новый вопрос после подтверждения всё равно заслуживает ответа");
    }

    [Fact]
    public void UnsolicitedAddressIsNotAnAnswerToSomeoneElsesQuestion()
    {
        var bob = Guid.NewGuid();
        var mind = Mind with { Names = new Dictionary<Guid, string>(Mind.Names) { [bob] = "Борис" } };
        var now = DateTimeOffset.UtcNow;
        List<DiscussionLine> lines = [new(Me, "Борис, что думаешь о месте?", ["boat"], now),
            new(Ann, "Бот Я, место 1.", ["boat"], now.AddSeconds(1))];
        var next = BotDiscussion.Compose(View, Tags, mind, lines, new Random(1));
        next?.Text.Should().NotContain("сверил твой ответ");
    }

    [Fact]
    public void TextRowTakesPriorityOverWholeVersionAttachment()
    {
        var now = DateTimeOffset.UtcNow;
        var question = new DiscussionLine(Ann, "Бот Я, что думаешь о месте?", ["rose", "cat"], now.AddSeconds(1));
        var line = BotDiscussion.Compose(View, Tags, Mind,
            [new(Me, "Версия", [], now), question], new Random(1))!.Value;
        line.Text.Should().Contain("Сейчас выберу место 1").And.Contain("не согласен");
        line.Cards[0].Should().Be("boat");
    }

    [Theory]
    [InlineData("Почему по мотиву так?", Category.Motive, "мотив")]
    [InlineData("Что думаешь о месте?", Category.Place, "место")]
    [InlineData("Какая карта места убедительнее?", Category.Place, "место")]
    [InlineData("По способу что выбираешь?", Category.Method, "способ")]
    [InlineData("Что со способом?", Category.Method, "способ")]
    [InlineData("Какую карту тайны поддерживаешь?", Category.Secret, "тайна")]
    [InlineData("С ТАЙНОЙ определился?", Category.Secret, "тайна")]
    public void CardlessQuestionsUnderstandRowInflections(string text, Category category, string label)
    {
        var view = View with { Board = [new(Category.Motive, ["knife", "rose"]), new(category, ["boat", "cat"])] };
        if (category == Category.Motive) view = view with { Board = [new(Category.Place, ["knife", "rose"]), new(category, ["boat", "cat"])] };
        var now = DateTimeOffset.UtcNow;
        var line = BotDiscussion.Compose(view, Tags, Mind,
            [new(Me, "Версия", [], now), new(Ann, "Бот Я, " + text, [], now.AddSeconds(1))], new Random(1))!.Value;
        line.Text.Should().Contain($"Сейчас выберу {label} 1");
        line.Cards[0].Should().Be("boat", "выделяем ответ по запрошенному ряду, а не первый ряд");
    }

    [Theory]
    [InlineData("Что думаешь?", false)]
    [InlineData("Что думаешь?", true)]
    [InlineData("Ты уже определился с местностью?", false)]
    public void AmbiguousQuestionGetsOneClarificationWithoutInventedCard(string question, bool attachVersion)
    {
        var now = DateTimeOffset.UtcNow;
        List<DiscussionLine> messages = [new(Me, "Версия", [], now),
            new(Ann, "Бот Я, " + question, attachVersion ? ["rose", "cat"] : [], now.AddSeconds(1))];
        var line = BotDiscussion.Compose(View, Tags, Mind, messages, new Random(1))!.Value;
        line.Text.Should().Contain("уточню").And.NotContain("мотив 1");
        line.Cards.Should().BeEmpty();
        messages.Add(new(Me, line.Text, line.Cards, now.AddSeconds(2)));
        BotDiscussion.Compose(View, Tags, Mind, messages, new Random(1))?.Text.Should().NotContain("уточню");
        messages.Add(new(Ann, "Бот Я, про место?", [], now.AddSeconds(3)));
        BotDiscussion.Compose(View, Tags, Mind, messages, new Random(1))!.Value.Text.Should().Contain("Сейчас выберу место 1");
    }

    [Theory]
    [InlineData("Что думаешь по мотиву и месту?")]
    [InlineData("С мотивом согласен? А что с местом?")]
    [InlineData("Какие улики связываешь с МЕСТОМ и МОТИВОМ?")]
    public void ExplicitMultipleRowsGetOneAnswerWithReasonsAndOnlyRelevantCards(string text)
    {
        var view = View with { Board = [.. View.Board, new(Category.Secret, ["dog", "tulip"])] };
        var now = DateTimeOffset.UtcNow;
        List<DiscussionLine> messages = [new(Me, "Версия", [], now),
            new(Ann, "Бот Я, " + text, ["rose", "boat", "dog"], now.AddSeconds(1))];
        var line = BotDiscussion.Compose(view, Tags, Mind, messages, new Random(1))!.Value;
        line.Text.Should().StartWith("Аня, отвечаю").And.NotContain("уточню");
        line.Text.Should().Contain("мотив 1").And.Contain("место 1").And.NotContain("тайна");
        line.Text.Should().Contain("Подсказка р.0 №1").And.Contain("Подсказка р.1 №2");
        line.Text.Should().Contain("не согласен").And.Contain("совпадают");
        line.Cards.Should().Equal("knife", "boat");
        line.Notes.Should().Equal("думаю, эта", "думаю, эта");
        line.Text.Length.Should().BeLessThanOrEqualTo(ChatService.MaxTextLength);
        messages.Add(new(Me, line.Text, line.Cards, now.AddSeconds(2)));
        BotDiscussion.Compose(view, Tags, Mind, messages, new Random(1))?.Text.Should().NotContain("отвечаю по названным рядам");
    }

    [Fact]
    public void WholeVersionQuestionGetsEveryRowInsteadOfArbitraryFirstOne()
    {
        var now = DateTimeOffset.UtcNow;
        var line = BotDiscussion.Compose(View, Tags, Mind,
            [new(Me, "Версия", [], now), new(Ann, "Бот Я, какая у тебя версия?", [], now.AddSeconds(1))], new Random(1))!.Value;
        line.Text.Should().Contain("по всей версии").And.Contain("мотив 1").And.Contain("место 1");
        line.Cards.Should().Equal("knife", "boat");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnswerToLatestQuestionDoesNotConsumeEarlierQuestionFromSamePlayer(bool vague)
    {
        var now = DateTimeOffset.UtcNow;
        List<DiscussionLine> lines = [new(Me, "Моя версия", ["knife", "boat"], now),
            new(Ann, "Бот Я, почему такой мотив?", [], now.AddSeconds(1)),
            new(Ann, vague ? "Бот Я, что думаешь?" : "Бот Я, а что по месту?", [], now.AddSeconds(2))];
        var latest = BotDiscussion.Compose(View, Tags, Mind, lines, new Random(1))!.Value;
        latest.Text.Should().Contain(vague ? "уточню" : "Сейчас выберу место 1");
        lines.Add(new(Me, latest.Text, latest.Cards, now.AddSeconds(3)));
        var earlier = BotDiscussion.Compose(View, Tags, Mind, lines, new Random(1))!.Value;
        earlier.Text.Should().Contain("отвечаю про голосование").And.Contain("Сейчас выберу мотив 1");
        lines.Add(new(Me, earlier.Text, earlier.Cards, now.AddSeconds(4)));
        BotDiscussion.Compose(View, Tags, Mind, lines, new Random(1))?.Text.Should().NotContain("отвечаю про голосование");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectQuestionAboutPlayerGetsSuspicionAnswer_NotCardNumber(bool attachCards)
    {
        var now = DateTimeOffset.UtcNow;
        var line = BotDiscussion.Compose(View, Tags, Mind,
            [new(Me, "Версия", ["knife"], now), new(Ann, "Бот Я, за кого будешь голосовать?", attachCards ? ["boat"] : [], now.AddSeconds(15))], new Random(1))!.Value;
        line.Text.Should().Contain("про подозрения").And.Contain("недостаточно оснований").And.NotContain("мотив 1");
    }

    [Theory]
    [InlineData("Что думаешь о месте и за кого будешь голосовать?", "место 1")]
    [InlineData("Что думаешь о мотиве и месте, кого подозреваешь?", "мотив 1")]
    public void MixedQuestionAnswersRowsAndSuspicions(string question, string expected)
    {
        var now = DateTimeOffset.UtcNow;
        var line = BotDiscussion.Compose(View, Tags, Mind,
            [new(Me, "Версия", [], now), new(Ann, "Бот Я, " + question, [], now.AddSeconds(1))], new Random(1))!.Value;
        line.Text.Should().Contain(expected).And.Contain("место 1").And.Contain("недостаточно оснований");
        line.Cards.Should().Contain("boat");
    }

    [Theory]
    [InlineData("Бот Борис, кто будет голосовать за место 1?", false)]
    [InlineData("Борис, кто будет голосовать за место 1?", false)]
    [InlineData("Никто не голосовал за место 1?", false)]
    [InlineData("Кто будет голосовать за место 1?", true)]
    public void OnlyUnaddressedWhoQuestionsInviteTheWholeTable(string text, bool shouldAnswer)
    {
        var bob = Guid.NewGuid();
        var mind = Mind with { Names = new Dictionary<Guid, string>(Mind.Names) { [bob] = "Бот Борис" } };
        var now = DateTimeOffset.UtcNow;
        List<DiscussionLine> lines = [new(Me, "Моя версия", ["knife", "boat"], now),
            new(Ann, text, [], now.AddSeconds(1))];
        var reply = BotDiscussion.Compose(View, Tags, mind, lines, new Random(1))!.Value.Text;
        if (shouldAnswer) reply.Should().Contain("отвечаю про голосование").And.Contain("Сейчас выберу место 1");
        else reply.Should().NotContain("отвечаю").And.NotContain("уточню");
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
