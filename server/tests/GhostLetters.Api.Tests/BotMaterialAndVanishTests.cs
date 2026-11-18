using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

/// <summary>
/// Материал — отдельный слабый признак (разметка по картинке может ошибаться), едва видные детали почти не
/// связывают карты, а исчезнувшее письмо ставит под сомнение все ясно связанные карты, а не только самую похожую.
/// </summary>
public sealed class BotMaterialAndVanishTests
{
    private static readonly Guid Me = Guid.NewGuid(), Ghost = Guid.NewGuid();

    [Fact]
    public void SharedMaterialAlone_IsAWeakLink_AndIsExplainedAsMaterial()
    {
        var tags = CardTags.Parse("""
            {"knife":["knife","metal","sharp","weapon"],"sword":["sword","sharp","weapon","metal"],
             "pan":["pan","metal"],"pot":["pot","metal"],"rose":["rose","flower"]}
            """);
        var materialOnly = tags.Similarity("pan", "pot");
        materialOnly.Should().BeGreaterThan(0, "общий материал — не ноль: человек и его заметит");
        materialOnly.Should().BeLessThan(.2, "но «оба металлические» не связывает карты, как общий предмет");
        tags.Similarity("pan", "rose").Should().Be(0);
        tags.Similarity("knife", "sword").Should().BeGreaterThan(.4, "общий предмет и назначение связывают сильно");

        tags.Explain("pan", "pot", (.8, .15, .05), 0).Should().Be("по материалу: металл");
        tags.Explain("knife", "sword", (.8, .15, .05), 0).Should().Contain("по смыслу")
            .And.NotContain("по материалу", "материал вспоминают, только когда другой связи нет");
    }

    [Fact]
    public void WeakDetail_BarelyLinksCards_EvenForDetailFocusedBot()
    {
        var tags = CardTags.Parse("""{"cabin":["cabin","house","wood"],"ship":["ship","sea","wood"],"hut":["hut","house","wood"]}""")
            .WithDetails("""
                {"cabin":[{"tag":"flower","weight":0.05,"label":"едва видный цветок"}],
                 "ship":[{"tag":"flower","weight":0.05,"label":"цветочек на борту"}]}
                """);
        var attention = (Meaning: 1.0, Shape: 0.0, Color: 0.0);
        tags.Similarity("cabin", "ship", attention, 1).Should().BeLessThan(.2,
            "едва видная деталь (вес .05) не связывает карты, как главный предмет");
        tags.Similarity("cabin", "hut", attention, 1).Should().BeGreaterThan(.3,
            "а общий предмет «дом» — заметно сильнее слабой детали");
    }

    [Fact]
    public void WouldReveal_ClearLink_RevealsForEveryLinkedCard()
    {
        GhostCluePolicy.WouldReveal(.5, .4).Should().BeTrue("связь заметна и не сильно уступает лучшей альтернативе");
        GhostCluePolicy.WouldReveal(.2, .4).Should().BeFalse("уступает лучшей альтернативе — письмо увело бы к ней");
        GhostCluePolicy.WouldReveal(.1, .05).Should().BeFalse("ниже планки заметности");
    }

    // Поле: cat | dog | stone; моё письмо wolf в прошлом раунде не открыли.
    private static PlayerView VanishView() => new(
        Guid.NewGuid(), 2, Phase.Discussion, 2, 3, DiscussionMode.FreeChat,
        [new BoardRowView(Category.Motive, ["cat", "dog", "stone"])], [], 0,
        [new PlayerInfoView(Ghost, 0, true, Role.Ghost, false, 5), new PlayerInfoView(Me, 1, false, Role.Detective, false, 5)],
        new MeView(Me, Role.Detective, [], [new MyLetterView(1, "wolf", false)]), null, 0, null, null, null, null, [], [], null);

    [Fact]
    public void VanishedAnimal_CastsDoubtOnEveryClearlyLinkedAnimal_NotJustTheClosest()
    {
        var tags = CardTags.Parse("""
            {"wolf":["wolf","animal","wild"],"cat":["cat","animal","fur"],"dog":["dog","animal","pet"],"stone":["stone"]}
            """);
        var mind = BotMind.Neutral with
        {
            Personality = new BotPersonality { Meaning = 1, Shape = 0, Color = 0, Details = 0, SecondaryMeanings = 0, Variability = 0 },
        };
        var view = VanishView();

        double Cat, Dog, Stone;
        using (BotTuning.Use(BotTuning.Default with { VanishWide = true }))
        {
            Cat = BotPlayer.Evidence(view, tags, mind, "cat");
            Dog = BotPlayer.Evidence(view, tags, mind, "dog");
            Stone = BotPlayer.Evidence(view, tags, mind, "stone");
        }
        Cat.Should().BeLessThan(Stone, "кинули животное, письмо исчезло — животные под сомнением");
        Dog.Should().BeLessThan(Stone);
        Dog.Should().BeApproximately(Cat * .8, .02,
            "под сомнением все ясно связанные животные (сходства .25 и .2), а не только самое похожее");

        using (BotTuning.Use(BotTuning.Default with { VanishWide = false }))
        {
            BotPlayer.Evidence(view, tags, mind, "cat").Should().BeApproximately(Cat, 1e-9,
                "для самой похожей карты штраф тот же в обоих чтениях");
            BotPlayer.Evidence(view, tags, mind, "dog").Should().BeGreaterThan(Cat * .8 + .02,
                "узкое чтение «объясняет» письмо лучшим совпадением и почти прощает остальных");
        }
    }

    [Fact]
    public void Deck_DropsDuplicateNames_SeededChoiceIsReproducible()
    {
        var tags = CardTags.Parse("""{"a":["bird"],"b":["bird"],"c":["cat"]}""")
            .WithNames("""{"a":["ворон"],"b":["Ворон"],"c":["кошка"]}""");
        var deck = CardCatalog.WithoutDuplicateNames(["a", "b", "c"], tags, 42);
        deck.Should().HaveCount(2).And.Contain("c");
        deck.Where(x => x is "a" or "b").Should().ContainSingle("два «ворона» в одной партии путают стол");
        CardCatalog.WithoutDuplicateNames(["a", "b", "c"], tags, 42).Should().Equal(deck, "жребий по сиду воспроизводим");
        CardCatalog.WithoutDuplicateNames(["a", "b", "c"], CardTags.Parse("""{"a":["bird"],"b":["bird"],"c":["cat"]}"""), 42)
            .Should().HaveCount(3, "карты без названия не конфликтуют");
    }

    [Fact]
    public void SearchByTag_FindsByKeyLabelMeaningDetailAndName()
    {
        var tags = CardTags.Parse("""{"knife":["knife","metal","sharp","weapon"],"rose":["rose","flower","love"],"spoon":["spoon","metal"]}""")
            .WithNames("""{"knife":["нож"],"rose":["роза"]}""");
        tags.SearchByTag("металл").Should().Equal(["knife", "spoon"], "поиск по словарной подписи признака");
        tags.SearchByTag("sharp").Should().Equal(["knife"], "поиск по ключу метки");
        tags.SearchByTag("оружие").Should().Equal(["knife"], "поиск по подписи смысла");
        tags.SearchByTag("роз").Should().Equal(["rose"], "поиск по названию карты");
        tags.SearchByTag("").Should().BeEmpty();
        tags.SearchByTag("несуществующее").Should().BeEmpty();
    }
}
