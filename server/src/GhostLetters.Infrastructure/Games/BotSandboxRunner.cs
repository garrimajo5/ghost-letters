using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;

namespace GhostLetters.Infrastructure.Games;

public sealed record SandboxMessage(Guid Author, int Round, string Text, IReadOnlyList<string> Cards, IReadOnlyList<string> Notes, DateTimeOffset At);
public sealed record SandboxFrame(string Action, Guid? Actor, JsonElement? Command, GameState State, PlayerView? Observation, int MessageCount);
public sealed record SandboxSeat(Guid Id, string Name, BotPersonality Personality);
public sealed record SandboxReport(int Format, string EngineVersion, string Scenario, int Seed, string Status, string? Error,
    bool? Passed, int CorrectRows, int TotalRows, string? Winner, long ElapsedMs, IReadOnlyList<SandboxSeat> Seats,
    IReadOnlyList<SandboxMessage> Messages, IReadOnlyList<SandboxFrame> Frames);

/// <summary>Isolated engine: no users, live games, ratings, relationships or persistence services.</summary>
public static class BotSandboxRunner
{
    public static readonly string[] Scenarios = ["full-game", "clear-hints", "witness-reveal"];
    public const int MaxSteps = 600;
    private static Guid StableId(int seed, int seat) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"sandbox-v1:{seed}:{seat}"))[..16]);

    public static SandboxReport Run(string scenario, int seed, IReadOnlyList<string> deck, CardTags tags, CancellationToken ct)
    {
        if (!Scenarios.Contains(scenario)) throw new ArgumentException("Unknown scenario", nameof(scenario));
        var watch = Stopwatch.StartNew();
        var count = scenario == "witness-reveal" ? 7 : 6;
        var seats = Enumerable.Range(0, count).Select(i => {
            var preset = BotPresets.All[i % BotPresets.All.Count];
            return new SandboxSeat(StableId(seed, i), $"Бот {preset.Name} {i + 1}", preset.P.Clamped());
        }).ToList();
        var names = seats.ToDictionary(s => s.Id, s => s.Name);
        var state = GameEngine.Create(StableId(seed, -1), seats.Select(s => s.Id).ToList(), new GameSettings {
            Rounds = 3, Discussion = DiscussionMode.FreeChat, Ranked = false,
            Roles = new RoleOptions(KillerEnabled: scenario != "clear-hints")
        }, deck.Order(StringComparer.Ordinal).ToList(), seed);
        var messages = new List<SandboxMessage>();
        var frames = new List<SandboxFrame>();
        var parsedAccusations = new List<Accusation>();
        var rng = new Random(seed);
        string? error = null;
        var status = "completed";
        void Capture(string action, Guid? actor = null, GameCommand? command = null, PlayerView? observation = null) => frames.Add(new(
            action, actor, command is null ? null : JsonSerializer.SerializeToElement(command, command.GetType(), GameJson.Options),
            GameJson.Deserialize<GameState>(GameJson.Serialize(state)), observation, messages.Count));
        void Say(Guid actor, string text, IReadOnlyList<string> cards, IReadOnlyList<string> notes)
        {
            messages.Add(new(actor, state.Round, text, cards, notes, DateTimeOffset.UnixEpoch.AddSeconds(messages.Count * 5)));
            parsedAccusations.AddRange(AccusationReader.Read(actor, text, names));
        }

        if (scenario == "clear-hints")
        {
            // Curated, real-image pairs: two roses, two books, two cats, knife and knife.
            var pairs = new[] { ("orig_0206", "orig_0296"), ("orig_0046", "orig_0131"), ("orig_0350", "orig_0639"), ("orig_0233", "orig_0674") };
            var reserved = pairs.SelectMany(p => new[] { p.Item1, p.Item2 }).ToHashSet();
            if (reserved.Any(c => !deck.Contains(c) || tags.Of(c).Count == 0))
            {
                status = "invalid-scenario"; error = "Для учебной задачи нужны включённые карты пар роз, книг, кошек и ножей с разметкой.";
            }
            else
            {
                state.Truth = [];
                foreach (var (row, index) in state.Board.Select((r, i) => (r, i)))
                {
                    ct.ThrowIfCancellationRequested();
                    var (truth, hint) = pairs[index];
                    // Distractors have the weakest relation to all hints, across the participating personalities.
                    var distractors = deck.Where(c => !reserved.Contains(c)).OrderBy(c => seats.Max(seat =>
                        pairs.Sum(pair => tags.Similarity(pair.Item2, c, seat.Personality.Attention, seat.Personality.Details, seat.Personality.SecondaryMeanings))))
                        .ThenBy(c => c, StringComparer.Ordinal).Take(state.Settings.Columns - 1).ToList();
                    var column = rng.Next(state.Settings.Columns);
                    distractors.Insert(column, truth);
                    row.Cards.Clear(); row.Cards.AddRange(distractors);
                    foreach (var card in distractors) reserved.Add(card);
                    state.Truth.Add(column);
                    state.Hints.Add(new HintGroup { Round = index + 1, Cards = [hint] });
                }
                foreach (var p in state.Players) p.Hand.RemoveAll(reserved.Contains);
                state.Deck.RemoveAll(reserved.Contains);
                state.TotalRounds = 4; state.Round = 4; state.Phase = Phase.Discussion;
            }
        }
        else if (scenario == "witness-reveal")
        {
            state.Truth = state.Board.Select(_ => 0).ToList();
            state.Phase = Phase.Hunt; state.Round = state.TotalRounds; state.Solved = true;
            var witness = state.Players.Single(p => p.Role == Role.Witness);
            Say(witness.Id, "Я Свидетель.", [], []);
        }
        Capture("Начальное состояние");
        for (var step = 0; status == "completed" && state.Result is null && step < MaxSteps; step++)
        {
            ct.ThrowIfCancellationRequested();
            if (watch.Elapsed > TimeSpan.FromSeconds(15)) { status = "limit"; error = "Превышен лимит времени симуляции."; break; }
            var moved = false;
            foreach (var seat in seats.OrderBy(_ => rng.Next()))
            {
                var view = GameProjection.For(state, seat.Id);
                if (view.AllowedCommands.Count == 0 && state.Phase != Phase.Discussion) continue;
                var mind = Mind(seat, view, names, messages, parsedAccusations);
                if (state.Phase == Phase.Discussion && !view.Players.Single(p => p.Id == seat.Id).IsGhost)
                {
                    var discussion = messages.Where(m => m.Round == state.Round).Select(m => new DiscussionLine(m.Author, m.Text, m.Cards, m.At)).ToList();
                    var own = discussion.Where(m => m.Author == seat.Id).ToList();
                    var mayReply = own.Count == 1 && discussion.Any(m => m.Author != seat.Id && m.At > own[0].At);
                    var line = state.Round >= state.TotalRounds
                        ? BotDiscussion.Compose(view, tags, mind, discussion, rng)
                        : own.Count == 0 ? BotPlayer.Say(view, rng, tags, mind)
                        : mayReply ? BotPlayer.Reply(view, rng, tags, mind) : null;
                    if (line is { } said)
                    {
                        Say(seat.Id, said.Text, said.Cards, said.Notes);
                        Capture("Сообщение", seat.Id, observation: view); moved = true; break;
                    }
                }
                var command = BotPlayer.Decide(view, rng, tags, mind);
                if (command is null) continue;
                // Do not let readiness end discussion before all investigators have spoken.
                if (command is ReadyNextRound && seats.Any(s => s.Id != state.Ghost.Id && !messages.Any(m => m.Author == s.Id && m.Round == state.Round))) continue;
                try { GameEngine.Execute(state, seat.Id, command); }
                catch (GameRuleException e) { status = "invalid-command"; error = e.Message; }
                Capture(command.GetType().Name, seat.Id, command, view); moved = true; break;
            }
            if (status != "completed") break;
            if (!moved) { status = "stalled"; error = "Ни один бот не смог сделать ход."; break; }
        }
        if (status == "completed" && state.Result is null) { status = "limit"; error = "Превышен лимит ходов."; }
        var rows = state.VoteOutcomes.Where(o => o.Kind == VoteStageKind.Row).ToList();
        bool? passed = status != "completed" ? false : scenario switch {
            "clear-hints" => rows.Count == state.Board.Count && rows.All(r => r.Correct),
            "witness-reveal" => state.Hunt is { Success: true } h && state.Player(h.Target!.Value).Role == Role.Witness,
            _ => null
        };
        if (status == "invalid-scenario") passed = null;
        return new(1, typeof(BotSandboxRunner).Assembly.ManifestModule.ModuleVersionId.ToString(), scenario, seed, status, error,
            passed, rows.Count(r => r.Correct), rows.Count, state.Result?.Side.ToString(), watch.ElapsedMilliseconds, seats, messages, frames);
    }

    private static BotMind Mind(SandboxSeat seat, PlayerView view, IReadOnlyDictionary<Guid, string> names, IReadOnlyList<SandboxMessage> messages, IReadOnlyList<Accusation> parsedAccusations)
    {
        var board = view.Board.SelectMany(r => r.Cards).ToHashSet();
        var others = messages.Where(m => m.Author != seat.Id).ToList();
        var opinions = others.SelectMany(m => TableReasoning.Read(m.Author, m.Cards, m.Notes, board))
            .GroupBy(o => (o.Author, o.CardId, o.IsCheck)).Select(g => g.Last()).ToList();
        var accusations = parsedAccusations.Where(a => a.Author != seat.Id)
            .GroupBy(a => (a.Author, a.Target)).Select(g => g.Last()).ToList();
        return new(seat.Personality, new Dictionary<Guid, PlayerHistory>(), opinions, names, accusations,
            BotService.Breadth(others.Select(m => (m.Author, m.Notes))),
            RoleClaims: PublicRoleClaims.Read(others.Select(m => (m.Author, m.Text))));
    }
}
