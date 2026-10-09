using System.Threading.RateLimiting;
using Microsoft.AspNetCore.SignalR;

namespace GhostLetters.Api.Realtime;

/// <summary>HTTP limits cover the handshake only; invocations share a per-user budget across connections.</summary>
public sealed class HubRateLimitFilter : IHubFilter, IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public HubRateLimitFilter(IConfiguration configuration)
    {
        var limit = configuration.GetValue("RateLimit:HubCallsPerMinute", 120);
        _limiter = PartitionedRateLimiter.Create<string, string>(user => RateLimitPartition.GetFixedWindowLimiter(user,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1) }));
    }

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        using var lease = _limiter.AttemptAcquire(context.Context.UserIdentifier ?? context.Context.ConnectionId);
        if (!lease.IsAcquired) throw new HubException("RATE_LIMIT: Слишком много действий. Подождите немного.");
        return await next(context);
    }

    public void Dispose() => _limiter.Dispose();
}
