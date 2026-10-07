using System.Text.Json.Serialization;
using GhostLetters.Api.Endpoints;
using GhostLetters.Api.Realtime;
using GhostLetters.Application;
using GhostLetters.Domain.Roles;
using GhostLetters.Domain.Rules;
using GhostLetters.Infrastructure;
using GhostLetters.Infrastructure.Games;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

// Реалтайм регистрируется до инфраструктуры: она подставляет заглушку, только если уведомителя нет.
builder.Services.AddSingleton<IRealtimeNotifier, HubNotifier>();
builder.Services.AddSingleton<IUserIdProvider, SubUserIdProvider>();
builder.Services.AddSignalR(o => o.EnableDetailedErrors = builder.Environment.IsDevelopment())
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

// Веб-версия клиента при отладке (flutter run -d chrome) открывается с другого порта — разрешаем любой origin в Development.
builder.Services.AddCors(o => o.AddPolicy("dev", p => p
    .SetIsOriginAllowed(_ => true)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.UseCors("dev");
}

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
api.MapLobbies();
api.MapGames();
api.MapSocial();
app.MapHub<PlayHub>(PlayHub.Path);

await app.Services.MigrateDatabaseAsync(app.Configuration);
await app.RunAsync();

/// <summary>Нужен для WebApplicationFactory в интеграционных тестах.</summary>
public partial class Program
{
}
