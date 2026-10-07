using GhostLetters.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GhostLetters.Infrastructure;

public static class DependencyInjection
{
    /// <summary>БД и прочая инфраструктура. Строка подключения — ConnectionStrings:Default.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<GhostLettersDbContext>(o => o.UseNpgsql(connectionString));
            services.AddHealthChecks().AddDbContextCheck<GhostLettersDbContext>("postgres");
        }
        else
        {
            services.AddHealthChecks();
        }

        return services;
    }
}
