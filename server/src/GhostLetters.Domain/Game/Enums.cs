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

    /// <summary>Финальное голосование (следующий PR).</summary>
    Voting,

    Finished,
}

public enum DiscussionMode
{
    Radio,
    FreeChat,
}
