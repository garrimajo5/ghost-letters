namespace GhostLetters.Infrastructure.Bots;

/// <summary>Имена ботов: в нике всегда «Бот …», а за столом их зовут без приставки — «Ватсон», «Пуаро».</summary>
public static class BotNames
{
    public const string Prefix = "Бот ";

    /// <summary>«Бот Ватсон» → «Ватсон»; ник человека — как есть.</summary>
    public static string Bare(string name) => name.StartsWith(Prefix, StringComparison.Ordinal) ? name[Prefix.Length..] : name;
}
