using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Infrastructure.Persistence;

/// <summary>Контекст БД. Таблицы добавляются в PR «БД и авторизация».</summary>
public sealed class GhostLettersDbContext(DbContextOptions<GhostLettersDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");
        base.OnModelCreating(modelBuilder);
    }
}
