using GhostLetters.Application;
using GhostLetters.Infrastructure.Auth;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GhostLetters.Infrastructure.Bots;

/// <summary>Спектры характера — как их видит кабинет (все 0…1).</summary>
public sealed record BotSpectra(
    double Meaning,
    double Shape,
    double Color,
    double Negative,
    double Memory,
    double Risk,
    double Compromise,
    double Variability,
    double Strictness = 0.5)
{
    public static BotSpectra From(BotPersonality p) =>
        new(p.Meaning, p.Shape, p.Color, p.Negative, p.Memory, p.Risk, p.Compromise, p.Variability, p.Strictness);

    public BotPersonality ToPersonality() => new BotPersonality
    {
        Meaning = Meaning, Shape = Shape, Color = Color, Negative = Negative,
        Memory = Memory, Risk = Risk, Compromise = Compromise, Variability = Variability, Strictness = Strictness,
    }.Clamped();
}

public sealed record BotDto(
    Guid Id,
    string Nickname,
    string AvatarColor,
    Guid? AvatarId,
    string About,
    BotSpectra Spectra,
    bool Enabled,
    int Games,
    int Wins,
    int Rating);

public sealed record SaveBotRequest(string Nickname, string? AvatarColor, string? About, BotSpectra Spectra, bool Enabled = true);

/// <summary>Бот для выбора в лобби: имя, цвет и пара слов о характере.</summary>
public sealed record BotCardDto(Guid Id, string Nickname, string AvatarColor, Guid? AvatarId, string About);

/// <summary>
/// Кабинет ботов: общий набор характеров, который настраивает админ (Admin:UserIds — id игроков через запятую).
/// </summary>
public sealed class BotAdminService(GhostLettersDbContext db, IConfiguration configuration, TimeProvider time)
{
    public bool IsAdmin(Guid userId) =>
        (configuration["Admin:UserIds"] ?? string.Empty)
        .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
        .Any(s => Guid.TryParse(s, out var id) && id == userId);

    public void RequireAdmin(Guid userId)
    {
        if (!IsAdmin(userId))
        {
            throw AppException.Forbidden("Кабинет ботов — только для администратора.");
        }
    }

    public async Task<IReadOnlyList<BotDto>> ListAsync(Guid adminId, CancellationToken ct)
    {
        RequireAdmin(adminId);
        var rows = await (from b in db.BotProfiles.AsNoTracking()
                          join u in db.Users.AsNoTracking() on b.UserId equals u.Id
                          join s in db.Stats.AsNoTracking() on u.Id equals s.UserId into ss
                          from s in ss.DefaultIfEmpty()
                          orderby u.Nickname
                          select new { b, u, Games = s == null ? 0 : s.Games, Wins = s == null ? 0 : s.Wins, Rating = s == null ? UserStats.InitialRating : s.Rating })
            .ToListAsync(ct);
        return rows.Select(r => ToDto(r.b, r.u, r.Games, r.Wins, r.Rating)).ToList();
    }

