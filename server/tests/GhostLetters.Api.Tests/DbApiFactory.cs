using GhostLetters.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace GhostLetters.Api.Tests;

/// <summary>API с настоящей базой в контейнере; миграции применяются при старте. Время управляемое.</summary>
public sealed class DbApiFactory(PostgresFixture postgres) : WebApplicationFactory<Program>
{
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Jwt:SigningKey", ApiFactory.TestSigningKey);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }

    public async Task<T> WithDbAsync<T>(Func<GhostLettersDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<GhostLettersDbContext>());
    }
}
