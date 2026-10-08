using GhostLetters.Domain.Roles;
using GhostLetters.Domain.Rules;

namespace GhostLetters.Domain.Game;

public sealed record BoardRowView(Category Category, IReadOnlyList<string> Cards);

public sealed record HintGroupView(int Round, IReadOnlyList<string> Cards);

/// <summary>Игрок глазами смотрящего: роль видна, только если смотрящий имеет право её знать.</summary>
public sealed record PlayerInfoView(Guid Id, int Seat, bool IsGhost, Role? KnownRole, bool HasActed, int HandCount);

public sealed record MyLetterView(int Round, string CardId, bool? Revealed);

public sealed record MeView(Guid Id, Role Role, IReadOnlyList<string> Hand, IReadOnlyList<MyLetterView> Letters, IReadOnlyList<string>? Discarded = null);

/// <summary>Всё, что видит один игрок (или экран стола, если Me == null).</summary>
public sealed record PlayerView(
    Guid GameId,
    int Version,
    Phase Phase,
    int Round,
    int TotalRounds,
    DiscussionMode Discussion,
    IReadOnlyList<BoardRowView> Board,
    IReadOnlyList<HintGroupView> Hints,
    int VanishedCount,
    IReadOnlyList<PlayerInfoView> Players,
    MeView? Me,
    IReadOnlyList<int>? Truth,
    int MailboxCount,
    IReadOnlyList<string>? MailboxForGhost,
    Guid? RadioHolder,
    Guid? CurrentSpeaker,
    Guid? FloorGrantedTo,
    IReadOnlyList<Guid> RaisedHands,
    IReadOnlyList<string> AllowedCommands,
    FinaleView? Finale,
    IReadOnlyList<TeamSuggestionView>? TeamSuggestions = null,
    IReadOnlyList<Role>? HuntRoles = null);

/// <summary>Подсказка Сообщника Убийце — видна только команде Убийцы.</summary>
public sealed record TeamSuggestionView(Guid From, IReadOnlyList<int>? Columns, Guid? Target, Role? Guess);

/// <summary>Строит проекцию состояния для игрока по матрице «кто что знает».</summary>
public static class GameProjection
{
    public static PlayerView For(GameState state, Guid? viewerId)
    {
        var viewer = viewerId is { } id ? state.Player(id) : null;

        var players = state.Players.OrderBy(p => p.Seat)
            .Select(p => new PlayerInfoView(
                p.Id,
                p.Seat,
                p.Role == Role.Ghost,
                KnownRole(state, viewer, p),
                state.Done.Contains(p.Id),
                p.Hand.Count))
            .ToList();

        MeView? me = null;
        if (viewer is not null)
        {
            var letters = state.Letters
                .Where(l => l.From == viewer.Id)
                .Select(l => new MyLetterView(l.Round, l.CardId, IsRoundRevealed(state, l.Round) ? l.Revealed : null))
                .ToList();
            me = new MeView(viewer.Id, viewer.Role, viewer.Hand.ToList(), letters, viewer.Discarded.ToList());
        }

        var isGhost = viewer?.Role == Role.Ghost;

        return new PlayerView(
            state.Id,
            state.Version,
            state.Phase,
            state.Round,
            state.TotalRounds,
            state.Settings.Discussion,
            state.Board.Select(r => new BoardRowView(r.Category, r.Cards.ToList())).ToList(),
            state.Hints.Select(h => new HintGroupView(h.Round, h.Cards.ToList())).ToList(),
            state.Vanished.Count,
            players,
            me,
            KnowsTruth(state, viewer) ? state.Truth?.ToList() : null,
            state.Mailbox.Count,
            isGhost && state.Phase == Phase.GhostPick ? state.Mailbox.Select(l => l.CardId).ToList() : null,
            state.RadioHolder,
            state.CurrentSpeaker,
            state.FloorGrantedTo,
            state.RaisedHands.ToList(),
            viewer is null ? Array.Empty<string>() : AllowedCommands(state, viewer),
            FinaleProjection.For(state, viewer),
            viewer?.Role is Role.Killer or Role.Accomplice && GameEngine.TeamSuggestPhase(state)
                ? state.TeamSuggestions.Select(kv => new TeamSuggestionView(kv.Key, kv.Value.Columns, kv.Value.Target, kv.Value.Guess)).ToList()
                : null,
            // На охоте — какие роли вообще есть в партии (состав по таблице известен всем): нельзя назвать Эксперта, если его нет.
            state.Phase == Phase.Hunt
                ? state.Players.Select(p => p.Role).Where(r => r is Role.Witness or Role.Expert).Distinct().OrderBy(r => r).ToList()
                : null);
    }

