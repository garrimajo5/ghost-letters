using System.Threading.RateLimiting;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;

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
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = 1;
            o.KnownNetworks.Clear();
            o.KnownProxies.Clear();
            // No trust-all fallback. Forwarding is enabled only with an explicit proxy address.
            foreach (var address in (configuration["Proxy:KnownProxies"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                o.KnownProxies.Add(IPAddress.Parse(address));
            if (o.KnownProxies.Count == 0) o.KnownProxies.Add(IPAddress.None);
        });
        services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 3 * 1024 * 1024);
        services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(o => o.Limits.MaxRequestBodySize = 3 * 1024 * 1024);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                var subject = http.User.FindFirst("sub")?.Value;
                var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var write = http.Request.Method is not ("GET" or "HEAD" or "OPTIONS");
                var upload = http.Request.Path == "/api/v1/media" || http.Request.Path == "/api/v1/me/avatar";
                var category = upload && write ? "upload" : write ? "write" : "read";
                var limit = category switch
                {
                    "upload" => configuration.GetValue("RateLimit:UploadsPerMinute", 10),
                    "write" => configuration.GetValue("RateLimit:WritesPerMinute", 120),
                    _ => configuration.GetValue("RateLimit:ReadsPerMinute", 600),
                };
                return RateLimitPartition.GetFixedWindowLimiter($"{category}:{subject ?? ip}",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1) });
            });
            o.AddPolicy(AuthPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) }));
        });
        return services;
    }
}
