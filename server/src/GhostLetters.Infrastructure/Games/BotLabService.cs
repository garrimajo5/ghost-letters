using GhostLetters.Application;
using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Infrastructure.Games;

public sealed record LabRow(string Category, IReadOnlyList<string> Cards);
public sealed record LabHint(int Round, IReadOnlyList<string> Cards);

/// <param name="Players">Состав по правилам Озона (поле, раунды): 3–11.</param>
/// <param name="Seed">Номер раздачи; null — случайный.</param>
public sealed record LabDealRequest(int Players = 6, int? Seed = null);

/// <param name="Pool">Карты на руках у игроков — из них в партии приходят письма (кандидаты в подсказки).</param>
public sealed record LabDeal(int Seed, int Players, int Rounds, IReadOnlyList<LabRow> Board, IReadOnlyList<int> Truth,
    IReadOnlyList<string> Pool, IReadOnlyDictionary<string, string> Names);

/// <param name="Bots">Имена готовых характеров (Пуаро, Марпл…); пусто — все.</param>
public sealed record LabThinkRequest(IReadOnlyList<LabRow> Board, IReadOnlyList<LabHint> Hints, IReadOnlyList<int>? Truth = null,
    IReadOnlyList<string>? Bots = null, int Rounds = 4);

/// <summary>Как подсказка связана с картой глазами бота.</summary>
public sealed record LabLink(string Hint, double Similarity, string Why);

/// <param name="Belief">Итоговая вероятность истины (как бот видит ряд).</param>
/// <param name="Rowwise">По каждому ряду отдельно — до общей теории.</param>
/// <param name="Theory">Общая теория (все подсказки вместе); null — теория выключена.</param>
public sealed record LabCard(string Card, double Belief, double Rowwise, double? Theory, double Evidence, IReadOnlyList<LabLink> Links);
public sealed record LabRowThought(string Category, int Vote, bool? Correct, IReadOnlyList<LabCard> Cards);
public sealed record LabThought(string Bot, string Color, string About, IReadOnlyList<LabRowThought> Rows,
    IReadOnlyList<string> TheoryBest, IReadOnlyList<string> Unexplained, int? Correct)
{
    /// <summary>Как лучшая версия бота читает каждую подсказку (на какую истину, прямо, повтором, парой…).</summary>
    public IReadOnlyList<HintExplanation> Explanations { get; init; } = [];
}
public sealed record LabResult(IReadOnlyList<LabThought> Bots, IReadOnlyDictionary<string, string> Names);

/// <param name="Candidates">Письма, из которых выбирает Призрак (например, кандидаты раунда); пусто — любые карты пула.</param>
public sealed record LabSuggestRequest(IReadOnlyList<LabRow> Board, IReadOnlyList<int> Truth, IReadOnlyList<LabHint> Hints,
    IReadOnlyList<string> Candidates, int Round = 1, int Rounds = 4);
public sealed record LabSuggestion(string Card, bool Pick, double TrueMatch, double FalseMatch, string Reason);

/// <summary>Завершённая партия, перенесённая в лабораторию: поле, истина, подсказки и пул писем как были за столом.</summary>
public sealed record LabGame(Guid GameId, DateTimeOffset? FinishedAt, int Players, int Rounds, IReadOnlyList<LabRow> Board,
    IReadOnlyList<int> Truth, IReadOnlyList<LabHint> Hints, IReadOnlyList<string> Pool, IReadOnlyDictionary<string, string> Names);

/// <summary>
/// Лаборатория мыслей ботов: своя раскладка (вручную или раздачей), подсказки — и что о каждом ряде думает каждый
/// характер, без партии, речи и озвучки. Решения — те же функции, что за столом (журнал, общая теория, голос).
/// </summary>
public sealed class BotLabService(BotAdminService admins, CardCatalog catalog, CardTagStore tagStore, GhostLettersDbContext db)
{
    private static readonly Guid Ghost = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Me = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid[] Others = Enumerable.Range(1, 4).Select(i => new Guid($"00000000-0000-0000-0000-00000000010{i}")).ToArray();

    public async Task<IReadOnlyDictionary<string, string>> NamesAsync(Guid admin, CancellationToken ct)
    {
        admins.RequireAdmin(admin);
        var tags = await tagStore.LoadAsync(ct);
        var deck = await catalog.DeckAsync(LobbySettings.AllCardSets, ct);
        return deck.ToDictionary(c => c, c => tags.NameOf(c) ?? c);
    }

