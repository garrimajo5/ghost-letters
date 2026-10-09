using System.Text.Json;
using GhostLetters.Application;
using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Auth;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Infrastructure.Bots;

public sealed record BotRelationshipDto(UserDto Player, int Score, string Attitude, int SharedGames, DateTimeOffset UpdatedAt);
public sealed record BotRelationshipDetailDto(BotRelationshipDto Relationship, IReadOnlyDictionary<string, double> Components);

public sealed class BotRelationshipService(GhostLettersDbContext db, BotAdminService admin)
{
    public async Task<IReadOnlyList<BotRelationshipDto>> PublicAsync(Guid botId, CancellationToken ct)
    {
        await RequireBotAsync(botId, ct);
        var rows = await (from r in db.BotRelationships.AsNoTracking()
                          join u in db.Users.AsNoTracking() on r.PlayerId equals u.Id
                          where r.BotId == botId orderby r.Score descending, u.Nickname, u.Id
                          select new { r.Score, r.SharedGames, r.UpdatedAt, User = u }).ToListAsync(ct);
        return rows.Select(r => new BotRelationshipDto(UserDto.From(r.User), r.Score, BotAffinity.Label(r.Score), r.SharedGames, r.UpdatedAt)).ToList();
    }

    public async Task<IReadOnlyList<BotRelationshipDetailDto>> DetailsAsync(Guid caller, Guid botId, CancellationToken ct)
    {
        admin.RequireAdmin(caller);
        var relationships = await PublicAsync(botId, ct);
        var factors = await db.BotRelationships.AsNoTracking().Where(r => r.BotId == botId)
            .ToDictionaryAsync(r => r.PlayerId, r => r.Components, ct);
        return relationships.Select(r => new BotRelationshipDetailDto(r, Read(factors[r.Player.Id]))).ToList();
    }

    private async Task RequireBotAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id && u.IsBot, ct)) throw AppException.NotFound("Бот не найден.");
    }

    public static Dictionary<string, double> Read(string json) => JsonSerializer.Deserialize<Dictionary<string, double>>(json) ?? [];

    /// <summary>Runs in GameService's transaction, after awards. No live signals can leak hidden roles.</summary>
    public async Task RecordAsync(GameState state, DateTimeOffset now, CancellationToken ct)
    {
        if (state.Phase != Phase.Finished || state.Result is null) return;
        var ids = state.Players.Select(p => p.Id).ToList();
        var bots = await db.Users.Where(u => ids.Contains(u.Id) && u.IsBot).Select(u => u.Id).ToListAsync(ct);
        if (bots.Count == 0) return;
        var messages = await db.ChatMessages.AsNoTracking().Where(m => m.GameId == state.Id &&
            m.Channel == ChatChannels.Public && m.AuthorId != null && ids.Contains(m.AuthorId.Value)).ToListAsync(ct);
        var stats = await db.Stats.Where(s => ids.Contains(s.UserId)).ToDictionaryAsync(s => s.UserId, ct);
        var profiles = await db.BotProfiles.AsNoTracking().Where(p => bots.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, ct);
        double Skill(Guid id) => stats.TryGetValue(id, out var s) ? (s.Wins + 2.0) / (s.Games + 4) : .5;
        // Count distinct participation rounds, not message volume. Repeating text cannot buy affinity.
        double? Activity(Guid id) => state.Players.First(p => p.Id == id).Role == Role.Ghost ? null :
            Math.Min(1, messages.Where(m => m.AuthorId == id).Select(m => m.Round).Distinct().Count() / (double)Math.Max(1, state.TotalRounds));

        foreach (var bot in bots.Order())
        {
            // The same bot may finish two different games concurrently. Serialize updates across games.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({bot.ToString()}, 0))", ct);
            if (await db.BotRelationshipGames.AnyAsync(g => g.GameId == state.Id && g.BotId == bot, ct) ||
                db.BotRelationshipGames.Local.Any(g => g.GameId == state.Id && g.BotId == bot)) continue;
            var social = profiles.TryGetValue(bot, out var profile) ? BotAdminService.Personality(profile).Social : BotSocialTraits.ForBot(bot);
            var relationships = await db.BotRelationships.Where(r => r.BotId == bot && ids.Contains(r.PlayerId)).ToDictionaryAsync(r => r.PlayerId, ct);
            foreach (var player in ids.Where(id => id != bot))
            {
                if (!relationships.TryGetValue(player, out var relation))
                {
                    relation = new BotRelationship { BotId = bot, PlayerId = player };
                    db.BotRelationships.Add(relation);
                }
                var votes = (from a in state.VoteRecords where a.Voter == bot
                             join b in state.VoteRecords.Where(v => v.Voter == player) on (a.Stage, a.Attempt) equals (b.Stage, b.Attempt)
                             where (a.Column != null || a.Suspect != null) && (b.Column != null || b.Suspect != null)
                             select a.Column == b.Column && a.Suspect == b.Suspect ? 1.0 : 0).ToList();
                // Only verifiable claims about letters count as honesty, not wrong guesses or assigned roles.
                var claims = messages.Where(m => m.AuthorId == player).SelectMany(m => m.CardIds.Select((card, i) =>
                    (Card: card, Note: i < m.CardNotes.Count ? m.CardNotes[i] : "")))
                    .Where(x => x.Note.StartsWith("кидал", StringComparison.OrdinalIgnoreCase)).Select(x => x.Card).Distinct().ToList();
                double? honesty = claims.Count == 0 ? null : claims.Average(card => state.Letters.Any(l => l.From == player && l.CardId == card) ? 1.0 : -1);
                var thanked = state.Likes.Any(l => l.From == player && l.To == bot);
                relation.SharedGames++;
                var observation = BotAffinity.Observe(social, Activity(player), Activity(bot), votes.Count == 0 ? null : votes.Average(),
                    Skill(player) - Skill(bot), thanked, honesty, relation.SharedGames);
                var components = BotAffinity.Update(Read(relation.Components), observation, social.Forgiveness);
                relation.Components = JsonSerializer.Serialize(components);
                relation.Score = Math.Clamp((int)Math.Round(components.Values.Sum()), -100, 100);
                relation.UpdatedAt = now;
            }
            db.BotRelationshipGames.Add(new BotRelationshipGame { GameId = state.Id, BotId = bot });
        }
    }
}
