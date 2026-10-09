using GhostLetters.Infrastructure.Auth;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Endpoints;

public static class CardEndpoints
{
    public static void MapCards(this RouteGroupBuilder api)
    {
        api.MapGet("/cards/sets", (CardAdminService cards, CancellationToken ct) => cards.PublicSetsAsync(ct)).RequireAuthorization();
        var admin = api.MapGroup("/admin/cards").RequireAuthorization().WithTags("Admin cards");
        admin.MapGet("", (HttpContext http, CardAdminService cards, string? query, string? setCode, bool? active, int? page, CancellationToken ct) =>
            cards.ListAsync(http.User.UserId(), query, setCode, active, page ?? 0, ct));
        admin.MapPut("/{id:guid}", (Guid id, SaveCardRequest request, HttpContext http, CardAdminService cards, CancellationToken ct) =>
            cards.SaveAsync(http.User.UserId(), id, request, ct));
    }
}
