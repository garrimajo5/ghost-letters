using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GhostLetters.Api.Tests;

/// <summary>API без базы: окружение Testing, строка подключения пустая.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string TestSigningKey = "test-signing-key-0123456789abcdef-0123456789";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", string.Empty);
        builder.UseSetting("Jwt:SigningKey", TestSigningKey);
    }
}