    public async Task<LabDeal> DealAsync(Guid admin, LabDealRequest request, CancellationToken ct)
    {
        admins.RequireAdmin(admin);
        var deck = await catalog.DeckAsync(LobbySettings.AllCardSets, ct);
        var tags = await tagStore.LoadAsync(ct);
        var seed = request.Seed ?? Random.Shared.Next();
        var s = new LobbySettings { RulesPreset = "ozon" }.ResolveForPlayers(request.Players);
        var ids = Enumerable.Range(0, request.Players).Select(i => new Guid($"00000000-0000-0000-0001-{i + 1:D12}")).ToList();
        GameState state;
        try
        {
            state = GameEngine.Create(Guid.NewGuid(), ids, new GameSettings
            {
                Rounds = s.Rounds, Discussion = DiscussionMode.FreeChat, Ranked = false, Columns = s.Columns,
                UseSecretRow = s.UseSecretRow, Roles = s.Roles,
            }, CardCatalog.WithoutDuplicateNames(deck, tags, seed).Order(StringComparer.Ordinal).ToList(), seed);
        }
        catch (GameRuleException e) { throw AppException.Validation(e.Message); }
        var rng = new Random(seed);
        var board = state.Board.Select(r => new LabRow(r.Category.ToString(), r.Cards.ToList())).ToList();
        var truth = board.Select(r => rng.Next(r.Cards.Count)).ToList();
        var pool = state.Players.Where(p => p.Role != Role.Ghost).SelectMany(p => p.Hand).Distinct().ToList();
        var names = board.SelectMany(r => r.Cards).Concat(pool).Distinct().ToDictionary(c => c, c => tags.NameOf(c) ?? c);
        return new LabDeal(seed, request.Players, state.TotalRounds, board, truth, pool, names);
    }