    public async Task<BotDto> CreateAsync(Guid adminId, SaveBotRequest request, CancellationToken ct)
    {
        RequireAdmin(adminId);
        var now = time.GetUtcNow();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Nickname = BotNickname(request.Nickname),
            AvatarColor = request.AvatarColor is null ? "#5C7C99" : ProfileRules.AvatarColor(request.AvatarColor),
            CreatedAt = now,
            LastSeenAt = now,
            IsBot = true,
        };
        var profile = new BotProfile { UserId = user.Id };
        Apply(profile, request, now);
        db.Users.Add(user);
        db.Stats.Add(new UserStats { UserId = user.Id });
        db.BotProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
        return ToDto(profile, user, 0, 0, UserStats.InitialRating);
    }

    public async Task<BotDto> UpdateAsync(Guid adminId, Guid botId, SaveBotRequest request, CancellationToken ct)
    {
        RequireAdmin(adminId);
        var profile = await db.BotProfiles.FirstOrDefaultAsync(b => b.UserId == botId, ct) ?? throw AppException.NotFound("Бот не найден.");
        var user = await db.Users.SingleAsync(u => u.Id == botId, ct);
        user.Nickname = BotNickname(request.Nickname);
        if (request.AvatarColor is not null)
        {
            user.AvatarColor = ProfileRules.AvatarColor(request.AvatarColor);
        }

        Apply(profile, request, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        var stats = await db.Stats.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == botId, ct);
        return ToDto(profile, user, stats?.Games ?? 0, stats?.Wins ?? 0, stats?.Rating ?? UserStats.InitialRating);
    }

    /// <summary>Один бот кабинета (после смены аватарки и т. п.).</summary>
    public async Task<BotDto> GetAsync(Guid adminId, Guid botId, CancellationToken ct)
    {
        await RequireBotAsync(adminId, botId, ct);
        var profile = await db.BotProfiles.AsNoTracking().SingleAsync(b => b.UserId == botId, ct);
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == botId, ct);
        var stats = await db.Stats.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == botId, ct);
        return ToDto(profile, user, stats?.Games ?? 0, stats?.Wins ?? 0, stats?.Rating ?? UserStats.InitialRating);
    }

    /// <summary>Админ трогает только ботов кабинета — не людей и не старых безликих ботов.</summary>
    public async Task RequireBotAsync(Guid adminId, Guid botId, CancellationToken ct)
    {
        RequireAdmin(adminId);
        if (!await db.BotProfiles.AnyAsync(b => b.UserId == botId, ct))
        {
            throw AppException.NotFound("Бот не найден.");
        }
    }

    /// <summary>Готовые характеры одной кнопкой; уже созданные (по имени) не дублируются.</summary>
    public async Task<IReadOnlyList<BotDto>> CreatePresetsAsync(Guid adminId, CancellationToken ct)
    {
        RequireAdmin(adminId);
        var existing = await (from b in db.BotProfiles join u in db.Users on b.UserId equals u.Id select u.Nickname).ToListAsync(ct);
        foreach (var (name, color, about, p) in BotPresets.All)
        {
            var nickname = BotNickname(name);
            if (!existing.Contains(nickname))
            {
                await CreateAsync(adminId, new SaveBotRequest(nickname, color, about, BotSpectra.From(p)), ct);
            }
        }

        return await ListAsync(adminId, ct);
    }

    /// <summary>Включённые боты — для выбора в лобби.</summary>
    public async Task<IReadOnlyList<BotCardDto>> PublicListAsync(CancellationToken ct) =>
        await (from b in db.BotProfiles.AsNoTracking()
               join u in db.Users.AsNoTracking() on b.UserId equals u.Id
               where b.Enabled
               orderby u.Nickname
               select new BotCardDto(u.Id, u.Nickname, u.AvatarColor, u.AvatarMediaId, b.About))
            .ToListAsync(ct);

    public static BotPersonality Personality(BotProfile p) => new BotPersonality
    {
        Meaning = p.Meaning, Shape = p.Shape, Color = p.Color, Negative = p.Negative,
        Memory = p.Memory, Risk = p.Risk, Compromise = p.Compromise, Variability = p.Variability, Strictness = p.Strictness,
    }.Clamped();

    private static void Apply(BotProfile profile, SaveBotRequest request, DateTimeOffset now)
    {
        var p = (request.Spectra ?? throw AppException.Validation("Нужны спектры характера.")).ToPersonality();
        profile.Meaning = p.Meaning;
        profile.Shape = p.Shape;
        profile.Color = p.Color;
        profile.Negative = p.Negative;
        profile.Memory = p.Memory;
        profile.Risk = p.Risk;
        profile.Compromise = p.Compromise;
        profile.Variability = p.Variability;
        profile.Strictness = p.Strictness;
        var about = (request.About ?? string.Empty).Trim();
        profile.About = about.Length > 300 ? about[..300] : about;
        profile.Enabled = request.Enabled;
        profile.UpdatedAt = now;
    }

    /// <summary>Боты всегда с приставкой «Бот», чтобы их не путали с людьми.</summary>
    private static string BotNickname(string? name)
    {
        var clean = (name ?? string.Empty).Trim();
        if (clean.StartsWith("Бот ", StringComparison.Ordinal))
        {
            clean = clean[4..].Trim();
        }

        return ProfileRules.Nickname("Бот " + clean);
    }

    private static BotDto ToDto(BotProfile b, User u, int games, int wins, int rating) =>
        new(u.Id, u.Nickname, u.AvatarColor, u.AvatarMediaId, b.About, BotSpectra.From(Personality(b)), b.Enabled, games, wins, rating);
}
