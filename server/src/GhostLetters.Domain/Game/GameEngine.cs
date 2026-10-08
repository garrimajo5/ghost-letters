using GhostLetters.Domain.Roles;
using GhostLetters.Domain.Rules;

namespace GhostLetters.Domain.Game;

/// <summary>
/// Правила партии: создание, команды игроков и таймауты фаз.
/// Чистая логика без БД и часов; вся случайность — от сида партии и её версии.
/// </summary>
public static partial class GameEngine
{
    public static GameState Create(Guid gameId, IReadOnlyList<Guid> playerIds, GameSettings settings,
        IReadOnlyList<string> deck, int seed)
    {
        if (playerIds.Distinct().Count() != playerIds.Count)
        {
            throw GameRuleException.Validation("Игроки повторяются.");
        }

        var cards = deck.Distinct().ToList();
        settings.Validate(playerIds.Count, cards.Count);

        var rng = new Random(seed);
        var roles = RoleTable.Compose(playerIds.Count, settings.Roles).ToList();
        Shuffle(roles, rng);

        // Хост заранее назначил Призрака — меняем его роль местами с выпавшей Призраку.
        if (settings.GhostPlayerId is { } ghostId && playerIds.ToList().IndexOf(ghostId) is var seat and >= 0)
        {
            var ghostSeat = roles.IndexOf(Role.Ghost);
            if (ghostSeat >= 0)
            {
                (roles[seat], roles[ghostSeat]) = (roles[ghostSeat], roles[seat]);
            }
        }
        Shuffle(cards, rng);

        var state = new GameState
        {
            Id = gameId,
            Seed = seed,
            Settings = settings,
            TotalRounds = settings.RoundsFor(playerIds.Count),
            Phase = Phase.RoleReveal,
            Players = playerIds.Select((id, seat) => new PlayerState { Id = id, Seat = seat, Role = roles[seat] }).ToList(),
            Deck = cards,
        };

        foreach (var category in settings.Categories)
        {
            var row = new BoardRow { Category = category };
            for (var i = 0; i < settings.Columns; i++)
            {
                row.Cards.Add(TakeFromDeck(state));
            }

            state.Board.Add(row);
        }

        return state;
    }

    /// <summary>Применить команду игрока. Бросает GameRuleException, если по правилам нельзя.</summary>
    public static IReadOnlyList<GameEvent> Execute(GameState state, Guid actorId, GameCommand command)
    {
        var actor = state.Player(actorId);
        var events = new List<GameEvent>();

        switch (command)
        {
            case AckRole:
                RequirePhase(state, Phase.RoleReveal);
                AckRoleFor(state, actor, events);
                break;
            case ChooseTruth choose:
                RequirePhase(state, Phase.Night);
                if (actor.Id != TruthChooser(state).Id)
                {
                    throw GameRuleException.NotAllowed("Истинные улики выбирает Убийца (без Убийцы — Призрак).");
                }

                ApplyTruth(state, choose.Columns, events);
                break;
            case GiveFirstClue clue:
                RequirePhase(state, Phase.FirstClue);
                RequireGhost(state, actor);
                ApplyFirstClue(state, clue.CardId, events);
                break;
            case SendLetter send:
                RequirePhase(state, Phase.Mailbox);
                ApplyLetter(state, actor, send.CardIds, events);
                break;
            case RevealHints reveal:
                RequirePhase(state, Phase.GhostPick);
                RequireGhost(state, actor);
                ApplyReveal(state, reveal.CardIds, events);
                break;
            case Discard discard:
                if (state.Phase is not (Phase.GhostPick or Phase.Refill))
                {
                    throw GameRuleException.WrongPhase(Phase.Refill, state.Phase);
                }

                ApplyDiscard(state, actor, discard.CardId, events);
                break;
            case EndTurn:
                RequireRadioSpeaker(state, actor);
                AdvanceSpeaker(state, events);
                break;
            case GiveFloor floor:
                RequireRadioSpeaker(state, actor);
                var target = state.Player(floor.To);
                if (target.Role == Role.Ghost || target.Id == actor.Id)
                {
                    throw GameRuleException.Validation("Слово можно дать другому игроку, кроме Призрака.");
                }

                state.FloorGrantedTo = target.Id;
                state.RaisedHands.Remove(target.Id);
                events.Add(new GameEvent("FloorGiven", actor.Id, Detail: target.Id.ToString()));
                break;
            case RaiseHand hand:
                RequirePhase(state, Phase.Discussion);
                RequireInvestigator(actor);
                if (hand.Raised)
                {
                    state.RaisedHands.Add(actor.Id);
                }
                else
                {
                    state.RaisedHands.Remove(actor.Id);
                }

                events.Add(new GameEvent(hand.Raised ? "HandRaised" : "HandLowered", actor.Id));
                break;
            case ReadyNextRound:
                RequirePhase(state, Phase.Discussion);
                RequireInvestigator(actor);
                if (state.Settings.Discussion != DiscussionMode.FreeChat)
                {
                    throw GameRuleException.NotAllowed("В режиме рации раунд идёт по кругу говорящих.");
                }

                if (state.Done.Add(actor.Id))
                {
                    events.Add(new GameEvent("ReadyNextRound", actor.Id));
                }

                if (state.Investigators.All(p => state.Done.Contains(p.Id)))
                {
                    EndDiscussion(state, events);
                }

                break;
            default:
                if (!ExecuteFinale(state, actor, command, events))
                {
                    throw GameRuleException.Validation($"Неизвестная команда {command.GetType().Name}.");
                }

                break;
        }

        state.Version++;
        return events;
    }