    /// <summary>Перенос завершённой партии в лабораторию: поле, истина, подсказки по раундам и пул писем игроков.</summary>
    public async Task<LabGame> GameAsync(Guid admin, Guid gameId, CancellationToken ct)
    {
        admins.RequireAdmin(admin);
        var game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId, ct)
            ?? throw AppException.NotFound("Партия не найдена.");
        var state = GameStore.Read(game);
        if (state.Truth is not { } truth || state.Board.Count == 0)
            throw AppException.Validation("В этой партии нет разгадки — перенести в лабораторию нельзя.");
        var tags = await tagStore.LoadAsync(ct);
        var board = state.Board.Select(r => new LabRow(r.Category.ToString(), r.Cards.ToList())).ToList();
        // Подсказки с реальными номерами раундов (0 — зацепка Призрака до первого раунда).
        var hints = state.Hints.Where(h => h.Cards.Count > 0).Select(h => new LabHint(h.Round, h.Cards.ToList())).ToList();
        var pool = state.Letters.Select(l => l.CardId)
            .Concat(state.Players.Where(p => p.Role != Role.Ghost).SelectMany(p => p.Hand))
            .Where(c => !state.Board.Any(r => r.Cards.Contains(c))).Distinct().ToList();
        var names = board.SelectMany(r => r.Cards).Concat(pool).Concat(hints.SelectMany(h => h.Cards)).Distinct()
            .ToDictionary(c => c, c => tags.NameOf(c) ?? c);
        return new LabGame(game.Id, game.FinishedAt, state.Players.Count, state.TotalRounds, board, truth.ToList(), hints, pool, names);
    }

    public async Task<LabResult> ThinkAsync(Guid admin, LabThinkRequest request, CancellationToken ct)
    {
        admins.RequireAdmin(admin);
        var tags = await tagStore.LoadAsync(ct);
        return Think(request, tags);
    }

    public async Task<IReadOnlyList<LabSuggestion>> SuggestAsync(Guid admin, LabSuggestRequest request, CancellationToken ct)
    {
        admins.RequireAdmin(admin);
        var tags = await tagStore.LoadAsync(ct);
        return Suggest(request, tags);
    }

    private static List<BoardRowView> Board(IReadOnlyList<LabRow> rows)
    {
        if (rows.Count == 0 || rows.Count > 5 || rows.Any(r => r.Cards.Count is < 2 or > 8))
            throw AppException.Validation("Поле: от 1 до 5 рядов, в ряду от 2 до 8 карт.");
        if (rows.SelectMany(r => r.Cards).Distinct().Count() != rows.Sum(r => r.Cards.Count))
            throw AppException.Validation("Карты на поле повторяются.");
        return rows.Select(r => new BoardRowView(Enum.TryParse<Category>(r.Category, out var c) ? c
            : throw AppException.Validation("Неизвестный ряд: " + r.Category), r.Cards)).ToList();
    }

    private static List<PlayerInfoView> Players(Role me) =>
    [
        new(Ghost, 0, true, Role.Ghost, false, 5),
        new(Me, 1, false, me, false, 5),
        .. Others.Select((id, i) => new PlayerInfoView(id, i + 2, false, null, false, 5)),
    ];

    private static Dictionary<Guid, string> Names() =>
        new[] { Ghost, Me }.Concat(Others).Select((id, i) => (id, i == 0 ? "Призрак" : $"Игрок {i}")).ToDictionary(x => x.id, x => x.Item2);

    /// <summary>Мысли каждого характера о рядах: вероятности, теория, голос и связи подсказок с картами.</summary>
    public static LabResult Think(LabThinkRequest request, CardTags tags)
    {
        var board = Board(request.Board);
        var boardSet = board.SelectMany(r => r.Cards).ToHashSet();
        var hints = request.Hints.Where(h => h.Cards.Count > 0)
            .Select(h => new HintGroupView(h.Round, h.Cards.Where(c => !boardSet.Contains(c)).Distinct().ToList())).ToList();
        var round = Math.Clamp(hints.Select(h => h.Round).DefaultIfEmpty(1).Max(), 1, Math.Max(1, request.Rounds));
        var presets = BotPresets.All.Where(p => request.Bots is not { Count: > 0 } b || b.Contains(p.Name)).ToList();
        var hintCards = hints.SelectMany(h => h.Cards).Distinct().ToList();
        var thoughts = new List<LabThought>();
        foreach (var preset in presets)
        {
            var p = preset.P.Clamped();
            var view = new PlayerView(Guid.NewGuid(), 1, Phase.Discussion, round, Math.Max(round, request.Rounds), DiscussionMode.FreeChat,
                board, hints, 0, Players(Role.Detective), new MeView(Me, Role.Detective, [], []), null, 0, null, null, null, null, [], [], null);
            var mind = BotService.BuildMind(Me, p, new Dictionary<Guid, PlayerHistory>(), [], boardSet, Names());
            var journal = BotPlayer.Journal(view, tags, mind)!;
            var theory = BotPlayer.Theory(view, tags, mind);
            var votes = BotPlayer.Plan(view, tags, mind, new Random(1));
            var rows = board.Select((r, i) =>
            {
                bool? ok = request.Truth is { } t && i < t.Count ? votes[i] == t[i] : null;
                var cards = r.Cards.Select(c => new LabCard(c, journal.Truth.GetValueOrDefault(c),
                    theory?.Before.GetValueOrDefault(c) ?? journal.Truth.GetValueOrDefault(c), theory?.Truth.GetValueOrDefault(c),
                    journal.Evidence.GetValueOrDefault(c),
                    hintCards.Select(h => new LabLink(h, tags.Similarity(h, c, p.Attention, p.Details, p.SecondaryMeanings),
                            tags.Explain(h, c, p.Attention, p.Details, p.SecondaryMeanings)))
                        .Where(l => l.Similarity >= .05).OrderByDescending(l => l.Similarity).Take(3).ToList())).ToList();
                return new LabRowThought(r.Category.ToString(), votes[i], ok, cards);
            }).ToList();
            thoughts.Add(new LabThought(preset.Name, preset.Color, preset.About, rows, theory?.Best ?? [], theory?.Unexplained ?? [],
                request.Truth is null ? null : rows.Count(r => r.Correct == true)) { Explanations = theory?.Explanations ?? [] });
        }
        var names = boardSet.Concat(hintCards).ToDictionary(c => c, c => tags.NameOf(c) ?? c);
        return new LabResult(thoughts, names);
    }

    /// <summary>Что открыл бы Призрак-бот из кандидатов: те же правила, что за столом, с причиной по каждому письму.</summary>
    public static IReadOnlyList<LabSuggestion> Suggest(LabSuggestRequest request, CardTags tags)
    {
        var board = Board(request.Board);
        if (request.Truth.Count != board.Count || request.Truth.Select((t, i) => t < 0 || t >= board[i].Cards.Count).Any(x => x))
            throw AppException.Validation("Истина задана не для каждого ряда.");
        var boardSet = board.SelectMany(r => r.Cards).ToHashSet();
        var candidates = request.Candidates.Where(c => !boardSet.Contains(c)).Distinct().ToList();
        if (candidates.Count == 0) throw AppException.Validation("Нет писем-кандидатов.");
        var hints = request.Hints.Where(h => h.Cards.Count > 0).Select(h => new HintGroupView(h.Round, h.Cards)).ToList();
        var round = Math.Clamp(request.Round, 1, Math.Max(1, request.Rounds));
        var view = new PlayerView(Guid.NewGuid(), 1, Phase.GhostPick, round, Math.Max(round, request.Rounds), DiscussionMode.FreeChat,
            board, hints, 0, Players(Role.Detective), new MeView(Ghost, Role.Ghost, [], []), request.Truth, candidates.Count, candidates,
            null, null, null, [], [], null);
        var mind = BotService.BuildMind(Ghost, BotPersonality.Default, new Dictionary<Guid, PlayerHistory>(), [], boardSet, Names());
        var picked = GhostCluePolicy.Choose(view, tags, mind, candidates).ToHashSet();
        return candidates.Select(c => (c, a: GhostCluePolicy.Assess(view, tags, mind, c)))
            .Select(x => new LabSuggestion(x.c, picked.Contains(x.c), x.a.TrueMatch, x.a.FalseMatch, x.a.Reason))
            .OrderByDescending(s => s.Pick).ThenByDescending(s => s.TrueMatch - s.FalseMatch).ToList();
    }
}
