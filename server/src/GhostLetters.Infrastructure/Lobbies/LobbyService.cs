using System.Security.Cryptography;
using GhostLetters.Application;
using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using GhostLetters.Domain.Rules;

namespace GhostLetters.Infrastructure.Lobbies;

/// <summary>Лобби: создание, вход по коду, готовность, настройки, хост, старт партии.</summary>
public sealed class LobbyService(
    GhostLettersDbContext db,
    CardCatalog cards,
    IRealtimeNotifier notifier,
    TimeProvider time,
    IServiceProvider services)
{
    public const int MaxTableScreens = 4;
    public const int MaxTitleLength = 64;

    /// <summary>Без похожих символов: нет 0/O, 1/I.</summary>
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<LobbyDto> CreateAsync(Guid userId, CreateLobbyRequest request, CancellationToken ct)
    {
        var settings = request.Settings ?? new LobbySettings();
        settings.Validate();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw AppException.NotFound("Игрок не найден.");
        var title = string.IsNullOrWhiteSpace(request.Title) ? $"Стол {user.Nickname}" : request.Title.Trim();
        if (title.Length > MaxTitleLength)
        {
            throw AppException.Validation($"Название — до {MaxTitleLength} символов.");
        }

        var now = time.GetUtcNow();
        var lobby = new Lobby
        {
            Id = Guid.NewGuid(),
            Code = await FreeCodeAsync(ct),
            Title = title,
            HostUserId = userId,
            Status = LobbyStatuses.Open,
            Settings = GameJson.Serialize(settings),
            CreatedAt = now,
        };
        db.Lobbies.Add(lobby);
        db.LobbyMembers.Add(new LobbyMember
        {
            LobbyId = lobby.Id, UserId = userId, Seat = 0, JoinMode = JoinModes.Player, JoinedAt = now,
        });
        await db.SaveChangesAsync(ct);
        return await PublishAsync(lobby.Id, ct);
    }

    public async Task<LobbyDto> GetByCodeAsync(string code, CancellationToken ct)
    {
        var lobby = await FindByCodeAsync(code, ct);
        return await DtoAsync(lobby.Id, ct);
    }

    public async Task<LobbyDto> GetAsync(Guid lobbyId, Guid userId, CancellationToken ct)
    {
        await RequireMemberAsync(lobbyId, userId, ct);
        return await DtoAsync(lobbyId, ct);
    }

    public async Task<IReadOnlyList<WatchableGameDto>> WatchableAsync(Guid userId, CancellationToken ct) =>
        await (from lobby in db.Lobbies.AsNoTracking()
               join game in db.Games.AsNoTracking() on lobby.CurrentGameId equals game.Id
               where lobby.Status == LobbyStatuses.InGame && game.Status == GameStatuses.Active
                   && !db.GamePlayers.Any(p => p.GameId == game.Id && p.UserId == userId)
               orderby game.StartedAt descending, game.Id
               select new WatchableGameDto(lobby.Code, lobby.Title, game.Id, game.Phase,
                   db.GamePlayers.Count(p => p.GameId == game.Id)))
            .Take(50).ToListAsync(ct);

    /// <summary>Войти игроком, зрителем или экраном стола. Во время партии режим менять нельзя.</summary>
    public async Task<LobbyDto> JoinAsync(string code, Guid userId, JoinLobbyRequest request, CancellationToken ct)
    {
        var mode = request.Mode ?? JoinModes.Player;
        if (mode is not (JoinModes.Player or JoinModes.Table or JoinModes.Spectator))
        {
            throw AppException.Validation("Режим входа — player, spectator или table.");
        }

        var lobby = await FindByCodeAsync(code, ct);
        var members = await db.LobbyMembers.Where(m => m.LobbyId == lobby.Id).ToListAsync(ct);
        var existing = members.FirstOrDefault(m => m.UserId == userId);
        if (existing is not null && existing.JoinMode == mode)
        {
            return await DtoAsync(lobby.Id, ct);
        }

        if (existing is not null && lobby.Status != LobbyStatuses.Open)
        {
            throw AppException.Conflict(AppException.Codes.GameInProgress, "Во время партии нельзя менять режим участия.");
        }

        if (userId == lobby.HostUserId && mode != JoinModes.Player)
        {
            throw AppException.Validation("Хост остаётся игроком. Для просмотра выйдите из лобби и войдите зрителем.");
        }

        // Экран стола можно подключить и к идущей партии; игроком — только пока лобби открыто.
        if (lobby.Status != LobbyStatuses.Open && mode == JoinModes.Player)
        {
            throw AppException.Conflict(AppException.Codes.GameInProgress, "Партия уже идёт — можно подключиться зрителем.");
        }

        if (mode == JoinModes.Player && members.Count(m => m.JoinMode == JoinModes.Player) >= RoleTable.MaxPlayers)
        {
            throw AppException.Conflict(AppException.Codes.LobbyFull, $"В лобби уже {RoleTable.MaxPlayers} игроков.");
        }

        if (mode == JoinModes.Table && members.Count(m => m.JoinMode == JoinModes.Table) >= MaxTableScreens)
        {
            throw AppException.Conflict(AppException.Codes.LobbyFull, "Подключено слишком много экранов стола.");
        }

        var member = existing;
        if (member is null)
        {
            member = new LobbyMember { LobbyId = lobby.Id, UserId = userId, JoinedAt = time.GetUtcNow() };
            db.LobbyMembers.Add(member);
            members.Add(member);
        }

        member.JoinMode = mode;
        member.IsReady = false;
        member.Seat = mode == JoinModes.Player ? int.MaxValue : -1;
        Reseat(members.Where(m => m.JoinMode == JoinModes.Player));
        await db.SaveChangesAsync(ct);
        return await PublishAsync(lobby.Id, ct);
    }

    /// <summary>Выйти. Хост передаётся следующему игроку по кругу; без игроков лобби закрывается.</summary>
    public async Task LeaveAsync(Guid lobbyId, Guid userId, CancellationToken ct)
    {
        var lobby = await FindAsync(lobbyId, ct);
        var member = await RequireMemberAsync(lobbyId, userId, ct);
        if (lobby.Status == LobbyStatuses.InGame && member.JoinMode == JoinModes.Player)
        {
            throw AppException.Conflict(AppException.Codes.GameInProgress, "Нельзя выйти из лобби во время партии.");
        }

        await RemoveMemberAsync(lobby, member, ct);
        await db.SaveChangesAsync(ct);
        await PublishAsync(lobby.Id, ct);
    }

    /// <summary>Добавить бота-игрока (только хост, пока лобби открыто). Бот сразу готов.</summary>
    /// <summary>
    /// Добавить бота: выбранного из кабинета (botId), иначе случайного включённого из кабинета, которого ещё нет в лобби;
    /// если кабинет пуст — безымянного бота без характера, как раньше.
    /// </summary>
    public async Task<LobbyDto> AddBotAsync(Guid lobbyId, Guid hostId, CancellationToken ct, Guid? botId = null)
    {
        var lobby = await RequireHostAsync(lobbyId, hostId, ct);
        if (lobby.Status != LobbyStatuses.Open)
        {
            throw AppException.Conflict(AppException.Codes.GameInProgress, "Ботов добавляют до начала партии.");
        }

        var players = await db.LobbyMembers.CountAsync(m => m.LobbyId == lobbyId && m.JoinMode == JoinModes.Player, ct);
        if (players >= RoleTable.MaxPlayers)
        {
            throw AppException.Conflict(AppException.Codes.LobbyFull, $"В лобби уже {RoleTable.MaxPlayers} игроков.");
        }

        var members = await db.LobbyMembers.Where(m => m.LobbyId == lobbyId).Select(m => m.UserId).ToListAsync(ct);
        var available = await db.BotProfiles.Where(b => b.Enabled && !members.Contains(b.UserId)).Select(b => b.UserId).ToListAsync(ct);
        var now = time.GetUtcNow();
        Guid chosen;
        if (botId is { } wanted)
        {
            if (members.Contains(wanted))
            {
                throw AppException.Conflict(AppException.Codes.Conflict, "Этот бот уже в лобби.");
            }

            chosen = available.Contains(wanted) ? wanted : throw AppException.NotFound("Такого бота нет или он выключен.");
        }
        else if (available.Count > 0)
        {
            chosen = available[Random.Shared.Next(available.Count)];
        }
        else
        {
            // Бот без характера: имя, которого нет в лобби и нет в кабинете. Такой бот уже был в прошлых
            // партиях — берём его же (одно имя — один бот со своей историей и рейтингом), иначе создаём.
            var bots = await (from m in db.LobbyMembers
                              join u in db.Users on m.UserId equals u.Id
                              where m.LobbyId == lobbyId && u.IsBot
                              select u.Nickname).ToListAsync(ct);
            var cabinet = await (from b in db.BotProfiles join u in db.Users on b.UserId equals u.Id select u.Nickname).ToListAsync(ct);
            var name = BotNames.FirstOrDefault(n => !bots.Contains(n) && !cabinet.Contains(n)) ?? $"Бот {bots.Count + 1}";
            var existing = await db.Users
                .Where(u => u.IsBot && u.Nickname == name && !db.BotProfiles.Any(b => b.UserId == u.Id))
                .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync(ct);
            if (existing is { } known)
            {
                chosen = known;
            }
            else
            {
                var bot = new User
                {
                    Id = Guid.NewGuid(),
                    Nickname = name,
                    AvatarColor = BotColors[bots.Count % BotColors.Length],
                    CreatedAt = now,
                    LastSeenAt = now,
                    IsBot = true,
                };
                db.Users.Add(bot);
                chosen = bot.Id;
            }
        }

        db.LobbyMembers.Add(new LobbyMember
        {
            LobbyId = lobbyId, UserId = chosen, Seat = players, JoinMode = JoinModes.Player, IsReady = true, JoinedAt = now,
        });
        await db.SaveChangesAsync(ct);
        return await PublishAsync(lobbyId, ct);
    }

    private static readonly string[] BotNames =
        ["Бот Пуаро", "Бот Марпл", "Бот Ватсон", "Бот Лестрейд", "Бот Мегрэ", "Бот Коломбо", "Бот Фандорин", "Бот Знаменский", "Бот Каменская", "Бот Шарапов", "Бот Жеглов"];

    private static readonly string[] BotColors = ["#5C7C99", "#B370D9", "#E57F4F", "#4AA3DF", "#F2A541"];

    public async Task KickAsync(Guid lobbyId, Guid hostId, Guid userId, CancellationToken ct)
    {
        var lobby = await RequireHostAsync(lobbyId, hostId, ct);
        if (lobby.Status != LobbyStatuses.Open)
        {
            throw AppException.Conflict(AppException.Codes.GameInProgress, "Во время партии исключать нельзя.");
        }

        if (userId == hostId)
        {
            throw AppException.Validation("Хост не может исключить себя — выйдите из лобби.");
        }

        var member = await db.LobbyMembers.FirstOrDefaultAsync(m => m.LobbyId == lobbyId && m.UserId == userId, ct)
                     ?? throw AppException.NotFound("Такого игрока нет в лобби.");
        await RemoveMemberAsync(lobby, member, ct);
        await db.SaveChangesAsync(ct);
        await PublishAsync(lobby.Id, ct);
    }

    public async Task<LobbyDto> SetReadyAsync(Guid lobbyId, Guid userId, bool ready, CancellationToken ct)
    {
        var member = await RequireMemberAsync(lobbyId, userId, ct);
        if (member.JoinMode != JoinModes.Player)
        {
            throw AppException.Forbidden("Зрители не участвуют в готовности игроков.");
        }
        member.IsReady = ready;
        await db.SaveChangesAsync(ct);
        return await PublishAsync(lobbyId, ct);
    }

    /// <summary>
    /// Новые настройки (целиком). Во время партии меняются раунды, режим обсуждения (не во время него), темп и таймеры —
    /// они действуют со следующей фазы.
    /// </summary>
    public async Task<LobbyDto> UpdateSettingsAsync(Guid lobbyId, Guid hostId, LobbySettings settings, CancellationToken ct)
    {
        var lobby = await RequireHostAsync(lobbyId, hostId, ct);
        settings.Validate();

        if (lobby.Status == LobbyStatuses.InGame && lobby.CurrentGameId is { } gameId)
        {
            var current = GameJson.Deserialize<LobbySettings>(lobby.Settings);
            var rulesOnly = settings with
            {
                Tempo = current.Tempo, TurnHours = current.TurnHours, Timers = current.Timers, Rounds = current.Rounds,
                Discussion = current.Discussion,
            };
            if (GameJson.Serialize(rulesOnly) != GameJson.Serialize(current))
            {
                throw AppException.Conflict(AppException.Codes.GameInProgress,
                    "Во время партии можно менять только раунды, режим обсуждения, темп и таймеры.");
            }

            if (settings.Rounds != current.Rounds)
            {
                // «По правилам» — по таблице для числа игроков в партии.
                var players = await db.GamePlayers.CountAsync(p => p.GameId == gameId, ct);
                var rounds = settings.Rounds ?? GameDefaults.Rounds(players);
                await services.GetRequiredService<GameService>().ChangeRoundsAsync(gameId, rounds, ct);
            }

            if (settings.Discussion != current.Discussion)
            {
                await services.GetRequiredService<GameService>().ChangeDiscussionAsync(gameId, settings.Discussion, ct);
            }

            var game = await db.Games.FirstAsync(g => g.Id == gameId, ct);
            game.Settings = GameJson.Serialize(settings);
        }

        lobby.Settings = GameJson.Serialize(settings);
        await db.SaveChangesAsync(ct);
        return await PublishAsync(lobbyId, ct);
    }

    /// <summary>Старт: только хост, все игроки готовы, состав ролей и колода подходят.</summary>
    public async Task<StartGameResponse> StartAsync(Guid lobbyId, Guid hostId, CancellationToken ct)
    {
        var lobby = await RequireHostAsync(lobbyId, hostId, ct);
        if (lobby.Status != LobbyStatuses.Open)
        {
            throw AppException.Conflict(AppException.Codes.GameInProgress, "Партия уже идёт.");
        }

        var players = await db.LobbyMembers
            .Where(m => m.LobbyId == lobbyId && m.JoinMode == JoinModes.Player)
            .OrderBy(m => m.Seat).ToListAsync(ct);
        if (players.Count < RoleTable.MinPlayers)
        {
            throw AppException.Validation($"Нужно хотя бы {RoleTable.MinPlayers} игрока.");
        }

        if (players.Any(p => p.UserId != hostId && !p.IsReady))
        {
            throw AppException.Validation("Не все игроки готовы.");
        }

        var settings = GameJson.Deserialize<LobbySettings>(lobby.Settings);
        var deck = await cards.DeckAsync(settings.CardSets, ct);
        var gameId = Guid.NewGuid();
        GameState state;
        try
        {
            state = GameEngine.Create(gameId, players.Select(p => p.UserId).ToList(), settings.ToGameSettings(), deck,
                RandomNumberGenerator.GetInt32(int.MaxValue));
        }
        catch (Exception e) when (e is GameRuleException or ArgumentException)
        {
            throw AppException.Validation(e.Message);
        }

        var now = time.GetUtcNow();
        var game = new Game
        {
            Id = gameId,
            LobbyId = lobbyId,
            Status = GameStatuses.Active,
            Settings = lobby.Settings,
            Seed = state.Seed,
            StartedAt = now,
        };
        var solo = await GameStore.IsSoloAsync(db, players.Select(p => p.UserId).ToList(), ct);
        GameStore.Write(game, state, settings, now, phaseChanged: true, solo);
        db.Games.Add(game);
        db.GamePlayers.AddRange(state.Players.Select(p => new GamePlayer
        {
            GameId = gameId, UserId = p.Id, Seat = p.Seat, Role = p.Role.ToString(), IsConnected = true,
        }));
        lobby.Status = LobbyStatuses.InGame;
        lobby.CurrentGameId = gameId;
        // К реваншу люди снова отмечают готовность, боты всегда готовы.
        var playerIds = players.Select(p => p.UserId).ToList();
        var botIds = await db.Users.Where(u => u.IsBot && playerIds.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);
        foreach (var p in players.Where(p => !botIds.Contains(p.UserId)))
        {
            p.IsReady = false;
        }

        await db.SaveChangesAsync(ct);
        await PublishAsync(lobbyId, ct);
        await notifier.GameStartedAsync(lobbyId, gameId, ct);
        await notifier.GameChangedAsync(state, [new GameEvent("GameStarted")], game.PhaseDeadline, ct);
        return new StartGameResponse(gameId);
    }

    /// <summary>Партия закончилась — лобби снова открыто для реванша.</summary>
    public async Task GameFinishedAsync(Guid lobbyId, CancellationToken ct)
    {
        var lobby = await db.Lobbies.FirstOrDefaultAsync(l => l.Id == lobbyId, ct);
        if (lobby is { Status: LobbyStatuses.InGame })
        {
            lobby.Status = LobbyStatuses.Open;
            await db.SaveChangesAsync(ct);
            await PublishAsync(lobbyId, ct);
        }
    }

    public async Task<LobbyMember> RequireMemberAsync(Guid lobbyId, Guid userId, CancellationToken ct) =>
        await db.LobbyMembers.FirstOrDefaultAsync(m => m.LobbyId == lobbyId && m.UserId == userId, ct)
        ?? throw AppException.Forbidden("Вы не в этом лобби.");

    private async Task RemoveMemberAsync(Lobby lobby, LobbyMember member, CancellationToken ct)
    {
        db.LobbyMembers.Remove(member);
        var players = await db.LobbyMembers
            .Where(m => m.LobbyId == lobby.Id && m.JoinMode == JoinModes.Player && m.UserId != member.UserId)
            .OrderBy(m => m.Seat).ToListAsync(ct);

        if (lobby.HostUserId == member.UserId)
        {
            // Следующий по кругу после ушедшего хоста.
            var next = players.FirstOrDefault(p => p.Seat > member.Seat) ?? players.FirstOrDefault();
            if (next is null)
            {
                lobby.Status = LobbyStatuses.Closed;
            }
            else
            {
                lobby.HostUserId = next.UserId;
            }
        }

        Reseat(players);
    }

    private static void Reseat(IEnumerable<LobbyMember> players)
    {
        var seat = 0;
        foreach (var p in players.OrderBy(x => x.Seat))
        {
            p.Seat = seat++;
        }
    }

    private async Task<Lobby> RequireHostAsync(Guid lobbyId, Guid userId, CancellationToken ct)
    {
        var lobby = await FindAsync(lobbyId, ct);
        if (lobby.HostUserId != userId)
        {
            throw AppException.Forbidden("Это может только хост.");
        }

        return lobby;
    }

    private async Task<Lobby> FindAsync(Guid lobbyId, CancellationToken ct) =>
        await db.Lobbies.FirstOrDefaultAsync(l => l.Id == lobbyId && l.Status != LobbyStatuses.Closed, ct)
        ?? throw AppException.NotFound("Лобби не найдено.");

    private async Task<Lobby> FindByCodeAsync(string code, CancellationToken ct)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        return await db.Lobbies.FirstOrDefaultAsync(l => l.Code == normalized && l.Status != LobbyStatuses.Closed, ct)
               ?? throw AppException.NotFound("Лобби с таким кодом не найдено.");
    }

    private async Task<string> FreeCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var code = new string(Enumerable.Range(0, Lobby.CodeLength)
                .Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]).ToArray());
            if (!await db.Lobbies.AnyAsync(l => l.Code == code && l.Status != LobbyStatuses.Closed, ct))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Не удалось подобрать свободный код лобби.");
    }

    private async Task<LobbyDto> PublishAsync(Guid lobbyId, CancellationToken ct)
    {
        var dto = await DtoAsync(lobbyId, ct);
        await notifier.LobbyChangedAsync(dto, ct);
        return dto;
    }

    private async Task<LobbyDto> DtoAsync(Guid lobbyId, CancellationToken ct)
    {
        var lobby = await db.Lobbies.AsNoTracking().FirstAsync(l => l.Id == lobbyId, ct);
        var members = await (
                from m in db.LobbyMembers.AsNoTracking()
                join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                where m.LobbyId == lobbyId
                select new { m, u })
            .ToListAsync(ct);

        return new LobbyDto(
            lobby.Id,
            lobby.Code,
            lobby.Title,
            lobby.HostUserId,
            lobby.Status,
            GameJson.Deserialize<LobbySettings>(lobby.Settings),
            lobby.CurrentGameId,
            members
                .OrderBy(x => x.m.JoinMode == JoinModes.Player ? 0 : 1).ThenBy(x => x.m.Seat)
                .Select(x => new LobbyMemberDto(x.u.Id, x.u.Nickname, x.u.AvatarColor, x.m.Seat, x.m.JoinMode, x.m.IsReady, x.u.IsBot, x.u.AvatarMediaId))
                .ToList());
    }
}
