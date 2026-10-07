using GhostLetters.Application;
using GhostLetters.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Infrastructure.Auth;

/// <summary>Свой профиль: просмотр, смена ника и цвета.</summary>
public sealed class UserService(GhostLettersDbContext db)
{
    public async Task<UserDto> GetAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw AppException.NotFound("Игрок не найден.");
        return UserDto.From(user);
    }

    public async Task<UserDto> UpdateAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw AppException.NotFound("Игрок не найден.");

        if (request.Nickname is not null)
        {
            user.Nickname = ProfileRules.Nickname(request.Nickname);
        }

        if (request.AvatarColor is not null)
        {
            user.AvatarColor = ProfileRules.AvatarColor(request.AvatarColor);
        }

        await db.SaveChangesAsync(ct);
        return UserDto.From(user);
    }
}
