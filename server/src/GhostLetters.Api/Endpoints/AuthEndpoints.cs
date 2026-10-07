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

        me.MapGet("", (HttpContext http, UserService users, CancellationToken ct) =>
                users.GetAsync(http.User.UserId(), ct))
            .WithName("GetMe");

        me.MapPatch("", (UpdateProfileRequest request, HttpContext http, UserService users, CancellationToken ct) =>
                users.UpdateAsync(http.User.UserId(), request, ct))
            .WithName("UpdateMe");

        return api;
    }
}