    /// <summary>Время фазы вышло: сервер делает ходы за тех, кто не успел.</summary>
    public static IReadOnlyList<GameEvent> Timeout(GameState state)
    {
        var events = new List<GameEvent> { new("Timeout", Detail: state.Phase.ToString()) };
        var rng = Rng(state);

        switch (state.Phase)
        {
            case Phase.RoleReveal:
                foreach (var p in state.Players.Where(x => !x.Acknowledged).ToList())
                {
                    AckRoleFor(state, p, events);
                }

                break;
            case Phase.Night:
                var columns = state.Board.Select(_ => rng.Next(state.Settings.Columns)).ToList();
                ApplyTruth(state, columns, events);
                break;
            case Phase.FirstClue:
                ApplyFirstClue(state, null, events);
                break;
            case Phase.Mailbox:
                var perPlayer = GameDefaults.LettersPerPlayer(state.Players.Count);
                foreach (var p in state.Players.Where(x => !state.Done.Contains(x.Id)).OrderBy(x => x.Seat).ToList())
                {
                    var picked = p.Hand.OrderBy(_ => rng.Next()).Take(perPlayer).ToList();
                    ApplyLetter(state, p, picked, events);
                }

                break;
            case Phase.GhostPick:
                ApplyReveal(state, [], events);
                break;
            case Phase.Refill:
                foreach (var p in state.Players.Where(x => !state.Done.Contains(x.Id)).OrderBy(x => x.Seat).ToList())
                {
                    ApplyDiscard(state, p, null, events);
                }

                break;
            case Phase.Discussion:
                if (state.Settings.Discussion == DiscussionMode.Radio)
                {
                    AdvanceSpeaker(state, events);
                }
                else
                {
                    EndDiscussion(state, events);
                }

                break;
            default:
                TimeoutFinale(state, rng, events);
                break;
        }

        state.Version++;
        return events;
    }

    /// <summary>Кто выбирает истинные улики ночью.</summary>
    public static PlayerState TruthChooser(GameState state) =>
        state.Players.FirstOrDefault(p => p.Role == Role.Killer) ?? state.Ghost;

    private static void AckRoleFor(GameState state, PlayerState player, List<GameEvent> events)
    {
        if (!player.Acknowledged)
        {
            player.Acknowledged = true;
            events.Add(new GameEvent("RoleAcknowledged", player.Id));
        }

        if (state.Players.All(p => p.Acknowledged))
        {
            SetPhase(state, Phase.Night, events);
        }
    }

    private static void ApplyTruth(GameState state, IReadOnlyList<int> columns, List<GameEvent> events)
    {
        if (columns.Count != state.Board.Count)
        {
            throw GameRuleException.Validation($"Нужно выбрать по одной улике в каждом из {state.Board.Count} рядов.");
        }

        if (columns.Any(c => c < 0 || c >= state.Settings.Columns))
        {
            throw GameRuleException.Validation("Номер столбца вне поля.");
        }

        state.Truth = columns.ToList();
        events.Add(new GameEvent("TruthChosen", OnlyFor: TruthChooser(state).Id));

        foreach (var p in state.Players.OrderBy(x => x.Seat))
        {
            Refill(state, p);
        }

        events.Add(new GameEvent("HandsDealt"));
        SetPhase(state, Phase.FirstClue, events);
    }

