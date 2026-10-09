using GhostLetters.Application;
using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Infrastructure.Lobbies;

public sealed record SettingsPresetDto(Guid Id, string Name, LobbySettings Settings);
public sealed record SaveSettingsPresetRequest(string Name, LobbySettings Settings);

/// <summary>Личные пресеты аккаунта. Встроенные шаблоны неизменяемы и живут в клиенте.</summary>
public sealed class SettingsPresetService(GhostLettersDbContext db)
{
    public async Task<IReadOnlyList<SettingsPresetDto>> ListAsync(Guid userId, CancellationToken ct) =>
        (await db.SettingsPresets.AsNoTracking().Where(p => p.UserId == userId).OrderBy(p => p.Name).ThenBy(p => p.Id).ToListAsync(ct))
        .Select(Dto).ToList();

    public async Task<SettingsPresetDto> SaveAsync(Guid userId, Guid id, SaveSettingsPresetRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is < 1 or > 40) throw AppException.Validation("Название пресета — от 1 до 40 символов.");
        if (request.Settings is null) throw AppException.Validation("Укажите настройки пресета.");
        request.Settings.Validate();
        var preset = await db.SettingsPresets.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (preset is not null && preset.UserId != userId) throw AppException.NotFound("Пресет не найден.");
        if (preset is null)
        {
            if (await db.SettingsPresets.CountAsync(p => p.UserId == userId, ct) >= 30)
                throw AppException.Validation("Можно сохранить до 30 личных пресетов.");
            preset = new SettingsPreset { Id = id, UserId = userId };
            db.SettingsPresets.Add(preset);
        }
        preset.Name = name;
        // Назначение конкретного человека Призраком не переносим между столами.
        preset.Settings = GameJson.Serialize(request.Settings with { GhostUserId = null });
        await db.SaveChangesAsync(ct);
        return Dto(preset);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var preset = await db.SettingsPresets.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct)
            ?? throw AppException.NotFound("Пресет не найден.");
        db.SettingsPresets.Remove(preset);
        await db.SaveChangesAsync(ct);
    }

    private static SettingsPresetDto Dto(SettingsPreset p) => new(p.Id, p.Name, GameJson.Deserialize<LobbySettings>(p.Settings));
}
