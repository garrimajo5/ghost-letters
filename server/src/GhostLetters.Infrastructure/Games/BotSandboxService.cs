using GhostLetters.Application;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using GhostLetters.Domain.Game;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Infrastructure.Games;

/// <param name="Ai">Боты 2.0: дописать к первым репликам вариант ИИ-модели (до <see cref="BotSandboxService.AiLines"/>).</param>
public sealed record SandboxRequest(string Scenario, int Seed, bool Ai = false);
public sealed record SandboxSummary(Guid Id, DateTimeOffset CreatedAt, string Scenario, int Seed, string Status,
    string? Error, bool? Passed, int CorrectRows, int TotalRows, string? Winner, long ElapsedMs, int Steps, int Messages);
public sealed record SandboxReplay(SandboxSummary Summary, IReadOnlyList<SandboxSeat> Seats);
public sealed record SandboxStep(int Index, int Total, string Action, Guid? Actor, System.Text.Json.JsonElement? Command,
    PlayerView View, PlayerView? Observation, IReadOnlyList<int>? Truth, IReadOnlyDictionary<Guid, string>? Roles, IReadOnlyList<SandboxMessage> Messages);

public sealed class SandboxGate { public SemaphoreSlim Semaphore { get; } = new(1, 1); }

public sealed class BotSandboxService(GhostLettersDbContext db, BotAdminService admins, CardCatalog catalog,
    CardTagStore tagStore, SandboxGate gate, TimeProvider time, Speech.SpeechWriter writer)
{
    /// <summary>Сколько реплик прогона отдаём ИИ-модели — дальше дорого и долго.</summary>
    public const int AiLines = 12;

    public async Task<IReadOnlyList<SandboxSummary>> ListAsync(Guid admin, CancellationToken ct)
    {
        admins.RequireAdmin(admin);
        var rows = await db.SandboxRuns.AsNoTracking().OrderByDescending(r => r.CreatedAt).Take(100).Select(r => r.Summary).ToListAsync(ct);
        return rows.Select(GameJson.Deserialize<SandboxSummary>).ToList();
    }

    public async Task<SandboxSummary> RunAsync(Guid admin, SandboxRequest request, CancellationToken ct)
    {
        admins.RequireAdmin(admin);
        if (!BotSandboxRunner.Scenarios.Contains(request.Scenario)) throw AppException.Validation("Неизвестный сценарий.");
        if (!await gate.Semaphore.WaitAsync(0, ct)) throw AppException.Conflict("SANDBOX_BUSY", "Уже выполняется прогон. Попробуйте позже.");
        try
        {
            if (await db.SandboxRuns.CountAsync(ct) >= 100) throw AppException.Conflict("SANDBOX_FULL", "Сохранено 100 прогонов. Удалите ненужные перед новым запуском.");
            var deck = await catalog.DeckAsync(LobbySettings.AllCardSets, ct);
            if (deck.Count < 120) throw AppException.Validation("Для песочницы нужно не менее 120 активных карт.");
            var tags = await tagStore.LoadAsync(ct);
            var report = await Task.Run(() => BotSandboxRunner.Run(request.Scenario, request.Seed, deck, tags, ct), ct);
            if (request.Ai) report = report with { Messages = await WithAiAsync(report, ct) };
            var id = Guid.NewGuid(); var created = time.GetUtcNow();
            var summary = new SandboxSummary(id, created, request.Scenario, request.Seed, report.Status, report.Error,
                report.Passed, report.CorrectRows, report.TotalRows, report.Winner, report.ElapsedMs, report.Frames.Count, report.Messages.Count);
            db.SandboxRuns.Add(new SandboxRun { Id = id, CreatedAt = created, Scenario = request.Scenario, Seed = request.Seed,
                Summary = GameJson.Serialize(summary), Report = GameJson.Serialize(report) });
            await db.SaveChangesAsync(ct);
            return summary;
        }
        finally { gate.Semaphore.Release(); }
    }

    /// <summary>
    /// Боты 2.0 в песочнице: первые реплики-замыслы ещё раз — словами ИИ-модели, с причиной, если ответ не прошёл проверку.
    /// Тот же паспорт, что за столом: замысел, черновик «За столом», имена и последние реплики.
    /// </summary>
    private async Task<IReadOnlyList<SandboxMessage>> WithAiAsync(SandboxReport report, CancellationToken ct)
    {
        if (!writer.Enabled) return report.Messages.Select(m => m.Intent is null ? m : m with { Ai = "не принято: YandexGPT не подключён" }).ToList();
        var names = report.Seats.ToDictionary(s => s.Id, s => BotNames.Bare(s.Name));
        var genders = report.Seats.Where(s => Speech.PersonaLines.IsFemale(s.Name) is not null)
            .ToDictionary(s => BotNames.Bare(s.Name), s => Speech.PersonaLines.IsFemale(s.Name)!.Value);
        var list = report.Messages.ToList();
        var targets = list.Select((m, i) => (m, i)).Where(x => x.m.Intent is not null).Take(AiLines).ToList();
        using var limit = new SemaphoreSlim(4);
        await Task.WhenAll(targets.Select(async x =>
        {
            await limit.WaitAsync(ct);
            try
            {
                var (m, i) = x;
                var seat = report.Seats.First(s => s.Id == m.Author);
                var draft = m.Spoken ?? m.Text;
                var passport = new Speech.SpeechPassport(
                    new Speech.Speaker(BotNames.Bare(seat.Name), "", Speech.PersonaLines.IsFemale(seat.Name) != true),
                    Speech.PersonaLines.KeyOf(seat.Name), Speech.SpokenStyle.MannerOf(seat.Id), m.Intent, draft,
                    Speech.SpeechPassport.AddresseesOf(m.Intent, genders),
                    list.Take(i).Where(p => p.Round == m.Round).TakeLast(5).Select(p => new Speech.TableLine(names[p.Author], p.Spoken ?? p.Text)).ToList(),
                    list.Take(i).Where(p => p.Author == m.Author).TakeLast(4).Select(p => p.Spoken ?? p.Text).ToList(),
                    names.Where(n => n.Key != m.Author).Select(n => n.Value).ToList());
                var (text, why) = await writer.WriteAsync(passport, ct);
                lock (list) list[i] = m with { Ai = text ?? "не принято: " + why };
            }
            finally { limit.Release(); }
        }));
        return list;
    }

    private async Task<SandboxRun> ReadAsync(Guid admin, Guid id, CancellationToken ct)
    {
        admins.RequireAdmin(admin);
        return await db.SandboxRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw AppException.NotFound("Прогон не найден.");
    }

    public async Task<SandboxReplay> ReplayAsync(Guid admin, Guid id, CancellationToken ct)
    {
        var run = await ReadAsync(admin, id, ct);
        return new(GameJson.Deserialize<SandboxSummary>(run.Summary), GameJson.Deserialize<SandboxReport>(run.Report).Seats);
    }

    public async Task<SandboxStep> StepAsync(Guid admin, Guid id, int index, Guid? viewer, bool reveal, CancellationToken ct)
    {
        var run = await ReadAsync(admin, id, ct);
        var report = GameJson.Deserialize<SandboxReport>(run.Report);
        if (index < 0 || index >= report.Frames.Count || (viewer is not null && report.Seats.All(s => s.Id != viewer)))
            throw AppException.Validation("Неверный шаг или участник.");
        var frame = report.Frames[index];
        return new(index, report.Frames.Count, frame.Action, frame.Actor, frame.Command, GameProjection.For(frame.State, viewer),
            frame.Observation, reveal ? frame.State.Truth : null,
            reveal ? frame.State.Players.ToDictionary(p => p.Id, p => p.Role.ToString()) : null,
            report.Messages.Take(frame.MessageCount).ToList());
    }

    public async Task DeleteAsync(Guid admin, Guid id, CancellationToken ct)
    {
        admins.RequireAdmin(admin);
        await db.SandboxRuns.Where(r => r.Id == id).ExecuteDeleteAsync(ct);
    }
}
