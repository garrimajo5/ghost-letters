using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

/// <summary>
/// Призрак слушает стол: мирные сказали «роза» (а истина — нож) и «кот» (верно). Письмо, разводящее спор о ноже,
/// для Призрака ценнее письма про кота, о котором стол и так прав. Голоса чёрных Призрак не слушает.
/// </summary>
public sealed class GhostListensTests
{
    private static readonly Guid Ghost = Guid.NewGuid(), Ann = Guid.NewGuid(), Bob = Guid.NewGuid(), Eve = Guid.NewGuid();

    private static readonly CardTags Tags = new(new Dictionary<string, HashSet<string>>
    {
        ["knife"] = ["knife", "metal", "weapon"], ["rose"] = ["rose", "flower", "red"],
        ["boat"] = ["boat", "water"], ["cat"] = ["cat", "animal", "fur"],
        ["sword"] = ["sword", "metal", "weapon"], ["kitten"] = ["kitten", "animal", "fur"],
    });

    private static PlayerView View(Role? eve = null) => new(
        Guid.NewGuid(), 1, Phase.GhostPick, 2, 4, DiscussionMode.FreeChat,
        [new BoardRowView(Category.Motive, ["knife", "rose"]), new BoardRowView(Category.Place, ["boat", "cat"])],
        [], 0,
        [
            new PlayerInfoView(Ghost, 0, true, Role.Ghost, false, 5),
            new PlayerInfoView(Ann, 1, false, null, false, 5),
            new PlayerInfoView(Bob, 2, false, null, false, 5),
            new PlayerInfoView(Eve, 3, false, eve, false, 5),
        ],
        new MeView(Ghost, Role.Ghost, ["sword", "kitten"], []), [0, 1], 0, null, null, null, null, [], [], null);

    private static BotMind Mind(params ChatOpinion[] opinions) => BotMind.Neutral with { Opinions = opinions };

    [Fact]
    public void Voices_LatestPublicVersionPerRow_IgnoringChecksAndKnownBlack()
    {
        var mind = Mind(
            new ChatOpinion(Ann, "knife", 1), new ChatOpinion(Ann, "rose", 1), // передумала — в счёт последнее
            new ChatOpinion(Bob, "cat", 1), new ChatOpinion(Bob, "knife", .5, IsCheck: true),
            new ChatOpinion(Eve, "boat", 1));
        var votes = GhostCluePolicy.Voices(View(), mind);
        votes[0].Should().Equal(0, 1);
        votes[1].Should().Equal(.5, .5);
        GhostCluePolicy.Voices(View(Role.Killer), mind)[1].Should().Equal(0, 1); // Убийцу Призрак знает и не слушает
    }

    [Fact]
    public void WrongRow_WeighsMore_InGhostsChoice()
    {
        var view = View();
        var mind = Mind(new ChatOpinion(Ann, "rose", 1), new ChatOpinion(Bob, "cat", 1));
        var votes = GhostCluePolicy.Voices(view, mind);
        double Gain(string letter, double listen)
        {
            double S(IReadOnlyList<string> h) => GhostCluePolicy.TableScore(view, [0, 1], h, [], (a, b) => Tags.Similarity(a, b), 8, votes, listen);
            return S([letter]) - S([]);
        }
        // Слушая стол, Призрак ценит «меч» (спор о ноже) выше «котёнка» сильнее, чем не слушая.
        (Gain("sword", 1) - Gain("kitten", 1)).Should().BeGreaterThan(Gain("sword", 0) - Gain("kitten", 0));
        Gain("sword", 1).Should().BeGreaterThan(Gain("kitten", 1));
    }
}
