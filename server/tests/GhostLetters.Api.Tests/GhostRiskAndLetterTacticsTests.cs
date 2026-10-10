using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

public sealed class GhostRiskAndLetterTacticsTests
{
    private static readonly Guid Me = Guid.NewGuid(), Other = Guid.NewGuid(), Ghost = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static readonly CardTags Tags = new(new Dictionary<string, HashSet<string>> {
        ["truth"] = ["weapon", "metal"], ["false"] = ["flower", "plant"],
        ["clear"] = ["weapon", "metal"], ["ambiguous"] = ["weapon", "metal", "flower", "plant"],
        ["empty"] = ["water"], ["misleading"] = ["weapon", "flower", "plant"] });
    private static BotMind Mind(double risk) => BotMind.Neutral with {
        Personality = new BotPersonality { Meaning = 1, Shape = 0, Color = 0, Risk = risk, Memory = 1, Compromise = 1, Variability = 0 },
        Names = new Dictionary<Guid, string> { [Me] = "Я", [Other] = "Аня" }
    };
    private static PlayerView View(Role role = Role.Ghost) => new(Guid.NewGuid(), 1, Phase.Discussion, 1, 3, DiscussionMode.FreeChat,
        [new(Category.Motive, ["truth", "false"])], [new(1, ["clear"])], 0,
        [new(Ghost, 0, true, Role.Ghost, false, 0), new(Me, 1, false, role, false, 0), new(Other, 2, false, null, false, 0)],
        new(Me, role, ["clear", "ambiguous", "empty"], [new(1, "clear", true)]), [0], 0, null, null, null, null, [], [], null);

    [Fact]
    public void CautiousGhostRejectsAmbiguityButRiskyGhostMayRevealIt()
    {
        GhostCluePolicy.Choose(View(), Tags, Mind(0), ["clear", "ambiguous", "empty"]).Should().Equal("clear");
        GhostCluePolicy.Assess(View(), Tags, Mind(1), "ambiguous").Reveal.Should().BeTrue();
        GhostCluePolicy.Assess(View(), Tags, Mind(1), "empty").Reveal.Should().BeFalse();
        GhostCluePolicy.Assess(View(), Tags, Mind(0), "misleading").Reveal.Should().BeFalse();
        GhostCluePolicy.Choose(View(), Tags, Mind(0), ["ambiguous", "empty"]).Should().BeEmpty();
    }

    [Fact]
    public void LateRiskRequiresPublicWrongTheories_AndIgnoresKnownEvil()
    {
        var mind = Mind(0) with { Opinions = [new(Other, "false", 1)] };
        GhostCluePolicy.Assess(View(), Tags, mind, "ambiguous").Reveal.Should().BeFalse();
        GhostCluePolicy.Assess(View() with { TotalRounds = 1 }, Tags, mind, "ambiguous").Reveal.Should().BeTrue();
        var late = View() with { Round = 3 };
        GhostCluePolicy.Assess(late, Tags, mind, "ambiguous").Reveal.Should().BeTrue();
        GhostCluePolicy.Assess(late, Tags, Mind(0), "ambiguous").Reveal.Should().BeFalse();
        GhostCluePolicy.Assess(late, Tags, mind with { Opinions = [new(Other, "truth", 1)] }, "ambiguous").Reveal.Should().BeFalse();
        late = late with { Players = late.Players.Select(p => p.Id == Other ? p with { KnownRole = Role.Killer } : p).ToList() };
        GhostCluePolicy.Assess(late, Tags, mind, "ambiguous").Reveal.Should().BeFalse();
        GhostCluePolicy.Choose(View(Role.Detective), Tags, mind, ["clear"]).Should().BeEmpty();
    }