    /// <summary>Знает ли смотрящий роль игрока target.</summary>
    public static Role? KnownRole(GameState state, PlayerState? viewer, PlayerState target)
    {
        if (target.Role == Role.Ghost)
        {
            return Role.Ghost;
        }

        // После итогов роли открыты всем, включая экран стола.
        if (state.Result is not null)
        {
            return target.Role;
        }

        if (viewer is null)
        {
            return null;
        }

        if (viewer.Id == target.Id)
        {
            return target.Role;
        }

        return viewer.Role switch
        {
            // Подражателя не знает никто, Призрак видит его Детективом.
            Role.Ghost => target.Role == Role.Imitator ? Role.Detective : target.Role,
            Role.Killer when target.Role == Role.Accomplice => Role.Accomplice,
            Role.Accomplice when target.Role.IsKillerTeam() => target.Role,
            Role.Blackmailer when target.Role.IsKillerTeam() => target.Role,
            Role.Witness when target.Role == Role.Killer => Role.Killer,
            _ => null,
        };
    }

    public static bool KnowsTruth(GameState state, PlayerState? viewer) =>
        state.Truth is not null &&
        (state.Result is not null || viewer?.Role is Role.Ghost or Role.Killer or Role.Accomplice or Role.Expert);

    public static IReadOnlyList<string> AllowedCommands(GameState state, PlayerState viewer)
    {
        var done = state.Done.Contains(viewer.Id);
        var ghost = viewer.Role == Role.Ghost;
        var list = new List<string>();

        switch (state.Phase)
        {
            case Phase.RoleReveal when !viewer.Acknowledged:
                list.Add(nameof(AckRole));
                break;
            case Phase.Night when GameEngine.TruthChooser(state).Id == viewer.Id:
                list.Add(nameof(ChooseTruth));
                break;
            case Phase.Night when viewer.Role == Role.Accomplice && GameEngine.TeamSuggestPhase(state):
                list.Add(nameof(TeamSuggest));
                break;
            case Phase.FirstClue when ghost:
                list.Add(nameof(GiveFirstClue));
                break;
            case Phase.Mailbox when !done:
                list.Add(nameof(SendLetter));
                break;
            case Phase.GhostPick:
                if (ghost)
                {
                    list.Add(nameof(RevealHints));
                }

                if (!done)
                {
                    list.Add(nameof(Discard));
                }

                break;
            case Phase.Refill when !done:
                list.Add(nameof(Discard));
                break;
            case Phase.Discussion when !ghost:
                if (state.Settings.Discussion == DiscussionMode.Radio)
                {
                    if (state.CurrentSpeaker == viewer.Id)
                    {
                        list.Add(nameof(EndTurn));
                        list.Add(nameof(GiveFloor));
                    }
                    else
                    {
                        list.Add(nameof(RaiseHand));
                    }
                }
                else if (!done)
                {
                    list.Add(nameof(ReadyNextRound));
                }

                break;
            default:
                FinaleProjection.AddAllowedCommands(state, viewer, done, list);
                break;
        }

        return list;
    }

    private static bool IsRoundRevealed(GameState state, int round) =>
        state.Hints.Any(h => h.Round == round) || round < state.Round;

    /// <summary>Сколько писем кладёт каждый игрок в этой партии.</summary>
    public static int LettersPerPlayer(GameState state) => GameDefaults.LettersPerPlayer(state.Players.Count);
}
