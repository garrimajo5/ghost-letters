namespace GhostLetters.Infrastructure.Speech;

/// <summary>Голос SpeechKit: имя, подпись для кабинета, пол и амплуа (интонации).</summary>
public sealed record VoiceOption(string Id, string Label, bool Male, IReadOnlyList<string> Roles);

/// <summary>
/// Русские голоса Yandex SpeechKit (модель general, API v3). Голос бота выбирается в кабинете;
/// не выбран — назначается по id бота, чтобы у каждого бота был свой постоянный голос.
/// </summary>
public static class BotVoices
{
    public static readonly IReadOnlyList<VoiceOption> All =
    [
        new("alexander", "Александр", true, ["neutral", "good"]),
        new("anton", "Антон", true, ["neutral", "good"]),
        new("ermil", "Ермил", true, ["neutral", "good"]),
        new("filipp", "Филипп", true, []),
        new("kirill", "Кирилл", true, ["neutral", "strict", "good"]),
        new("zahar", "Захар", true, ["neutral", "good"]),
        new("madi_ru", "Мади", true, []),
        new("dasha", "Даша", false, ["neutral", "good", "friendly"]),
        new("jane", "Джейн", false, ["neutral", "good", "evil"]),
        new("julia", "Юлия", false, ["neutral", "strict"]),
        new("lera", "Лера", false, ["neutral", "friendly"]),
        new("marina", "Марина", false, ["neutral", "friendly"]),
        new("masha", "Маша", false, ["good", "strict", "friendly"]),
        new("omazh", "Омаж", false, ["neutral", "evil"]),
        new("saule_ru", "Сауле", false, ["neutral", "strict"]),
        new("zamira_ru", "Замира", false, ["neutral", "strict", "friendly"]),
        new("zhanar_ru", "Жанар", false, ["neutral", "strict", "friendly"]),
        new("yulduz_ru", "Юлдуз", false, ["neutral", "strict", "friendly"]),
    ];

    /// <summary>Подписи амплуа для кабинета.</summary>
    public static readonly IReadOnlyDictionary<string, string> RoleLabels = new Dictionary<string, string>
    {
        ["neutral"] = "спокойно", ["good"] = "радушно", ["strict"] = "строго", ["friendly"] = "дружелюбно", ["evil"] = "зловеще",
    };

    public static VoiceOption? Find(string? id) => All.FirstOrDefault(v => v.Id == id);

    /// <summary>Голос и амплуа бота: выбранные в кабинете, иначе постоянный голос по id (без шёпота и «злых» интонаций).</summary>
    public static (string Voice, string? Role) For(Guid botId, string? voice, string? role)
    {
        var chosen = Find(voice);
        if (chosen is null)
        {
            var pool = All.Where(v => v.Id is not ("madi_ru" or "saule_ru" or "zamira_ru" or "zhanar_ru" or "yulduz_ru")).ToList();
            chosen = pool[(int)(BitConverter.ToUInt32(botId.ToByteArray(), 0) % (uint)pool.Count)];
            return (chosen.Id, chosen.Roles.FirstOrDefault());
        }

        return (chosen.Id, role is not null && chosen.Roles.Contains(role) ? role : chosen.Roles.FirstOrDefault());
    }
}
