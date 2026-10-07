namespace GhostLetters.Domain.Roles;

/// <summary>
/// Состав ролей по числу игроков — таблица из правил плюс опции лобби.
/// 4–6: Призрак, Убийца, Детективы; 7–9: + Сообщник и Свидетель;
/// 10–12: + второй Сообщник и Эксперт. 2–3 игрока — всегда кооператив.
/// </summary>
public static class RoleTable
{
    public const int MinPlayers = 2;
    public const int MaxPlayers = 12;
    public const int MinCompetitivePlayers = 4;
    public const int MinImitatorPlayers = 5;
    public const int MinBlackmailerPlayers = 8;

    /// <summary>Роли для раздачи (без перемешивания), порядок стабильный.</summary>
    public static IReadOnlyList<Role> Compose(int players, RoleOptions? options = null)
    {
        options ??= RoleOptions.Default;
        if (players is < MinPlayers or > MaxPlayers)
        {
            throw new ArgumentOutOfRangeException(nameof(players), players,
                $"Игроков должно быть от {MinPlayers} до {MaxPlayers}.");
        }

        if (!options.KillerEnabled || players < MinCompetitivePlayers)
        {
            return Build(players, accomplices: 0, witness: false, expert: false, blackmailer: false,
                imitator: false, killer: false);
        }

        var accomplices = players >= 10 ? 2 : players >= 7 ? 1 : 0;
        var witness = options.UseWitness && players >= 7;
        var expert = options.UseExpert && players >= 10;
        var blackmailer = options.UseBlackmailer && players >= MinBlackmailerPlayers;

        var imitator = false;
        if (options.Imitator != ImitatorMode.None)
        {
            if (players < MinImitatorPlayers)
            {
                throw new ArgumentException(
                    $"Подражатель доступен с {MinImitatorPlayers} игроков.", nameof(options));
            }

            if (options.Imitator == ImitatorMode.ReplaceAccomplice)
            {
                if (accomplices == 0)
                {
                    throw new ArgumentException(
                        "Подражатель не может заменить Сообщника: при таком числе игроков Сообщников нет.",
                        nameof(options));
                }

                accomplices--;
            }

            imitator = true;
        }

        var composition = Build(players, accomplices, witness, expert, blackmailer, imitator, killer: true);
        if (composition.Count(r => r == Role.Detective) < 1)
        {
            throw new ArgumentException("Не осталось ни одного Детектива — уберите часть ролей.", nameof(options));
        }

        return composition;
    }

    /// <summary>Кооператив — если в составе нет Убийцы.</summary>
    public static bool IsCooperative(IReadOnlyCollection<Role> roles) => !roles.Contains(Role.Killer);

    private static List<Role> Build(int players, int accomplices, bool witness, bool expert, bool blackmailer,
        bool imitator, bool killer)
    {
        var roles = new List<Role>(players) { Role.Ghost };
        if (killer)
        {
            roles.Add(Role.Killer);
        }

        roles.AddRange(Enumerable.Repeat(Role.Accomplice, accomplices));
        if (witness)
        {
            roles.Add(Role.Witness);
        }

        if (expert)
        {
            roles.Add(Role.Expert);
        }

        if (blackmailer)
        {
            roles.Add(Role.Blackmailer);
        }

        if (imitator)
        {
            roles.Add(Role.Imitator);
        }

        roles.AddRange(Enumerable.Repeat(Role.Detective, players - roles.Count));
        return roles;
    }
}
