using System.Threading.RateLimiting;

namespace GhostLetters.Api.Hosting;

/// <summary>Защита публичного сервера: лимит запросов на вход и проверка настроек продакшена.</summary>
public static class Hardening
{
    public const string AuthPolicy = "auth";

    public static IServiceCollection AddHardening(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsProduction() && string.IsNullOrWhiteSpace(configuration.GetConnectionString("Default")))
        {
            throw new InvalidOperationException("В продакшене нужна строка подключения ConnectionStrings__Default.");
        }

        var perMinute = configuration.GetValue("RateLimit:AuthPerMinute", 30);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(AuthPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) }));
        });
        return services;
    }
}
