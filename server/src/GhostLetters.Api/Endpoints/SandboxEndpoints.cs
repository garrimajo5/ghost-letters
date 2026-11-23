using GhostLetters.Infrastructure.Auth;
using GhostLetters.Infrastructure.Games;
namespace GhostLetters.Api.Endpoints;

public static class SandboxEndpoints
{
    public static void MapSandbox(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/admin/sandbox").RequireAuthorization().WithTags("Admin sandbox");
        group.MapGet("", (HttpContext h, BotSandboxService s, CancellationToken ct) => s.ListAsync(h.User.UserId(), ct));
        group.MapPost("", (SandboxRequest request, HttpContext h, BotSandboxService s, CancellationToken ct) => s.RunAsync(h.User.UserId(), request, ct));
        group.MapPost("/trace", (SandboxRequest request, HttpContext h, BotSandboxService s, CancellationToken ct) => s.TraceAsync(h.User.UserId(), request, ct));
        // Лаборатория мыслей: своя раскладка и подсказки — что думает каждый характер (без партии и речи).
        group.MapGet("/lab/names", (HttpContext h, BotLabService s, CancellationToken ct) => s.NamesAsync(h.User.UserId(), ct));
        group.MapPost("/lab/deal", (LabDealRequest request, HttpContext h, BotLabService s, CancellationToken ct) => s.DealAsync(h.User.UserId(), request, ct));
        group.MapPost("/lab/think", (LabThinkRequest request, HttpContext h, BotLabService s, CancellationToken ct) => s.ThinkAsync(h.User.UserId(), request, ct));
        group.MapPost("/lab/suggest", (LabSuggestRequest request, HttpContext h, BotLabService s, CancellationToken ct) => s.SuggestAsync(h.User.UserId(), request, ct));
        // Перенос завершённой партии в лабораторию: поле, истина и подсказки, как были за столом.
        group.MapGet("/lab/game/{id:guid}", (Guid id, HttpContext h, BotLabService s, CancellationToken ct) => s.GameAsync(h.User.UserId(), id, ct));
        group.MapGet("/{id:guid}", (Guid id, HttpContext h, BotSandboxService s, CancellationToken ct) => s.ReplayAsync(h.User.UserId(), id, ct));
        group.MapGet("/{id:guid}/steps/{index:int}", (Guid id, int index, Guid? viewer, bool? reveal, HttpContext h, BotSandboxService s, CancellationToken ct) =>
            s.StepAsync(h.User.UserId(), id, index, viewer, reveal ?? false, ct));
        group.MapDelete("/{id:guid}", async (Guid id, HttpContext h, BotSandboxService s, CancellationToken ct) => { await s.DeleteAsync(h.User.UserId(), id, ct); return Results.NoContent(); });
    }
}
