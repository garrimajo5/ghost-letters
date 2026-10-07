namespace GhostLetters.Domain.Game;

/// <summary>Ряд улик на поле.</summary>
public enum Category
{
    Motive,
    Place,
    Method,
    Secret,
}

/// <summary>Фаза партии.</summary>
public enum Phase
{
    /// <summary>Игроки смотрят свои роли.</summary>
    RoleReveal,

    /// <summary>Убийца (или Призрак без Убийцы) выбирает истинные улики.</summary>
    Night,

    /// <summary>Призрак может выложить первую зацепку.</summary>
    FirstClue,

    /// <summary>Все отправляют письма в почтовый ящик.</summary>
    Mailbox,

    /// <summary>Призрак выбирает подсказки; игроки уже могут сбросить карту.</summary>
    GhostPick,

    /// <summary>Подсказки открыты, ждём решения по сбросу от оставшихся.</summary>
    Refill,

    /// <summary>Обсуждение по рации или свободное.</summary>
    Discussion,

    /// <summary>Финальное голосование: ряды по очереди, затем Убийца.</summary>
    Voting,

    /// <summary>Ничья: обсуждение перед переголосованием.</summary>
    VoteTie,

    /// <summary>Убийца ищет Свидетеля или Эксперта.</summary>
    Hunt,

    /// <summary>Дело не раскрыто: Убийца ищет Шантажиста.</summary>
    BlackmailerHunt,

    /// <summary>Шантажист не найден и называет истинные улики.</summary>
    BlackmailerClaim,

    /// <summary>Лайки и выдвижение на ачивки.</summary>
    AwardNomination,

    /// <summary>Голосование за выдвижения.</summary>
    AwardVoting,

    Finished,
}

public enum VoteStageKind
{
    Row,
    Killer,
}

public enum DiscussionMode
{
    Radio,
    FreeChat,
}
