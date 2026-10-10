using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

/// <summary>Характеры ботов: шесть спектров меняют то, как бот видит карты, читает подсказки и играет роль.</summary>
public sealed class BotPersonalityTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Ghost = Guid.NewGuid();
    private static readonly Guid Ann = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private static readonly CardTags Tags = new(new Dictionary<string, HashSet<string>>
    {
        ["knife"] = ["knife", "metal", "sharp", "weapon"],
        ["rose"] = ["rose", "flower", "red", "love"],
        ["boat"] = ["boat", "sea", "water", "travel"],
        ["cat"] = ["cat", "animal", "fur"],
        ["sword"] = ["sword", "metal", "sharp", "weapon"],
        ["dagger"] = ["dagger", "metal", "sharp", "weapon"],
        ["tulip"] = ["tulip", "flower", "love"],
        ["apple"] = ["apple", "fruit", "red", "shape-round"],
        ["pear"] = ["pear", "fruit", "green", "shape-long"],
        ["ball"] = ["ball", "toy", "red", "shape-round"],
    });

    private static readonly Dictionary<Guid, string> Names = new() { [Ann] = "Аня", [Bob] = "Бот Боб", [Ghost] = "Лена", [Me] = "Я" };

    private static BotMind Mind(BotPersonality p, IReadOnlyList<ChatOpinion>? opinions = null,
        IReadOnlyDictionary<Guid, PlayerHistory>? history = null, IReadOnlyList<Accusation>? accusations = null) =>
        new(p, history ?? new Dictionary<Guid, PlayerHistory>(), opinions ?? [], Names, accusations);

    // Ряд 0: knife | rose; ряд 1: boat | cat.
    private static PlayerView View(Phase phase, string[] allowed, Role role, IReadOnlyList<MyLetterView>? letters = null,
        IReadOnlyList<HintGroupView>? hints = null, IReadOnlyList<int>? truth = null, FinaleView? finale = null,
        IReadOnlyList<PlayerInfoView>? players = null) => new(
        Guid.NewGuid(), 1, phase, 2, 3, DiscussionMode.FreeChat,
        [new BoardRowView(Category.Motive, ["knife", "rose"]), new BoardRowView(Category.Place, ["boat", "cat"])],
        hints ?? [], 0,
        players ??
        [
            new PlayerInfoView(Ghost, 0, true, Role.Ghost, false, 5),
            new PlayerInfoView(Me, 1, false, role, false, 5),
            new PlayerInfoView(Ann, 2, false, null, false, 5),
            new PlayerInfoView(Bob, 3, false, null, false, 5),
        ],
        new MeView(Me, role, ["sword", "tulip"], letters ?? []),
        truth, 0, null, null, null, null, [], allowed, finale);

    private static FinaleView RowStage(int row) =>
        new(new VoteStageView(0, VoteStageKind.Row, row, 1, [0, 1], []), 3, null, [], [], [], null, null, null, [], []);

    private static FinaleView KillerStage() =>
        new(new VoteStageView(4, VoteStageKind.Killer, -1, 1, [], [Ann, Bob]), 5, null, [], [], [], null, null, null, [], []);

    [Fact]
    public void Attention_ColorEyedBot_SeesDifferentPairs()
    {
        var meaning = new BotPersonality { Meaning = 1, Shape = 0, Color = 0 }.Attention;
        var color = new BotPersonality { Meaning = 0, Shape = 0, Color = 1 }.Attention;

        Tags.Similarity("apple", "pear", meaning).Should().BeGreaterThan(Tags.Similarity("apple", "ball", meaning), "по смыслу яблоко ближе к груше");
        Tags.Similarity("apple", "ball", color).Should().BeGreaterThan(Tags.Similarity("apple", "pear", color), "по цвету яблоко ближе к мячу");
    }

    [Fact]
    public void Negative_NotOpenedLetter_PushesAwayOnlyIfBotBelievesIt()
    {
        // Подсказки: sword (→ knife) и tulip (→ rose). Моё письмо dagger (похоже на knife) не открыли.
        var hints = new[] { new HintGroupView(1, ["sword", "tulip"]) };
        var letters = new[] { new MyLetterView(1, "dagger", false) };
        var view = View(Phase.Voting, [nameof(CastVote)], Role.Detective, letters, hints, finale: RowStage(0));

        var shrugs = (CastVote)BotPlayer.Decide(view, new Random(1), Tags, Mind(new BotPersonality { Negative = 0, Compromise = 0 }))!;
        var believer = (CastVote)BotPlayer.Decide(view, new Random(1), Tags, Mind(new BotPersonality { Negative = 1, Compromise = 0 }))!;

        shrugs.Column.Should().Be(0, "«не достали и ладно» — нож всё ещё похож на подсказку");
        believer.Column.Should().Be(1, "«не достали — значит, не нож»");
    }

    [Fact]
    public void Compromise_FollowsTableOpinion_OrIgnoresIt()
    {
        var hints = new[] { new HintGroupView(1, ["sword"]) };
        var table = new[] { new ChatOpinion(Ann, "rose", 1), new ChatOpinion(Bob, "rose", 1) };
        var view = View(Phase.Voting, [nameof(CastVote)], Role.Detective, hints: hints, finale: RowStage(0));

        var stubborn = (CastVote)BotPlayer.Decide(view, new Random(2), Tags, Mind(new BotPersonality { Compromise = 0 }, table))!;
        var agreeable = (CastVote)BotPlayer.Decide(view, new Random(2), Tags, Mind(new BotPersonality { Compromise = 0.9 }, table))!;

        stubborn.Column.Should().Be(0, "упрямый верит своим глазам");
        agreeable.Column.Should().Be(1, "сговорчивый идёт за столом");
    }

    [Fact]
    public void Memory_RemembersWhoWasKiller()
    {
        var history = new Dictionary<Guid, PlayerHistory> { [Bob] = new(Games: 6, KillerTeam: 5, Informed: 0), [Ann] = new(6, 0, 1) };
        var view = View(Phase.Voting, [nameof(CastVote)], Role.Detective, finale: KillerStage());

        var votesForBob = Enumerable.Range(0, 40)
            .Count(seed => ((CastVote)BotPlayer.Decide(view, new Random(seed), Tags, Mind(new BotPersonality { Memory = 1 }, history: history))!).Suspect == Bob);
        var blank = Enumerable.Range(0, 40)
            .Count(seed => ((CastVote)BotPlayer.Decide(view, new Random(seed), Tags, Mind(new BotPersonality { Memory = 0 }, history: history))!).Suspect == Bob);

        votesForBob.Should().Be(40, "был Убийцей в 5 из 6 партий — значит, возможно, и сейчас");
        blank.Should().BeInRange(5, 35, "с чистого листа — без предубеждений");
    }

    [Fact]
    public void Risk_KillerLiesMoreOrStaysHonest()
    {
        // Подсказка раунда — tulip, моё письмо sword исчезло: соврать можно, выдав tulip за своё.
        var hints = new[] { new HintGroupView(2, ["tulip"]) };
        var letters = new[] { new MyLetterView(2, "sword", false) };
        var view = View(Phase.Discussion, [], Role.Killer, letters, hints, truth: [1, 0]);

        int Lies(double risk) => Enumerable.Range(0, 60)
            .Count(seed => BotPlayer.Say(view, new Random(seed), Tags, Mind(new BotPersonality { Risk = risk }))!.Value.Cards[0] == "tulip");

        Lies(0).Should().BeLessThan(15, "осторожный почти не врёт — не хочет попасться");
        Lies(1).Should().BeGreaterThan(45, "рисковый врёт почти всегда");
    }

    [Fact]
    public void Risk_WitnessAccusesKillerByName()
    {
        var players = new[]
        {
            new PlayerInfoView(Ghost, 0, true, Role.Ghost, false, 5),
            new PlayerInfoView(Me, 1, false, Role.Witness, false, 5),
            new PlayerInfoView(Ann, 2, false, null, false, 5),
            new PlayerInfoView(Bob, 3, false, Role.Killer, false, 5),
        };
        var view = View(Phase.Discussion, [], Role.Witness, hints: [new HintGroupView(1, ["sword"])], players: players);

        var bold = Enumerable.Range(0, 30).Count(seed =>
            BotPlayer.Say(view, new Random(seed), Tags, Mind(new BotPersonality { Risk = 1 }))!.Value.Text.Contains("Убийца — Бот Боб"));
        var careful = Enumerable.Range(0, 30).Count(seed =>
            BotPlayer.Say(view, new Random(seed), Tags, Mind(new BotPersonality { Risk = 0 }))!.Value.Text.Contains("Боб"));

        bold.Should().BeGreaterThan(20, "рисковый Свидетель говорит прямо");
        careful.Should().Be(0, "осторожный Свидетель молчит, чтобы его не вычислили");
    }

    [Fact]
    public void Variability_DriftsPerGame_ButStableWithinGame()
    {
        var steady = new BotPersonality { Risk = 0.5, Variability = 0 };
        var moody = new BotPersonality { Risk = 0.5, Variability = 1 };
        var bot = Guid.NewGuid();
        var games = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToList();

        games.Select(g => steady.ForGame(g, bot).Risk).Distinct().Should().ContainSingle();
        games.Select(g => moody.ForGame(g, bot).Risk).Distinct().Count().Should().BeGreaterThan(10);
        moody.ForGame(games[0], bot).Should().Be(moody.ForGame(games[0], bot), "в одной партии характер не скачет");
        games.Select(g => moody.ForGame(g, bot)).Should().OnlyContain(p => p.Risk >= 0 && p.Risk <= 1);
    }

    [Fact]
    public void Presets_AreDistinct()
    {
        BotPresets.All.Select(p => p.P).Distinct().Should().HaveCount(BotPresets.All.Count);
        BotPresets.All.Select(p => p.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void AccusationReader_FindsNamesNextToSuspicionWords()
    {
        AccusationReader.Read(Ann, "Мне кажется, Убийца — Бот Боб.", Names).Should().ContainSingle()
            .Which.Should().Be(new Accusation(Ann, Bob, 1));
        AccusationReader.Read(Bob, "Подозреваю, что аня из чёрных", Names).Should().ContainSingle().Which.Strength.Should().Be(0.6);
        AccusationReader.Read(Ann, "Боб точно не убийца", Names).Should().BeEmpty();
        AccusationReader.Read(Ann, "Боб, какая у тебя карта?", Names).Should().BeEmpty("без подозрения — не обвинение");
        AccusationReader.Read(Ann, "Аня — Убийца!", Names).Should().BeEmpty("себя автор не обвиняет");
    }

    [Fact]
    public void Accusations_AgreeableBotsListenToTheTable()
    {
        var accusations = new[] { new Accusation(Ann, Bob, 1), new Accusation(Ann, Bob, 1) };
        var view = View(Phase.Voting, [nameof(CastVote)], Role.Detective, finale: KillerStage());

        int VotesForBob(double compromise) => Enumerable.Range(0, 40).Count(seed =>
            ((CastVote)BotPlayer.Decide(view, new Random(seed), Tags, Mind(new BotPersonality { Compromise = compromise, Memory = 0 }, accusations: accusations))!)
            .Suspect == Bob);

        VotesForBob(0.9).Should().Be(40, "сговорчивый верит, что Боб — Убийца");
        VotesForBob(0).Should().BeLessThan(38, "упрямый почти не слушает чужих обвинений");
    }

    [Fact]
    public void Discussion_WhiteBotNamesSuspect_WhenConfident()
    {
        var accusations = new[] { new Accusation(Ann, Bob, 1), new Accusation(Ann, Bob, 1), new Accusation(Ghost, Bob, 1) };
        var view = View(Phase.Discussion, [], Role.Detective, hints: [new HintGroupView(1, ["sword"])]);

        var lines = Enumerable.Range(0, 20)
            .Select(seed => BotPlayer.Say(view, new Random(seed), Tags, Mind(new BotPersonality { Risk = 0.8, Compromise = 0.9 }, accusations: accusations))!.Value.Text)
            .ToList();

        lines.Should().OnlyContain(t => t.Contains("Убийца — Бот Боб"), "подозрение сильное — говорит прямо");
        AccusationReader.Read(Me, lines[0], Names).Should().Contain(a => a.Target == Bob, "и другие боты прочитают это обвинение");
    }

    [Fact]
    public void Discussion_KillerFramesTheAccuser()
    {
        var accusations = new[] { new Accusation(Ann, Me, 1) };
        var view = View(Phase.Discussion, [], Role.Killer, hints: [new HintGroupView(1, ["sword"])], truth: [1, 0]);

        var framed = Enumerable.Range(0, 40).Count(seed =>
            BotPlayer.Say(view, new Random(seed), Tags, Mind(new BotPersonality { Risk = 1 }, accusations: accusations))!.Value.Text.Contains("Аня из чёрных"));
        var calm = Enumerable.Range(0, 40).Count(seed =>
            BotPlayer.Say(view, new Random(seed), Tags, Mind(new BotPersonality { Risk = 0 }, accusations: accusations))!.Value.Text.Contains("из чёрных"));

        framed.Should().BeGreaterThan(15, "рисковый Убийца переводит стрелки на обвинителя");
        calm.Should().Be(0, "осторожный не привлекает внимания");
    }

    [Fact]
    public void DetailAlreadyPresentInOldTags_IsNotDoubleCountedAsMainSubject()
    {
        var tags = CardTags.Parse("""{"cage":["cage","flower"],"bloom":["flower","plant"],"prison":["cage","metal"]}""")
            .WithDetails("""{"cage":[{"tag":"flower","weight":0.4,"label":"розы у основания"}]}""");
        var attention = (Meaning: 1.0, Shape: 0.0, Color: 0.0);
        tags.Similarity("cage", "bloom", attention, 0).Should().Be(0);
        tags.Similarity("cage", "prison", attention, 0).Should().BeGreaterThan(0);
        tags.Similarity("cage", "bloom", attention, 1).Should().BeGreaterThan(tags.Similarity("cage", "prison", attention, 1));
        tags.Explain("cage", "bloom", attention, 1).Should().Contain("по деталям: розы");
    }

    [Fact]
    public void Details_ChangeEvidenceAndExplainTheActualFeature()
    {
        var tags = Tags.WithDetails("""
            {"sword":[{"tag":"flower","weight":0.3,"label":"цветок на рукояти"}],
             "knife":[{"tag":"hole","weight":0.8,"label":"отверстие"}],
             "rose":[{"tag":"flower","weight":0.9,"label":"цветок"}]}
            """);
        var broad = new BotPersonality { Meaning = 1, Shape = 0, Color = 0, Details = 0, Strictness = 0 };
        var picky = broad with { Details = 1 };
        var view = View(Phase.Discussion, [], Role.Detective, hints: [new HintGroupView(1, ["sword"])]);
        BotPlayer.Evidence(view, tags, Mind(broad), "knife").Should().BeGreaterThan(BotPlayer.Evidence(view, tags, Mind(broad), "rose"));
        BotPlayer.Evidence(view, tags, Mind(picky), "rose").Should().BeGreaterThan(BotPlayer.Evidence(view, tags, Mind(picky), "knife"));
        var line = BotPlayer.Say(view, new Random(1), tags, Mind(picky))!.Value;
        line.Text.Should().Contain("по деталям: цветок").And.Contain("Подсказка р.1 №1");
        tags.Explain("sword", "rose", broad.Attention, 0).Should().NotContain("по деталям");
    }

    [Fact]
    public void StructuredSpeech_ExplainsChecksAndUncertainty_WithinChatLimits()
    {
        var view = View(Phase.Discussion, [], Role.Detective,
            letters: [new MyLetterView(2, "sword", false)], hints: [new HintGroupView(2, ["tulip"])]);
        var line = BotPlayer.Say(view, new Random(3), Tags, Mind(new BotPersonality { Negative = 1 }))!.Value;
        line.Text.Should().Contain("Проверял:").And.Contain("по смыслу").And.Contain("ослабляет")
            .And.Contain("не исключает").And.Contain("Версия:").And.Contain("Подсказка р.2 №1");
        line.Text.Length.Should().BeLessThanOrEqualTo(ChatService.MaxTextLength);
        line.Cards.Count.Should().BeLessThanOrEqualTo(ChatService.MaxCards);
        line.Notes.Should().HaveSameCount(line.Cards);
        line.Notes.Should().OnlyContain(n => n.Length <= ChatService.MaxNoteLength);
        var shrug = BotPlayer.Say(view, new Random(3), Tags, Mind(new BotPersonality { Negative = 0 }))!.Value;
        shrug.Text.Should().Contain("не считаю это исключением");
    }

    [Fact]
    public void VanishedLetterWeakensButDoesNotExcludeCardSupportedByOtherClues()
    {
        var view = View(Phase.Discussion, [], Role.Detective,
            letters: [new MyLetterView(2, "sword", false)], hints: [new HintGroupView(2, ["dagger"])]);
        var mind = Mind(new BotPersonality { Meaning = 1, Shape = 0, Color = 0, Negative = 0.1, Compromise = 0 });
        var withoutVanishing = view with { Me = view.Me! with { Letters = [] } };
        BotPlayer.Evidence(view, Tags, mind, "knife").Should().BeLessThan(
            BotPlayer.Evidence(withoutVanishing, Tags, mind, "knife"));
        var line = BotPlayer.Say(view, new Random(3), Tags, mind)!.Value;
        var index = line.Cards.ToList().IndexOf("knife");
        index.Should().BeGreaterThanOrEqualTo(0);
        line.Notes[index].Should().Contain("думаю, эта");
        line.Text.Should().NotContain("исключаю эти карты").And.Contain("ослабляет");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolvedLetterWithoutKnownLinksDoesNotClaimToAwaitItsResult(bool revealed)
    {
        var view = View(Phase.Discussion, [], Role.Detective, letters: [new MyLetterView(2, "sword", revealed)]);
        var line = BotPlayer.Say(view, new Random(3), CardTags.Empty, Mind(BotPersonality.Default))!.Value;
        line.Text.Should().NotContain("жду результат").And.NotContain("усиливает эти связи")
            .And.Contain("явных связей с картами поля я не вижу");
        line.Notes.Should().Contain("кидал эту");
    }

    [Fact]
    public void Reply_AddressesLikelyAllyAndNamesBehaviouralReasonForSuspect()
    {
        var view = View(Phase.Discussion, [], Role.Detective, hints: [new HintGroupView(1, ["sword"]) ]);
        var mind = Mind(new BotPersonality { Risk = 1, Strictness = 1, Memory = 0 },
            [new ChatOpinion(Ann, "knife", 1), new ChatOpinion(Bob, "rose", 1)]);
        var reply = BotPlayer.Reply(view, new Random(0), Tags, mind)!.Value;
        reply.Text.Should().Contain("Аня,").And.Contain("пока считаю тебя мирным")
            .And.Contain("Бот Боб из чёрных").And.Contain("хуже объясняет").And.Contain("голосовать против");
        AccusationReader.Read(Me, reply.Text, Names).Should().ContainSingle().Which.Target.Should().Be(Bob);
        BotPlayer.Reply(view with { Me = view.Me! with { Role = Role.Ghost } }, new Random(0), Tags, mind).Should().BeNull();
        BotPlayer.Reply(view with { Hints = [] }, new Random(0), Tags, mind).Should().BeNull("без улик не объявляем мирным");
    }

    [Fact]
    public void CheckingCard_IsNotAdviceOrSuspiciousBehaviour()
    {
        var view = View(Phase.Discussion, [], Role.Detective, hints: [new HintGroupView(1, ["sword"]) ]);
        var mind = Mind(new BotPersonality { Risk = 1, Strictness = 1, Memory = 0 },
            [new ChatOpinion(Ann, "knife", 1), new ChatOpinion(Bob, "rose", 1, IsCheck: true)]);
        BotPlayer.Reply(view, new Random(0), Tags, mind)!.Value.Text.Should().NotContain("Боб");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectingOneOfEquallySupportedCardsIsNotSuspicious(bool tied)
    {
        var tags = new CardTags(new Dictionary<string, HashSet<string>> {
            ["knife"] = ["weapon"], ["rose"] = [tied ? "weapon" : "flower"],
            ["sword"] = ["weapon"], ["boat"] = ["water"], ["sea"] = ["water"] });
        var view = View(Phase.Discussion, [], Role.Detective, hints: [new HintGroupView(1, ["sword", "sea"])]);
        var mind = Mind(new BotPersonality { Meaning = 1, Shape = 0, Color = 0, Risk = 1, Strictness = 0.5, Memory = 0 },
            [new ChatOpinion(Ann, "boat", 1), new ChatOpinion(Bob, "knife", -1)]);
        var reply = BotPlayer.Reply(view, new Random(0), tags, mind)!.Value.Text;
        if (tied) reply.Should().NotContain("Бот Боб из чёрных", "другая карта ряда объясняет улики столь же хорошо");
        else reply.Should().Contain("Бот Боб из чёрных", "отрицание единственной хорошо поддержанной версии всё ещё учитывается");
    }

    [Fact]
    public void Memory_ZeroIsBlank_OneRetainsAll_RecentGamesWeighMore()
    {
        var now = DateTimeOffset.UtcNow;
        var past = Enumerable.Range(0, 80).Select(i => new RememberedPlayer(Guid.NewGuid(), Bob,
            i < 40 ? "Killer" : "Detective", now.AddDays(-i))).ToList();
        BotMemory.Recall(past, 0).Should().BeEmpty();
        var all = BotMemory.Recall(past, 1)[Bob];
        all.Games.Should().Be(80);
        all.KillerRate.Should().BeGreaterThan(0.6);
        BotMemory.Recall(past, 0.1)[Bob].Games.Should().BeLessThan(80);
        var reversed = past.Select((p, i) => p with { FinishedAt = now.AddDays(i) });
        BotMemory.Recall(reversed, 1)[Bob].KillerRate.Should().BeLessThan(all.KillerRate);
        for (var i = 0; i < 20; i++)
        {
            var p = new BotPersonality { Memory = 0, Details = 0, Variability = 1 }.ForGame(Guid.NewGuid(), Me);
            p.Memory.Should().Be(0);
            p.Details.Should().Be(0);
        }
    }
}