    private static void ApplyFirstClue(GameState state, string? cardId, List<GameEvent> events)
    {
        var ghost = state.Ghost;
        var group = new HintGroup { Round = 0 };
        if (cardId is not null)
        {
            if (!ghost.Hand.Remove(cardId))
            {
                throw GameRuleException.Validation("Такой карты нет на руке у Призрака.");
            }

            group.Cards.Add(cardId);
            Refill(state, ghost);
        }

        state.Hints.Add(group);
        events.Add(new GameEvent("FirstClue", ghost.Id, Detail: cardId));
        StartRound(state, events);
    }

    private static void ApplyLetter(GameState state, PlayerState player, IReadOnlyList<string> cardIds, List<GameEvent> events)
    {
        if (state.Done.Contains(player.Id))
        {
            throw GameRuleException.NotAllowed("Письмо в этом раунде уже отправлено.");
        }

        var required = GameDefaults.LettersPerPlayer(state.Players.Count);
        if (cardIds.Count != required || cardIds.Distinct().Count() != required)
        {
            throw GameRuleException.Validation($"Нужно отправить {required} карт(ы).");
        }

        if (cardIds.Any(c => !player.Hand.Contains(c)))
        {
            throw GameRuleException.Validation("Отправить можно только карту с руки.");
        }

        foreach (var card in cardIds)
        {
            player.Hand.Remove(card);
            state.Mailbox.Add(new Letter { CardId = card, From = player.Id });
            state.Letters.Add(new LetterRecord { Round = state.Round, From = player.Id, CardId = card });
        }

        state.Done.Add(player.Id);
        events.Add(new GameEvent("LetterSent", player.Id));

        if (state.RadioHolder is null && player.Role != Role.Ghost)
        {
            state.RadioHolder = player.Id;
            events.Add(new GameEvent("RadioTaken", player.Id));
        }

        if (state.Players.All(p => state.Done.Contains(p.Id)))
        {
            var rng = Rng(state);
            var shuffled = state.Mailbox.OrderBy(_ => rng.Next()).ToList();
            state.Mailbox.Clear();
            state.Mailbox.AddRange(shuffled);
            SetPhase(state, Phase.GhostPick, events);
        }
    }

    private static void ApplyReveal(GameState state, IReadOnlyList<string> cardIds, List<GameEvent> events)
    {
        if (cardIds.Distinct().Count() != cardIds.Count || cardIds.Any(c => state.Mailbox.All(l => l.CardId != c)))
        {
            throw GameRuleException.Validation("Открыть можно только письма из ящика, каждое один раз.");
        }

        var group = new HintGroup { Round = state.Round };
        foreach (var letter in state.Mailbox)
        {
            var revealed = cardIds.Contains(letter.CardId);
            if (revealed)
            {
                group.Cards.Add(letter.CardId);
            }
            else
            {
                state.Vanished.Add(letter.CardId);
            }

            var record = state.Letters.Last(r => r.Round == state.Round && r.CardId == letter.CardId);
            record.Revealed = revealed;
        }

        state.Hints.Add(group);
        state.Mailbox.Clear();
        events.Add(new GameEvent("HintsRevealed", state.Ghost.Id, Detail: group.Cards.Count.ToString()));

        // Решения по сбросу, принятые во время выкладки, сохраняются.
        state.Phase = Phase.Refill;
        events.Add(new GameEvent("PhaseChanged", Detail: Phase.Refill.ToString()));
        if (state.Players.All(p => state.Done.Contains(p.Id)))
        {
            StartDiscussion(state, events);
        }
    }

    private static void ApplyDiscard(GameState state, PlayerState player, string? cardId, List<GameEvent> events)
    {
        if (state.Done.Contains(player.Id))
        {
            throw GameRuleException.NotAllowed("Сброс в этом раунде уже сделан.");
        }

        if (cardId is not null)
        {
            if (!player.Hand.Remove(cardId))
            {
                throw GameRuleException.Validation("Такой карты нет на руке.");
            }

            state.DiscardPile.Add(cardId);
            player.Discarded.Add(cardId);
        }

        Refill(state, player);
        state.Done.Add(player.Id);
        events.Add(new GameEvent(cardId is null ? "HandKept" : "CardDiscarded", player.Id));

        if (state.Phase == Phase.Refill && state.Players.All(p => state.Done.Contains(p.Id)))
        {
            StartDiscussion(state, events);
        }
    }

