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
        group.MapGet("/{id:guid}", (Guid id, HttpContext h, BotSandboxService s, CancellationToken ct) => s.ReplayAsync(h.User.UserId(), id, ct));
        group.MapGet("/{id:guid}/steps/{index:int}", (Guid id, int index, Guid? viewer, bool? reveal, HttpContext h, BotSandboxService s, CancellationToken ct) =>
            s.StepAsync(h.User.UserId(), id, index, viewer, reveal ?? false, ct));
        group.MapDelete("/{id:guid}", async (Guid id, HttpContext h, BotSandboxService s, CancellationToken ct) => { await s.DeleteAsync(h.User.UserId(), id, ct); return Results.NoContent(); });
    }
}