    [Fact]
    public void EvilCanStealOnlyUnclaimedPublicLetter_AndRiskAffectsTiming()
    {
        var view = View(Role.Killer) with { Me = View(Role.Killer).Me! with { Letters = [] } };
        var low = Mind(0);
        Enumerable.Range(0, 100).Select(s => BotLetterTactics.Compose(view, low, [], new Random(s))).Should().OnlyContain(x => x == null);
        Enumerable.Range(0, 100).Select(s => BotLetterTactics.Compose(view, Mind(1), [], new Random(s))).Should().Contain(x => x != null);
        List<DiscussionLine> spoken = [new(Other, "Моя версия", [], Now)];
        Enumerable.Range(0, 100).Select(s => BotLetterTactics.Compose(view, low, spoken, new Random(s))).Should().Contain(x => x != null);
        spoken.Add(new(Other, "Моё письмо", ["clear"], Now.AddSeconds(1), Notes: ["кидал эту"]));
        Enumerable.Range(0, 100).Select(s => BotLetterTactics.Compose(view, Mind(1), spoken, new Random(s))).Should().OnlyContain(x => x == null);
        spoken = [new(Other, "Я отправлял вот эту", ["clear"], Now)];
        Enumerable.Range(0, 100).Select(s => BotLetterTactics.Compose(view, Mind(1), spoken, new Random(s))).Should().OnlyContain(x => x == null);
        var allies = view with { Players = view.Players.Select(p => p.Id == Other ? p with { KnownRole = Role.Accomplice } : p).ToList() };
        Enumerable.Range(0, 100).Select(s => BotLetterTactics.Compose(allies, low, [], new Random(s))).Should().Contain(x => x != null);
    }

    [Theory]
    [InlineData(Role.Detective)]
    [InlineData(Role.Killer)]
    public void CunningPlayerCanWaitCatchOrReclaim_WithoutRepeating(Role role)
    {
        var view = View(role);
        var mind = Mind(1) with { Accusations = [new(Me, Other, .9)] };
        var first = BotLetterTactics.Compose(view, mind, [], new Random(1))!.Value;
        first.Text.Should().Be(BotLetterTactics.WaitLine);
        List<DiscussionLine> messages = [new(Me, first.Text, first.Cards, Now, Notes: first.Notes)];
        BotLetterTactics.Pending(Me, messages).Should().BeTrue();
        BotLetterTactics.Compose(view, mind, messages, new Random(1), Now.AddSeconds(10)).Should().BeNull();
        var reclaim = BotLetterTactics.Compose(view, mind, messages, new Random(1), Now.AddSeconds(46))!.Value;
        reclaim.Cards.Should().Equal("clear");
        messages.Add(new(Other, "Это моё", ["clear"], Now.AddSeconds(15), Notes: ["кидал эту"]));
        var caught = BotLetterTactics.Compose(view, mind, messages, new Random(1), Now.AddSeconds(20))!.Value;
        caught.Text.Should().Contain("ты назвал моё письмо своим");
        messages.Add(new(Me, caught.Text, caught.Cards, Now.AddSeconds(20), Notes: caught.Notes));
        BotLetterTactics.Pending(Me, messages).Should().BeFalse();
        BotLetterTactics.Compose(view, mind, messages, new Random(1), Now.AddSeconds(50)).Should().BeNull();
    }

    [Fact]
    public void DebriefIsSilentUntilResult_ThenExplainsRevealedAndWithheldLetters()
    {
        var state = new GameState { Players = [new() { Id = Ghost, Role = Role.Ghost }, new() { Id = Other, Role = Role.Detective }],
            Board = [new() { Category = Category.Motive, Cards = ["truth", "false"] }], Truth = [0],
            Letters = [new() { From = Other, Round = 1, CardId = "clear", Revealed = true }, new() { From = Other, Round = 2, CardId = "empty" }] };
        GhostDebrief.Compose(state, Tags, Mind(0)).Should().BeEmpty();
        state.Result = new();
        var report = GhostDebrief.Compose(state, Tags, Mind(0));
        report.Should().HaveCount(2);
        report[0].Text.Should().Contain("Открыл").And.Contain("спасибо");
        report[1].Text.Should().Contain("закрытым").And.Contain("слабая");
        report.Should().OnlyContain(x => x.Text.Length <= 1000 && x.Cards.Count <= 5 && x.Notes.All(n => n.Length <= 30));
    }
}
