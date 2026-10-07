namespace GhostLetters.Domain.Roles;

/// <summary>Настройки состава ролей из лобби.</summary>
/// <param name="KillerEnabled">false — кооператив: только Призрак и Детективы.</param>
/// <param name="UseWitness">Свидетель по таблице (с 7 игроков); false — вместо него Детектив.</param>
/// <param name="UseExpert">Эксперт по таблице (с 10 игроков); false — вместо него Детектив.</param>
/// <param name="UseBlackmailer">Шантажист вместо одного Детектива (с 8 игроков).</param>
/// <param name="Imitator">Подражатель (с 5 игроков) вместо Детектива или Сообщника.</param>
public sealed record RoleOptions(
    bool KillerEnabled = true,
    bool UseWitness = true,
    bool UseExpert = true,
    bool UseBlackmailer = false,
    ImitatorMode Imitator = ImitatorMode.None)
{
    public static RoleOptions Default { get; } = new();
}
