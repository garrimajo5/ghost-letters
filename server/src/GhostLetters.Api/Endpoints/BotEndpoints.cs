using GhostLetters.Infrastructure.Auth;
using GhostLetters.Infrastructure.Bots;

namespace GhostLetters.Api.Endpoints;

/// <summary>Кабинет ботов (только админ) и список ботов для выбора в лобби.</summary>
public static class BotEndpoints
{
    public static RouteGroupBuilder MapBots(this RouteGroupBuilder api)
    {
        api.MapGet("/bots", (BotAdminService bots, CancellationToken ct) => bots.PublicListAsync(ct))
            .RequireAuthorization().WithTags("Bots").WithName("ListBots");

        var admin = api.MapGroup("/admin").WithTags("Admin").RequireAuthorization();

        admin.MapGet("/me", (HttpContext http, BotAdminService bots) => new { isAdmin = bots.IsAdmin(http.User.UserId()) })
            .WithName("AdminMe");

        admin.MapGet("/bots", (HttpContext http, BotAdminService bots, CancellationToken ct) => bots.ListAsync(http.User.UserId(), ct))
            .WithName("AdminListBots");

        admin.MapPost("/bots", (SaveBotRequest request, HttpContext http, BotAdminService bots, CancellationToken ct) =>
                bots.CreateAsync(http.User.UserId(), request, ct))
            .WithName("AdminCreateBot");

        admin.MapPut("/bots/{id:guid}", (Guid id, SaveBotRequest request, HttpContext http, BotAdminService bots, CancellationToken ct) =>
                bots.UpdateAsync(http.User.UserId(), id, request, ct))
            .WithName("AdminUpdateBot");

        // Аватарка бота: те же правила, что у игроков (JPEG/PNG/WebP до 1 МБ), но ставит админ.
        admin.MapPost("/bots/{id:guid}/avatar", async (Guid id, IFormFile file, HttpContext http, BotAdminService bots, UserService users, CancellationToken ct) =>
            {
                var adminId = http.User.UserId();
                await bots.RequireBotAsync(adminId, id, ct);
                await using var stream = file.OpenReadStream();
                await users.SetAvatarAsync(id, stream, file.Length, ct);
                return await bots.GetAsync(adminId, id, ct);
            })
            .DisableAntiforgery()
            .WithName("AdminUploadBotAvatar");

        admin.MapDelete("/bots/{id:guid}/avatar", async (Guid id, HttpContext http, BotAdminService bots, UserService users, CancellationToken ct) =>
            {
                var adminId = http.User.UserId();
                await bots.RequireBotAsync(adminId, id, ct);
                await users.RemoveAvatarAsync(id, ct);
                return await bots.GetAsync(adminId, id, ct);
            })
            .WithName("AdminRemoveBotAvatar");

        admin.MapPost("/bots/presets", (HttpContext http, BotAdminService bots, CancellationToken ct) =>
                bots.CreatePresetsAsync(http.User.UserId(), ct))
            .WithName("AdminCreatePresetBots");

        return api;
    }
}
