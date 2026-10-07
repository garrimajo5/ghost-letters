using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GhostLetters.Infrastructure.Persistence;

/// <summary>Для dotnet ef: миграции создаются без запуска API и без подключения к базе.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GhostLettersDbContext>
{
    public GhostLettersDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GhostLettersDbContext>();
        DependencyInjection.ConfigureDb(options, "Host=localhost;Database=ghost_letters;Username=ghost;Password=ghost");
        return new GhostLettersDbContext(options.Options);
    }
}