    private static void StartRound(GameState state, List<GameEvent> events)
    {
        state.Round++;
        state.Mailbox.Clear();
        state.RadioHolder = null;
        state.SpeakingOrder = [];
        state.SpeakerIndex = 0;
        state.FloorGrantedTo = null;
        state.RaisedHands.Clear();
        SetPhase(state, Phase.Mailbox, events);
    }

    private static void StartDiscussion(GameState state, List<GameEvent> events)
    {
        var order = state.Investigators.Select(p => p.Id).ToList();
        if (state.RadioHolder is { } holder)
        {
            var start = order.IndexOf(holder);
            order = order.Skip(start).Concat(order.Take(start)).ToList();
        }

        state.SpeakingOrder = order;
        state.SpeakerIndex = 0;
        state.FloorGrantedTo = null;
        SetPhase(state, Phase.Discussion, events);
        if (state.Settings.Discussion == DiscussionMode.Radio && state.CurrentSpeaker is { } speaker)
        {
            events.Add(new GameEvent("SpeakerChanged", speaker));
        }
    }

    private static void AdvanceSpeaker(GameState state, List<GameEvent> events)
    {
        state.SpeakerIndex++;
        state.FloorGrantedTo = null;
        if (state.CurrentSpeaker is { } next)
        {
            state.RaisedHands.Remove(next);
            events.Add(new GameEvent("SpeakerChanged", next));
        }
        else
        {
            EndDiscussion(state, events);
        }
    }

    private static void EndDiscussion(GameState state, List<GameEvent> events)
    {
        if (state.Round >= state.TotalRounds)
        {
            StartVoting(state, events);
        }
        else
        {
            StartRound(state, events);
        }
    }

    private static void SetPhase(GameState state, Phase phase, List<GameEvent> events)
    {
        state.Phase = phase;
        state.Done.Clear();
        events.Add(new GameEvent("PhaseChanged", Detail: phase.ToString()));
    }

    private static void Refill(GameState state, PlayerState player)
    {
        while (player.Hand.Count < state.Settings.HandSize)
        {
            if (state.Deck.Count == 0)
            {
                if (state.DiscardPile.Count == 0)
                {
                    return;
                }

                var rng = Rng(state);
                state.Deck.AddRange(state.DiscardPile.OrderBy(_ => rng.Next()));
                state.DiscardPile.Clear();
            }

            player.Hand.Add(TakeFromDeck(state));
        }
    }

    private static string TakeFromDeck(GameState state)
    {
        var card = state.Deck[0];
        state.Deck.RemoveAt(0);
        return card;
    }

    private static void RequirePhase(GameState state, Phase phase)
    {
        if (state.Phase != phase)
        {
            throw GameRuleException.WrongPhase(phase, state.Phase);
        }
    }

    private static void RequireGhost(GameState state, PlayerState actor)
    {
        if (actor.Id != state.Ghost.Id)
        {
            throw GameRuleException.NotAllowed("Это действие Призрака.");
        }
    }

    private static void RequireInvestigator(PlayerState actor)
    {
        if (actor.Role == Role.Ghost)
        {
            throw GameRuleException.NotAllowed("Призрак не участвует в обсуждении.");
        }
    }

    private static void RequireRadioSpeaker(GameState state, PlayerState actor)
    {
        RequirePhase(state, Phase.Discussion);
        if (state.Settings.Discussion != DiscussionMode.Radio)
        {
            throw GameRuleException.NotAllowed("Рация выключена в этой партии.");
        }

        if (state.CurrentSpeaker != actor.Id)
        {
            throw GameRuleException.NotYourTurn("Сейчас говорит другой игрок.");
        }
    }

    /// <summary>Детерминированный генератор для текущего шага партии.</summary>
    private static Random Rng(GameState state) => new(unchecked((state.Seed * 397) ^ (state.Version * 7919) ^ (state.Round * 104729)));

    private static void Shuffle<T>(IList<T> list, Random rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
