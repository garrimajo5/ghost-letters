using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

/// <summary>Боты 3.0, этап 1: модель стола и модель Призрака.</summary>
public sealed class TableModelTests
{
    private static readonly CardTags Tags = new(new Dictionary<string, HashSet<string>>
    {
        // Мотив: пистолет, нож, роза, книга. Место: море, лес, меч, дом.
        ["gun"] = ["weapon", "metal", "gun"], ["knife"] = ["weapon", "metal", "blade"], ["rose"] = ["flower", "plant"], ["book"] = ["paper", "book"],
        ["sea"] = ["water", "sea"], ["forest"] = ["tree", "plant"], ["sword"] = ["weapon", "metal", "blade"], ["house"] = ["building", "house"],
        // Письма.
        ["pistol"] = ["weapon", "metal", "gun"], ["dagger"] = ["weapon", "metal", "blade"], ["lily"] = ["flower", "plant"],
        ["brick"] = ["building", "house"], ["cloud"] = ["sky"],
    });

    private static readonly IReadOnlyList<IReadOnlyList<string>> Board = [["gun", "knife", "rose", "book"], ["sea", "forest", "sword", "house"]];

    private static TableModel Uniform() => TableModel.Uniform(Board, (a, b) => Tags.Similarity(a, b));

    [Fact]
    public void RevealRaises_AndVanishLowers_TheCardTheLetterPointsTo()
    {
        var start = Uniform();
        start.P("rose").Should().BeApproximately(.25, 1e-9);
        start.With("lily", true).P("rose").Should().BeGreaterThan(.5);
        start.With("lily", false).P("rose").Should().BeLessThan(.15);
        // Недоверие к источнику ослабляет вывод.
        start.With("lily", false, .3).P("rose").Should().BeGreaterThan(start.With("lily", false).P("rose"));
        // Письмо ни на что не похоже — ничего не узнали.
        start.With("cloud", true).P("rose").Should().BeApproximately(.25, .01);
    }

    [Fact]
    public void BroadLetter_IsWorthMoreEarly_ThanAfterItsRowIsClear()
    {
        var start = Uniform();
        start.InfoGain("pistol").Should().BeGreaterThan(start.InfoGain("cloud") + .1, "оружие проверяет сразу несколько карт");
        start.InfoGain("brick").Should().BeGreaterThan(start.InfoGain("cloud"));
        // Ряды с оружием уже ясны — та же широкая проверка почти ничего не даст.
        var clear = start.With("pistol", true).With("pistol", true).With("dagger", false).With("lily", false).With("brick", false);
        clear.InfoGain("pistol").Should().BeLessThan(start.InfoGain("pistol"));
    }

    [Fact]
    public void ModelGhost_OpensWhatLeadsToTruth_AndNotWhatLeadsAway()
    {
        var ghost = Guid.NewGuid();
        var view = new PlayerView(Guid.NewGuid(), 1, Phase.GhostPick, 1, 3, DiscussionMode.FreeChat,
            [new(Category.Motive, ["gun", "knife", "rose", "book"]), new(Category.Place, ["sea", "forest", "sword", "house"])], [], 0,
            [new(ghost, 0, true, Role.Ghost, false, 0), new(Guid.NewGuid(), 1, false, null, false, 0)],
            new(ghost, Role.Ghost, [], []), [0, 3], 0, null, null, null, null, [], [], null);
        var mind = BotMind.Neutral with { Personality = new BotPersonality { Meaning = 1, Shape = 0, Color = 0, Risk = 0 } };
        using (BotTuning.Use(BotTuning.Default with { GhostModel = true }))
        {
            // Истина: пистолет и дом. Кирпич ведёт к дому, кинжал — к ножу и мечу (ложные), облако — ни к чему.
            GhostCluePolicy.Choose(view, Tags, mind, ["dagger", "brick", "cloud"]).Should().Equal("brick");
            GhostCluePolicy.Choose(view, Tags, mind, ["pistol", "dagger", "brick"]).Should().BeEquivalentTo(["pistol", "brick"]);
            GhostCluePolicy.Choose(view, Tags, mind, ["dagger", "cloud"]).Should().BeEmpty("уводящее письмо лучше не открывать");
        }
    }
}
