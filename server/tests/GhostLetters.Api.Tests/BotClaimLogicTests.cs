using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

/// <summary>
/// Логика заявлений: бот перебирает, кто за столом чёрный, и из «проверял — исчезло» делает человеческие выводы.
/// </summary>
public sealed class BotClaimLogicTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static Guid Seat(int i) => new(i, 0, 0, new byte[8]);
    private static readonly Guid Ghost = Seat(1), Me = Seat(2), Ann = Seat(3), Bob = Seat(4),
        Cid = Seat(5), Dan = Seat(6), Eva = Seat(7), Fay = Seat(8);

    // Ряд 0: кольцо | кошелёк | лодка; ряд 1: меч | кот | роза.
    private static readonly CardTags Tags = new(new Dictionary<string, HashSet<string>>
    {
        ["ring"] = ["ring", "gold", "jewel"], ["wallet"] = ["wallet", "money", "leather"], ["boat"] = ["boat", "water"],
        ["sword"] = ["sword", "steel", "weapon"], ["cat"] = ["cat", "animal", "fur"], ["rose"] = ["rose", "flower", "red"],
        // Письма: каждое ясно указывает на одну карту поля.
        ["crown"] = ["gold", "jewel", "king"], ["tiara"] = ["gold", "jewel", "queen"], ["brooch"] = ["gold", "jewel", "pin"],
        ["purse"] = ["money", "leather", "bag"], ["ship"] = ["boat", "water", "sail"], ["stone"] = ["stone"],
    });

    private static readonly BotTuning On = BotTuning.Default with { ClaimLogic = true, ClaimLogicWeight = 1, ClaimLogicBlack = true };

    private static BotMind Mind(IReadOnlyList<ChatOpinion> opinions) =>
        new(BotPersonality.Default, new Dictionary<Guid, PlayerHistory>(), opinions,
            new Dictionary<Guid, string>
            {
                [Ghost] = "Призрак", [Me] = "Я", [Ann] = "Аня", [Bob] = "Боб", [Cid] = "Сид", [Dan] = "Дэн", [Eva] = "Ева", [Fay] = "Фая",
            });

    // Восемь за столом: Призрак, я и шестеро неизвестных — из них двое чёрных.
    private static PlayerView View() => new(
        Guid.NewGuid(), 2, Phase.Discussion, 2, 4, DiscussionMode.FreeChat,
        [new BoardRowView(Category.Motive, ["ring", "wallet", "boat"]), new BoardRowView(Category.Method, ["sword", "cat", "rose"])],
        [], 0,
        [
            new PlayerInfoView(Ghost, 0, true, Role.Ghost, false, 5),
            new PlayerInfoView(Me, 1, false, Role.Detective, false, 5),
            new PlayerInfoView(Ann, 2, false, null, false, 5),
            new PlayerInfoView(Bob, 3, false, null, false, 5),
            new PlayerInfoView(Cid, 4, false, null, false, 5),
            new PlayerInfoView(Dan, 5, false, null, false, 5),
            new PlayerInfoView(Eva, 6, false, null, false, 5),
            new PlayerInfoView(Fay, 7, false, null, false, 5),
        ],
        new MeView(Me, Role.Detective, ["stone"], []), null, 0, null, null, null, null, [], [], null);

    private static ChatOpinion Check(Guid who, string card, string letter) => new(who, card, .5, IsCheck: true, SourceCard: letter) { Round = 2 };

    [Fact]
    public void ThreeCheckedIt_TwoBlacks_SoAtLeastOneIsHonest()
    {
        var one = Mind([Check(Ann, "ring", "crown")]);
        var three = Mind([Check(Ann, "ring", "crown"), Check(Bob, "ring", "tiara"), Check(Cid, "ring", "brooch")]);
        using (BotTuning.Use(On))
        {
            var single = BotPlayer.Logic(View(), Tags, one)!;
            var many = BotPlayer.Logic(View(), Tags, three)!;
            many.Truth["ring"].Should().BeLessThan(single.Truth["ring"]);
            many.Truth["ring"].Should().BeLessThan(.2, "трое не могут быть чёрными, когда чёрных двое");
            BotPlayer.Evidence(View(), Tags, three, "ring").Should().BeLessThan(BotPlayer.Evidence(View(), Tags, one, "ring"));
            var d = many.Deductions.Should().ContainSingle(x => x.Kind == "consensus").Subject;
            d.Text.Should().Contain("проверили трое").And.Contain("двое чёрных").And.Contain("хоть один");
        }
    }

    [Fact]
    public void EveryCardOfARowChecked_SomeoneLiesOrGhostErred()
    {
        var mind = Mind([Check(Ann, "ring", "crown"), Check(Bob, "wallet", "purse"), Check(Cid, "boat", "ship")]);
        using (BotTuning.Use(On))
        {
            var logic = BotPlayer.Logic(View(), Tags, mind)!;
            var d = logic.Deductions.Should().ContainSingle(x => x.Kind == "contradiction").Subject;
            d.Text.Should().Contain("либо Призрак ошибся, либо кто-то врёт");
            // Подозрение падает на проверявших, а не на молчавших.
            foreach (var claimer in new[] { Ann, Bob, Cid }) logic.Black[claimer].Should().BeGreaterThan(logic.Black[Dan]);
            logic.Truth.Where(x => x.Key is "ring" or "wallet" or "boat").Sum(x => x.Value).Should().BeApproximately(1, 1e-6);
        }
    }

    [Fact]
    public void PublicReportUpdatesTrustWithoutUsingTheClaimedTarget()
    {
        var view = View();
        var mind = Mind([Check(Ann, "boat", "crown")]);
        var empty = BotPlayer.Journal(view, Tags, Mind([]))!;
        var after = BotPlayer.Journal(view, Tags, mind)!;
        after.Evidence["ring"].Should().BeLessThan(empty.Evidence["ring"]);
        after.Evidence["boat"].Should().Be(empty.Evidence["boat"]);
        after.Black[Ann].Should().BeGreaterThan(empty.Black[Ann]);
        after.Trust[Ann].Should().BeLessThan(empty.Trust[Ann]);
        var known = view with { Players = view.Players.Select(p => p.Id == Ann ? p with { KnownRole = Role.Accomplice } : p).ToList() };
        BotPlayer.Evidence(known, Tags, mind, "ring").Should().Be(BotPlayer.Evidence(known, Tags, Mind([]), "ring"));
    }

    [Fact]
    public void Seed42_DetectiveRevisesTheRowAndSendsADifferentProbeAfterTheReport()
    {
        var tags = CardTags.Parse("""
            {"ring":["jewelry","wear"],"wallet":["money","bag"],"crown":["jewelry","wear"],
             "tiara":["jewelry","wear"],"purse":["money","bag"],
             "portrait":["jewelry","person","face","portrait","oil","canvas","art","paint","frame"],
             "blade":["weapon"],"cat":["animal"],"house":["home"],"pier":["port"],"rose":["flower"],"mask":["theater"]}
            """);
        var view = View() with
        {
            GameId = Seat(42), Phase = Phase.Mailbox, Round = 2,
            Board = [new(Category.Motive, ["ring","wallet"]), new(Category.Method, ["blade","cat"]),
                     new(Category.Place, ["house","pier"]), new(Category.Secret, ["rose","mask"])],
            Hints = [new(0, ["portrait"])],
            Me = new(Me, Role.Detective, ["tiara","purse"], []), AllowedCommands = [nameof(SendLetter)],
        };
        var names = Mind([]).Names;
        var board = view.Board.SelectMany(r => r.Cards).ToHashSet();
        var profile = BotPersonality.Default with { Negative = 1, Variability = 0 };
        var message = new BotService.MindMessage(Ann, 1, "Кидала корону, проверяла кошелёк.",
            ["crown","wallet"], ["кидал эту","проверял:0"], true);
        BotMind Read(params BotService.MindMessage[] messages) =>
            BotService.BuildMind(Me, profile, new Dictionary<Guid, PlayerHistory>(), messages, board, names);
        var before = Read();
        var after = Read(message);
        var prior = BotPlayer.Journal(view, tags, before)!;
        var revised = BotPlayer.Journal(view, tags, after)!;
        string Letter(BotMind mind) => ((SendLetter)BotPlayer.Decide(view, new Random(42), tags, mind)!).CardIds.Single();
        string Pick(BotMind mind) => view.Board[0].Cards[BotPlayer.Plan(view, tags, mind, new Random(42))[0]];
        output.WriteLine($"ring {prior.Truth["ring"]:F3} -> {revised.Truth["ring"]:F3}; black {prior.Black[Ann]:F3} -> {revised.Black[Ann]:F3}; trust {prior.Trust[Ann]:F3} -> {revised.Trust[Ann]:F3}; plan {Pick(before)} -> {Pick(after)}; letter {Letter(before)} -> {Letter(after)}");
        Pick(before).Should().Be("ring");
        Pick(after).Should().Be("wallet");
        revised.Black[Ann].Should().BeGreaterThan(prior.Black[Ann]);
        revised.Trust[Ann].Should().BeLessThan(prior.Trust[Ann]);
        Letter(before).Should().Be("tiara");
        Letter(after).Should().Be("purse");

        var echo = Read(message, message);
        BotPlayer.Journal(view, tags, echo)!.Truth.Should().BeEquivalentTo(revised.Truth);
        var boardOnly = view with { Table = new([], [], [], [new(Ann, 1, "crown")], true, false) };
        BotPlayer.Journal(boardOnly, tags, before)!.Truth.Should().BeEquivalentTo(revised.Truth);
        var pending = view with { Round = 1, Phase = Phase.GhostPick };
        BotPlayer.Journal(pending, tags, after)!.Truth.Should().BeEquivalentTo(BotPlayer.Journal(pending, tags, before)!.Truth);
    }

    [Theory]
    [InlineData("theme,flower,silhouette,red", "theme")]
    [InlineData("flower,silhouette,red", "flower")]
    [InlineData("silhouette,red", "silhouette")]
    [InlineData("red,incidental", "red")]
    public void VisualDetectiveChoosesLettersInPriorityOrder(string hand, string expected)
    {
        var tags = CardTags.Parse("""
            {"ring":["jewelry","dominant-red","shape-round"],"wallet":["money"],"theme":["jewelry","wear","royal","ceremony"],
             "flower":["plant"],"silhouette":["shape-round"],"red":["dominant-red"],"incidental":["red","metal"]}
            """).WithDetails("""{"flower":[{"tag":"rose","weight":1,"label":"роза"}],"ring":[{"tag":"rose","weight":1,"label":"роза"}]}""");
        var view = View() with { Phase = Phase.Mailbox, AllowedCommands = [nameof(SendLetter)],
            Me = new(Me, Role.Detective, hand.Split(','), []), Board = [new(Category.Motive, ["ring","wallet"])] };
        var mind = Mind([]) with { Personality = new() { Meaning = 0, Details = 1, Shape = 1, Color = 1 } };
        ((SendLetter)BotPlayer.Decide(view, new Random(42), tags, mind)!).CardIds.Should().Equal(expected);
    }

    [Fact]
    public void TextClaimCanNameABluffAndItsTargetWithoutPrivateLetterHistory()
    {
        var tags = Tags.WithNames("""{"crown":["корона"],"wallet":["кошелёк"],"tiara":["диадема"],"brooch":["диадема"]}""");
        var row = new BoardRow { Category = Category.Motive, Cards = ["ring","wallet"] };
        var read = Infrastructure.Speech.SpokenCards.ReadPublic("Я кидала корону, проверяла кошелёк.", [row], [], tags);
        read.Cards.Should().Equal("crown","wallet");
        read.Notes.Should().Equal("кидал эту","проверял:0");
        Infrastructure.Speech.SpokenCards.ReadPublic("Я кидала диадему.", [row], [], tags).Cards.Should().BeEmpty();
    }

    [Fact]
    public void SameLetterClaimedInTwoRounds_IsACertainLie_TwoClaimants_IsAConflict()
    {
        var repeat = Mind([]) with { Claims = [new ClaimedLetter(Ann, "crown", 1), new ClaimedLetter(Ann, "crown", 2)] };
        using (BotTuning.Use(On))
        {
            var d = BotPlayer.Logic(View(), Tags, repeat)!.Deductions.Should().ContainSingle(x => x.Kind == "repeat").Subject;
            d.Who.Should().Be(Ann);
            d.Strength.Should().Be(3, "брошенное письмо в руку не возвращается — это точная ложь");
            d.Text.Should().Contain("Аня").And.Contain("раунде 1").And.Contain("врёшь");
        }

        var conflict = Mind([]) with { Claims = [new ClaimedLetter(Ann, "crown", 2), new ClaimedLetter(Bob, "crown", 2)] };
        using (BotTuning.Use(On))
        {
            var d = BotPlayer.Logic(View(), Tags, conflict)!.Deductions.Should().ContainSingle(x => x.Kind == "conflict").Subject;
            d.Who.Should().BeNull("не знаем, кто из двоих врёт");
            d.Strength.Should().Be(1.2);
            d.Text.Should().Contain("Аня").And.Contain("Боб").And.Contain("кто-то из них врёт");
        }

        var sameRound = Mind([]) with { Claims = [new ClaimedLetter(Ann, "crown", 2), new ClaimedLetter(Ann, "crown", 2)] };
        using (BotTuning.Use(On))
            BotPlayer.Logic(View(), Tags, sameRound)!.Deductions.Should().NotContain(x => x.Kind == "repeat",
                "повторить заявление в том же раунде — не ложь");
    }
}
