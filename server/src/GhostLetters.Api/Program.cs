using GhostLetters.Api.Endpoints;
using GhostLetters.Application;
using GhostLetters.Domain.Roles;
using GhostLetters.Domain.Rules;
using GhostLetters.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health");

var api = app.MapGroup("/api/v1");

api.MapGet("/version", () => Results.Ok(new { name = ApplicationInfo.Name, api = ApplicationInfo.ApiVersion }))
    .WithName("GetVersion");

// Предпросмотр состава ролей для экрана создания лобби.
api.MapGet("/rules/roles", (int players, bool? killer, bool? witness, bool? expert, bool? blackmailer, ImitatorMode? imitator) =>
    {
        var options = new RoleOptions(
            KillerEnabled: killer ?? true,
            UseWitness: witness ?? true,
            UseExpert: expert ?? true,
            UseBlackmailer: blackmailer ?? false,
            Imitator: imitator ?? ImitatorMode.None);
        try
        {
            var roles = RoleTable.Compose(players, options);
            return Results.Ok(new
            {
                players,
                cooperative = RoleTable.IsCooperative(roles),
                rounds = GameDefaults.Rounds(players),
                roles = roles.Select(r => r.ToString()),
            });
        }
        catch (ArgumentException e)
        {
            return Results.Problem(e.Message, statusCode: StatusCodes.Status400BadRequest, title: "VALIDATION");
        }
    })
    .WithName("PreviewRoles");

api.MapAuth();

await app.Services.MigrateDatabaseAsync(app.Configuration);
await app.RunAsync();

/// <summary>Нужен для WebApplicationFactory в интеграционных тестах.</summary>
public partial class Program
{
}
