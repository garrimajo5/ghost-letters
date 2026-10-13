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

        api.MapGet("/bots/{id:guid}/relationships", (Guid id, BotRelationshipService relationships, CancellationToken ct) => relationships.PublicAsync(id, ct))
            .RequireAuthorization().WithTags("Bots");

        var admin = api.MapGroup("/admin").WithTags("Admin").RequireAuthorization();

        admin.MapGet("/bots/{id:guid}/relationships", (Guid id, HttpContext http, BotRelationshipService relationships, CancellationToken ct) =>
            relationships.DetailsAsync(http.User.UserId(), id, ct));

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

        // Голоса для озвучки ботов и проба голоса (синтез SpeechKit, если на сервере есть ключ).
        admin.MapGet("/bots/voices", (HttpContext http, BotAdminService bots, Infrastructure.Speech.SpeechKit speech) =>
            {
                bots.RequireAdmin(http.User.UserId());
                return new
                {
                    enabled = speech.Enabled,
                    spentToday = speech.SpentToday,
                    voices = Infrastructure.Speech.BotVoices.All.Select(v => new
                    {
                        v.Id, v.Label, v.Male,
                        roles = v.Roles.Select(r => new { id = r, label = Infrastructure.Speech.BotVoices.RoleLabels.GetValueOrDefault(r, r) }),
                    }),
                };
            })
            .WithName("AdminBotVoices");

        admin.MapPost("/bots/voice-sample", async (VoiceSampleRequest request, HttpContext http, BotAdminService bots,
                Infrastructure.Speech.SpeechKit speech, CancellationToken ct) =>
            {
                bots.RequireAdmin(http.User.UserId());
                if (!speech.Enabled) return Results.Problem("Синтез речи не настроен на сервере.", statusCode: 503);
                var option = Infrastructure.Speech.BotVoices.Find(request.Voice);
                if (option is null) return Results.BadRequest(new { message = "Нет такого голоса." });
                var role = request.Role is { Length: > 0 } r && option.Roles.Contains(r) ? r : option.Roles.FirstOrDefault();
                var text = string.IsNullOrWhiteSpace(request.Text) ? "Ставлю на мотив — клетка. Подсказка второго раунда указывает на неё." : request.Text.Trim();
                if (text.Length > 200) text = text[..200];
                var audio = await speech.SynthesizeAsync(text, option.Id, role, ct);
                return audio is null ? Results.Problem("Озвучка не удалась — см. журнал сервера.", statusCode: 502) : Results.File(audio.Mp3, "audio/mpeg");
            })
            .WithName("AdminBotVoiceSample");

        // Фразы, которым боты научились у людей: посмотреть и запретить неудачные.
        admin.MapGet("/phrases", (int? take, HttpContext http, BotAdminService bots, Infrastructure.Speech.PhraseLearner phrases, CancellationToken ct) =>
            {
                bots.RequireAdmin(http.User.UserId());
                return phrases.ListAsync(take ?? 200, ct);
            })
            .WithName("AdminPhrases");

        admin.MapPost("/phrases/{id:guid}/block", async (Guid id, BlockPhraseRequest request, HttpContext http, BotAdminService bots,
                Infrastructure.Speech.PhraseLearner phrases, CancellationToken ct) =>
            {
                bots.RequireAdmin(http.User.UserId());
                return await phrases.BlockAsync(id, request.Blocked, ct) ? Results.NoContent() : Results.NotFound();
            })
            .WithName("AdminBlockPhrase");

        admin.MapPost("/bots/presets", (HttpContext http, BotAdminService bots, CancellationToken ct) =>
                bots.CreatePresetsAsync(http.User.UserId(), ct))
            .WithName("AdminCreatePresetBots");

        return api;
    }
}

/// <summary>Проба голоса: голос, амплуа и (необязательно) текст до 200 символов.</summary>
public sealed record VoiceSampleRequest(string Voice, string? Role, string? Text);

/// <summary>Запретить (true) или вернуть (false) выученную фразу.</summary>
public sealed record BlockPhraseRequest(bool Blocked);
