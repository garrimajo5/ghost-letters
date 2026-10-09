using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Domain.Rules;

namespace GhostLetters.Domain.Tests.Game;

/// <summary>Помощники для прогона партии в тестах.</summary>
internal static class TestGame
{
    public static readonly IReadOnlyList<string> Deck = Enumerable.Range(1, 300).Select(i => $"c{i:000}").ToList();

    public static IReadOnlyList<Guid> PlayerIds(int count) =>
        Enumerable.Range(1, count).Select(i => new Guid(i, 0, 0, new byte[8])).ToList();

    public static GameState Create(int players = 7, GameSettings? settings = null, int seed = 42, IReadOnlyList<string>? deck = null) =>
        GameEngine.Create(Guid.NewGuid(), PlayerIds(players), settings ?? new GameSettings(), deck ?? Deck, seed);

    public static PlayerState WithRole(this GameState state, Role role) => state.Players.First(p => p.Role == role);

    public static IReadOnlyList<GameEvent> Run(this GameState state, PlayerState actor, GameCommand command) =>
        GameEngine.Execute(state, actor.Id, command);

    public static void AckAll(this GameState state)
    {
        foreach (var p in state.Players)
        {
            state.Run(p, new AckRole());
        }
    }

    public static void ToFirstClue(this GameState state)
    {
        state.AckAll();
        state.Run(GameEngine.TruthChooser(state), new ChooseTruth(Enumerable.Repeat(0, state.Board.Count).ToList()));
    }

    public static void ToRound1(this GameState state)
    {
        state.ToFirstClue();
        state.Run(state.Ghost, new GiveFirstClue(null));
    }

    /// <summary>Все отправляют письма: сначала в порядке мест, начиная с указанного игрока.</summary>
    public static void SendAll(this GameState state, PlayerState? first = null)
    {
        var count = GameDefaults.LettersPerPlayer(state.Players.Count);
        var order = state.Players.OrderBy(p => p.Seat).ToList();
        if (first is not null)
        {
            order.Remove(first);
            order.Insert(0, first);
        }

        foreach (var p in order)
        {
            state.Run(p, new SendLetter(p.Hand.Take(count).ToList()));
        }
    }

    public static void KeepAll(this GameState state)
    {
        foreach (var p in state.Players.Where(x => !state.Done.Contains(x.Id)).ToList())
        {
            state.Run(p, new Discard(null));
        }
    }

    public static void TalkThrough(this GameState state)
    {
        if (state.EffectiveDiscussion == DiscussionMode.FreeChat)
        {
            foreach (var player in state.Investigators.ToList()) state.Run(player, new ReadyNextRound());
            return;
        }
        while (state.Phase == Phase.Discussion && state.CurrentSpeaker is { } speaker)
        {
            state.Run(state.Player(speaker), new EndTurn());
        }
    }

    /// <summary>Полный раунд: письма, Призрак открывает первое, все оставляют руку, все высказались.</summary>
    public static void PlayRound(this GameState state)
    {
        state.SendAll();
        state.Run(state.Ghost, new RevealHints([state.Mailbox[0].CardId]));
        state.KeepAll();
        state.TalkThrough();
    }
}
