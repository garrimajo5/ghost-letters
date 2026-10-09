using GhostLetters.Application;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using GhostLetters.Domain.Game;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Infrastructure.Games;

public sealed record SandboxRequest(string Scenario, int Seed);
public sealed record SandboxSummary(Guid Id, DateTimeOffset CreatedAt, string Scenario, int Seed, string Status,
    string? Error, bool? Passed, int CorrectRows, int TotalRows, string? Winner, long ElapsedMs, int Steps, int Messages);
public sealed record SandboxReplay(SandboxSummary Summary, IReadOnlyList<SandboxSeat> Seats);
public sealed record SandboxStep(int Index, int Total, string Action, Guid? Actor, System.Text.Json.JsonElement? Command,
    PlayerView View, PlayerView? Observation, IReadOnlyList<int>? Truth, IReadOnlyDictionary<Guid, string>? Roles, IReadOnlyList<SandboxMessage> Messages);

public sealed class SandboxGate { public SemaphoreSlim Semaphore { get; } = new(1, 1); }

public sealed class BotSandboxService(GhostLettersDbContext db, BotAdminService admins, CardCatalog catalog,
    CardTagStore tagStore, SandboxGate gate, TimeProvider time)
{
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
