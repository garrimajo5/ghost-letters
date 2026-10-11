using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

public sealed class ReactionLimiterTests
{
    [Fact]
    public void Burst_ThenBlocked_ThenFreeAfterWindow()
    {
        var limiter = new ReactionLimiter();
        var game = Guid.NewGuid();
        var user = Guid.NewGuid();
        var t = DateTimeOffset.Parse("2026-10-11T10:00:00Z");
        for (var i = 0; i < ReactionLimiter.Burst; i++) Assert.True(limiter.TryAcquire(game, user, t.AddMilliseconds(i * 10)));
        Assert.False(limiter.TryAcquire(game, user, t.AddSeconds(1)));
        // Другой игрок и другая партия — свои лимиты.
        Assert.True(limiter.TryAcquire(game, Guid.NewGuid(), t.AddSeconds(1)));
        Assert.True(limiter.TryAcquire(Guid.NewGuid(), user, t.AddSeconds(1)));
        Assert.True(limiter.TryAcquire(game, user, t + ReactionLimiter.Window + TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void OnlyFixedEmojiSet() =>
        Assert.All(ReactionService.Allowed, e => Assert.InRange(e.Length, 1, 4));
}
