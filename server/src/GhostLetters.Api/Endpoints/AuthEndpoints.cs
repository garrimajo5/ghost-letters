using GhostLetters.Infrastructure.Auth;

namespace GhostLetters.Api.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuth(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth").RequireRateLimiting(Hosting.Hardening.AuthPolicy);

        auth.MapPost("/guest", (GuestLoginRequest request, AuthService service, CancellationToken ct) =>
                service.GuestAsync(request, ct))
            .WithName("LoginGuest");

        auth.MapPost("/link", (LinkLoginRequest request, AuthService service, CancellationToken ct) =>
                service.LinkAsync(request, ct))
            .WithName("LoginByLinkCode");

        auth.MapPost("/key-login", (KeyLoginRequest request, AuthService service, CancellationToken ct) =>
            service.KeyLoginAsync(request, ct));

        auth.MapPost("/link-code", (HttpContext http, AuthService service, CancellationToken ct) =>
                service.CreateLinkCodeAsync(http.User.UserId(), ct))
            .RequireAuthorization()
            .WithName("CreateLinkCode");

        auth.MapPost("/refresh", (RefreshRequest request, AuthService service, CancellationToken ct) =>
                service.RefreshAsync(request.RefreshToken, ct))
            .WithName("RefreshTokens");

        auth.MapPost("/logout", async (RefreshRequest request, AuthService service, CancellationToken ct) =>
            {
                await service.LogoutAsync(request.RefreshToken, ct);
                return Results.NoContent();
            })
            .WithName("Logout");

        var me = api.MapGroup("/me").WithTags("Profile").RequireAuthorization();

        me.MapGet("/recovery", (HttpContext http, AuthService service, CancellationToken ct) =>
            service.RecoveryInfoAsync(http.User.UserId(), ct));
        me.MapPut("/recovery", (RecoveryKeyRequest request, HttpContext http, AuthService service, CancellationToken ct) =>
            service.SetRecoveryAsync(http.User.UserId(), request, ct)).RequireRateLimiting(Hosting.Hardening.AuthPolicy);


        me.MapGet("", (HttpContext http, UserService users, CancellationToken ct) =>
                users.GetAsync(http.User.UserId(), ct))
            .WithName("GetMe");

        me.MapPatch("", (UpdateProfileRequest request, HttpContext http, UserService users, CancellationToken ct) =>
                users.UpdateAsync(http.User.UserId(), request, ct))
            .WithName("UpdateMe");

        me.MapPost("/avatar", async (IFormFile file, HttpContext http, UserService users, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return await users.SetAvatarAsync(http.User.UserId(), stream, file.Length, ct);
            })
            .DisableAntiforgery()
            .WithName("UploadAvatar");

        me.MapDelete("/avatar", (HttpContext http, UserService users, CancellationToken ct) =>
                users.RemoveAvatarAsync(http.User.UserId(), ct))
            .WithName("RemoveAvatar");

        api.MapGet("/avatars/{mediaId:guid}", async (Guid mediaId, HttpContext http, UserService users, CancellationToken ct) =>
            {
                var (content, contentType) = await users.OpenAvatarAsync(mediaId, ct);
                // Новая аватарка — новый id, так что файл можно кэшировать навсегда.
                http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                return Results.Stream(content, contentType);
            })
            .WithTags("Profile")
            .WithName("GetAvatar");

        return api;
    }
}
