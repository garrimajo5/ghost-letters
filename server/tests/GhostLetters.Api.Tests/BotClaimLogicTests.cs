using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

/// <summary>
/// Логика заявлений: бот перебирает, кто за столом чёрный, и из «проверял — исчезло» делает человеческие выводы.
/// </summary>
public sealed class BotClaimLogicTests
{
    private static readonly Guid Ghost = Guid.NewGuid(), Me = Guid.NewGuid(),
        Ann = Guid.NewGuid(), Bob = Guid.NewGuid(), Cid = Guid.NewGuid(), Dan = Guid.NewGuid(), Eva = Guid.NewGuid(), Fay = Guid.NewGuid();

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
    public void ByDefault_OnlySpeech_EvidenceUnchanged()
    {
        var mind = Mind([Check(Ann, "ring", "crown")]);
        BotPlayer.Logic(View(), Tags, mind).Should().NotBeNull("по умолчанию логика рассуждает вслух");
        using (BotTuning.Use(BotTuning.Default with { ClaimLogic = false }))
            BotPlayer.Logic(View(), Tags, mind).Should().BeNull();
        // Решения — как без логики: вес 0.
        var plain = BotTuning.Default with { ClaimLogic = false };
        double off;
        using (BotTuning.Use(plain)) off = BotPlayer.Evidence(View(), Tags, mind, "ring");
        BotPlayer.Evidence(View(), Tags, mind, "ring").Should().BeApproximately(off, 1e-9);
        using (BotTuning.Use(BotTuning.Default with { ClaimLogic = true, ClaimLogicWeight = 0 }))
            BotPlayer.Evidence(View(), Tags, mind, "wallet").Should().BeApproximately(BotPlayer.Evidence(View(), Tags, Mind([]), "wallet"), 1e-9);
    }
}
