namespace GhostLetters.Domain.Roles;

/// <summary>Роль игрока в партии.</summary>
public enum Role
{
    Ghost,
    Detective,
    Killer,
    Accomplice,
    Witness,
    Expert,
    Blackmailer,
    Imitator,
}

/// <summary>Кого заменяет Подражатель.</summary>
public enum ImitatorMode
{
    None,
    ReplaceDetective,
    ReplaceAccomplice,
}

public static class RoleExtensions
{
    /// <summary>Команда Убийцы: Убийца и Сообщники.</summary>
    public static bool IsKillerTeam(this Role role) => role is Role.Killer or Role.Accomplice;

    /// <summary>Побеждает вместе с Детективами.</summary>
    public static bool IsDetectiveTeam(this Role role) =>
        role is Role.Ghost or Role.Detective or Role.Witness or Role.Expert;
}
