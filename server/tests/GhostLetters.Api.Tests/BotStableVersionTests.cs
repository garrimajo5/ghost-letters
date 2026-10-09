using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

public sealed class BotStableVersionTests
{
    private static readonly Guid Me = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static PlayerView View => new(Guid.Parse("22222222-2222-2222-2222-222222222222"), 1, Phase.Discussion, 3, 3, DiscussionMode.FreeChat,
        [new(Category.Motive, ["knife", "rose", "boat"])], [], 0,
        [new(Me, 1, false, Role.Detective, false, 0)],
        new(Me, Role.Detective, [], []), null, 0, null, null, null, null, [], [nameof(ReadyNextRound)], null);

    [Fact]
    public void EqualEvidenceDoesNotChangeVersionWithRandomStream()
    {
        var versions = Enumerable.Range(0, 30).Select(seed =>
            BotPlayer.Plan(View, CardTags.Empty, BotMind.Neutral, new Random(seed))[0]);
        versions.Distinct().Should().ContainSingle("повторное чтение тех же улик не меняет версию");
    }

    [Fact]
    public void TiedVoteMatchesAnnouncedPlan()
    {
        var view = View;
        var plan = BotPlayer.Plan(view, CardTags.Empty, BotMind.Neutral, new Random(1))[0];
        view = view with { Phase = Phase.Voting, AllowedCommands = [nameof(CastVote)],
            Finale = new(new(0, VoteStageKind.Row, 0, 1, [0, 1, 2], []), 1, null, [], [], [], null, null, null, [], []) };
        foreach (var seed in Enumerable.Range(0, 30))
            ((CastVote)BotPlayer.Decide(view, new Random(seed), CardTags.Empty, BotMind.Neutral)!).Column.Should().Be(plan);
    }

    [Fact]
    public void NewEvidenceCanOverridePreviousTiePreference()
    {
        var view = View;
        var old = BotPlayer.Plan(view, CardTags.Empty, BotMind.Neutral, new Random(1))[0];
        var target = (old + 1) % 3;
        var tags = new CardTags(new Dictionary<string, HashSet<string>> {
            [view.Board[0].Cards[target]] = ["matching"], ["hint"] = ["matching"] });
        var updated = view with { Hints = [new(3, ["hint"])] };
        BotPlayer.Plan(updated, tags, BotMind.Neutral, new Random(2))[0].Should().Be(target);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TiePreferencesVaryAcrossBotsAndGames(bool changeGame)
    {
        var choices = Enumerable.Range(1, 20).Select(i =>
        {
            var id = Guid.Parse($"{i:x8}-0000-0000-0000-000000000000");
            var view = changeGame ? View with { GameId = id } : View with {
                Me = View.Me! with { Id = id }, Players = [new(id, 1, false, Role.Detective, false, 0)] };
            return BotPlayer.Plan(view, CardTags.Empty, BotMind.Neutral, new Random(1))[0];
        });
        choices.Distinct().Count().Should().BeGreaterThan(1, "устойчивость не означает одинаковую карту для всех ботов и партий");
    }
}
