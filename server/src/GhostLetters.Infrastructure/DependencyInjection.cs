using GhostLetters.Infrastructure.Auth;
using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Lobbies;
using GhostLetters.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GhostLetters.Infrastructure;

public static class DependencyInjection
{
    /// <summary>БД, авторизация и прочая инфраструктура. Строка подключения — ConnectionStrings:Default.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);

        var connectionString = configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<GhostLettersDbContext>(o => ConfigureDb(o, connectionString));
            services.AddHealthChecks().AddDbContextCheck<GhostLettersDbContext>("postgres");
        }
        else
        {
            services.AddHealthChecks();
        }

        services.AddJwtAuth(configuration);
        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<CardCatalog>();
        services.AddScoped<CardAdminService>();
        services.AddScoped<CardTagStore>();
        services.AddScoped<LobbyService>();
        services.AddScoped<SettingsPresetService>();
        services.AddScoped<GameService>();
        services.AddScoped<GameRecorder>();
        services.AddScoped<BotService>();
        services.AddScoped<Bots.BotAdminService>();
        services.AddScoped<Bots.BotRelationshipService>();
        services.AddSingleton(sp => CardTags.FromConfiguration(
            sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<ILoggerFactory>().CreateLogger<CardTags>()));
        services.AddScoped<ChatService>();
        services.AddScoped<MediaLimits>();
        services.AddScoped<NotesService>();
        services.AddScoped<ProfileService>();
        services.AddSingleton<IMediaStorage, FileMediaStorage>();
        services.TryAddSingleton<IRealtimeNotifier, NullRealtimeNotifier>();
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddHostedService<GameTimerService>();
            services.AddHostedService<BotHostedService>();
            services.AddHostedService<MediaCleanupService>();
        }

        return services;
    }

    public static void ConfigureDb(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();

    /// <summary>Применить миграции, если включено Database:MigrateOnStartup.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Database:MigrateOnStartup"))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetService<GhostLettersDbContext>();
        if (db is not null)
        {
            await db.Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<CardCatalog>().EnsureSeededAsync(CancellationToken.None);
        }
    }
}
